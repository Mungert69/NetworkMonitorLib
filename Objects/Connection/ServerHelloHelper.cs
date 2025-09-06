using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections.Generic;

using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Security;

using NetworkMonitor.Objects;

class ServerHelloHelper
{
    private readonly Dictionary<int, AlgorithmInfo> _algosById;
    private readonly Dictionary<string, AlgorithmInfo> _algosByNameLower;

    private StringBuilder _sb = new StringBuilder();
    public StringBuilder Sb { get => _sb; set => _sb = value; }

    // HRR "random" marker defined by TLS 1.3
    private static readonly byte[] HrrRandom =
        Org.BouncyCastle.Utilities.Encoders.Hex.Decode(
            "cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c");

    private static bool IsHelloRetryRequestRandom(byte[] random) =>
        random != null && random.Length == 32 && random.SequenceEqual(HrrRandom);

    /* ─────────────────────────────────────────────────────────────────────────────
       Built-in fallback ids (used only if no AlgorithmInfo list is provided).
       These cover common oqsprovider ML-KEM hybrids in circulation.
       Extend/remove as you see fit; your AlgorithmInfo list overrides this anyway.
       ───────────────────────────────────────────────────────────────────────────── */
    private static readonly Dictionary<int, string> BuiltInPqHybridIds = new()
    {
        { 0x11EB, "SecP256r1MLKEM768"  },
        { 0x11EC, "X25519MLKEM768"     },
        { 0x11ED, "SecP384r1MLKEM1024" },
        { 0x11EE, "SecP521r1MLKEM1024" },
    };

    public ServerHelloHelper(IEnumerable<AlgorithmInfo>? algorithms = null)
    {
        // If a list is provided, use it as the source of truth.
        if (algorithms != null)
        {
            _algosById = algorithms
                .GroupBy(a => a.DefaultID)
                .ToDictionary(g => g.Key, g => g.First());

            _algosByNameLower = algorithms
                .Where(a => !string.IsNullOrWhiteSpace(a.AlgorithmName))
                .GroupBy(a => a.AlgorithmName.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());
        }
        else
        {
            // Fallback to built-ins (keep names minimal; mapping is only for convenience)
            _algosById = BuiltInPqHybridIds.ToDictionary(
                kv => kv.Key,
                kv => new AlgorithmInfo { AlgorithmName = kv.Value, DefaultID = kv.Key, Enabled = true });

            _algosByNameLower = _algosById.Values
                .ToDictionary(a => a.AlgorithmName.ToLowerInvariant(), a => a);
        }
    }

    private bool TryGetAlgoById(int id, out AlgorithmInfo algo) =>
        _algosById.TryGetValue(id, out algo);

    private bool TryGetAlgoByName(string? name, out AlgorithmInfo algo)
    {
        algo = null!;
        if (string.IsNullOrWhiteSpace(name)) return false;

        var key = name.Trim().ToLowerInvariant();
        if (_algosByNameLower.TryGetValue(key, out var a))
        {
            algo = a;
            return true;
        }

        // Heuristic: many oqs hybrids include "mlkem" in the printed name (future-proofing).
        // If your AlgorithmInfo names are terse (e.g., "MLKEM768_X25519"), consider normalizing upstream.
        if (key.Contains("mlkem"))
        {
            // try simple contains-based match
            var candidate = _algosByNameLower.Keys
                .Select(k => new { key = k, score = OverlapScore(k, key) })
                .OrderByDescending(x => x.score)
                .FirstOrDefault();

            if (candidate != null && candidate.score > 0)
            {
                algo = _algosByNameLower[candidate.key];
                return true;
            }
        }

        return false;
    }

    // tiny overlap metric (count of common chars) to choose a best-effort name match
    private static int OverlapScore(string a, string b)
    {
        var setA = new HashSet<char>(a);
        var setB = new HashSet<char>(b);
        setA.IntersectWith(setB);
        return setA.Count;
    }

    public KemExtension FindServerHello(string input)
    {
        var kemExtension = new KemExtension();

        // QUICK FAST-PATH: trust OpenSSL’s “Negotiated TLS1.3 group:” line if present
        // Example line: "Negotiated TLS1.3 group: X25519MLKEM768"
        var lines = input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var negotiatedLine = lines.FirstOrDefault(l =>
            l.StartsWith("Negotiated TLS1.3 group:", StringComparison.OrdinalIgnoreCase));

        if (negotiatedLine != null)
        {
            var groupName = negotiatedLine.Split(':', 2)[1].Trim();
            if (TryGetAlgoByName(groupName, out var algoFromName))
            {
                _sb.AppendLine($"[fast-path] OpenSSL reports negotiated group: {groupName} → {algoFromName.AlgorithmName} (0x{algoFromName.DefaultID:X})");
                return new KemExtension
                {
                    IsQuantumSafe = true,
                    GroupID = algoFromName.DefaultID,
                    GroupHexStringID = $"0x{algoFromName.DefaultID:X4}",
                };
            }
            _sb.AppendLine($"[fast-path] Negotiated group not recognized in AlgorithmInfo list: {groupName}");
        }

        // 1) Collect ALL ServerHello hex blobs from the transcript (there can be HRR then final SH)
        var serverHelloHexes = new List<string>();
        bool collecting = false;
        var sbHex = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Start of a ServerHello block (OpenSSL prints “…, ServerHello” on the same line)
            if (line.IndexOf("ServerHello", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (collecting && sbHex.Length > 0)
                {
                    serverHelloHexes.Add(sbHex.ToString());
                    sbHex.Clear();
                }
                collecting = true;
                continue;
            }

            // Any new record header (“<<< …” or “>>> …”) ends a ServerHello block
            if (collecting && (line.StartsWith("<<<") || line.StartsWith(">>>")))
            {
                if (sbHex.Length > 0)
                {
                    serverHelloHexes.Add(sbHex.ToString());
                    sbHex.Clear();
                }
                collecting = false;
                continue;
            }

            if (collecting)
            {
                var hex = line.Replace(" ", "");
                if (hex.Length > 0) sbHex.Append(hex);
            }
        }
        if (collecting && sbHex.Length > 0)
            serverHelloHexes.Add(sbHex.ToString());

        if (serverHelloHexes.Count == 0)
        {
            Sb.Append("No ServerHello found.");
            return kemExtension;
        }

        // 2) Parse each ServerHello; return the first non-HRR that is PQ/hybrid
        KemExtension lastSeen = new KemExtension();

        foreach (var shHex in serverHelloHexes)
        {
            Sb.Append("SERVERHELLO is : " + shHex);

            try
            {
                var tlsHandshakeBytes = Hex.Decode(shHex);        // should start with 0x02 (ServerHello)
                var serverHelloBytes = ExtractServerHelloMessage(tlsHandshakeBytes);
                var serverHello = ParseServerHello(serverHelloBytes);

                // Basic logging
                Sb.Append("ServerHello:");
                Sb.Append($"- Version: {serverHello.Version}");
                Sb.Append($"- Random: {BitConverter.ToString(serverHello.Random).Replace("-", "")}");
                Sb.Append($"- SessionID: {BitConverter.ToString(serverHello.SessionID).Replace("-", "")}");
                Sb.Append($"- CipherSuite: {serverHello.CipherSuite}");

                bool isHrr = IsHelloRetryRequestRandom(serverHello.Random);
                if (isHrr) Sb.Append("This ServerHello looks like an HRR (HelloRetryRequest) shim.");

                // Extensions
                Sb.Append("- Extensions:");
                var thisKem = new KemExtension();

                foreach (var extension in serverHello.Extensions)
                {
                    Sb.Append($"  - ExtensionType: {extension.Key} (0x{extension.Key:X})");
                    Sb.Append($"  - ExtensionData: {BitConverter.ToString(extension.Value).Replace("-", "")}");

                    var pair = new KeyValuePair<int, byte[]>(extension.Key, extension.Value);
                    var decoded = DecodeKeyShareExtension(pair);

                    // prefer a PQ/hybrid if we find one
                    thisKem = decoded.IsQuantumSafe ? decoded : thisKem;

                    if (decoded.IsQuantumSafe)
                    {
                        if (serverHelloBytes.Length > 100) decoded.LongServerHello = true;

                        if (!isHrr)
                        {
                            return decoded; // first non-HRR PQ/hybrid wins
                        }
                        else
                        {
                            lastSeen = decoded; // keep diagnostic and continue to final SH
                            Sb.Append("HRR indicated PQ/hybrid group; continuing to final ServerHello.");
                            break;
                        }
                    }
                }

                if (!thisKem.IsQuantumSafe)
                {
                    if (serverHelloBytes.Length > 100) thisKem.LongServerHello = true;
                    lastSeen = thisKem;
                }
            }
            catch (Exception ex)
            {
                Sb.Append($"Failed to parse a ServerHello block: {ex.Message}");
            }
        }

        // 3) If we reach here, no non-HRR ServerHello had PQ/hybrid — return best effort (likely not PQ)
        return lastSeen;
    }

    public KemExtension DecodeKeyShareExtension(KeyValuePair<int, byte[]> extension)
    {
        var kemExtension = new KemExtension();
        try
        {
            if (extension.Key != 0x0033) // 0x0033 = key_share
            {
                Sb.Append("This is not a key_share extension.");
                return kemExtension;
            }

            Sb.Append("This is a key_share extension.");

            if (extension.Value.Length < 2) return kemExtension; // malformed

            kemExtension.GroupID = (extension.Value[0] << 8) | extension.Value[1];
            kemExtension.GroupHexStringID = $"0x{extension.Value[0]:X2}{extension.Value[1]:X2}";

            Sb.Append("KemExtension object created:");
            Sb.Append($"- GroupHexID: {kemExtension.GroupHexStringID}");
            Sb.Append($"- GroupID: {kemExtension.GroupID}");

            // Mark PQ/hybrid if the GROUP ID is in your AlgorithmInfo list (or built-in fallback)
            if (TryGetAlgoById(kemExtension.GroupID, out _))
            {
                kemExtension.IsQuantumSafe = true;
            }

            // If present, parse classic ECDHE-style payload (often absent for hybrid in SH)
            if (extension.Value.Length > 2)
            {
                if (extension.Value.Length >= 4)
                {
                    kemExtension.KeyShareLength = (extension.Value[2] << 8) | extension.Value[3];
                    var take = Math.Min(kemExtension.KeyShareLength, Math.Max(0, extension.Value.Length - 4));
                    kemExtension.Data = extension.Value.Skip(4).Take(take).ToArray();

                    Sb.Append($"- KeyShareLength: {kemExtension.KeyShareLength}");
                    Sb.Append($"- Data: {BitConverter.ToString(kemExtension.Data).Replace("-", "")}");
                }
                else
                {
                    Sb.Append("- KeyShare present but too short to contain a length; ignoring payload.");
                }
            }
        }
        catch
        {
            // Intentionally swallow; return whatever we decoded.
        }

        return kemExtension;
    }

    private byte[] ExtractServerHelloMessage(byte[] tlsHandshakeBytes)
    {
        if (tlsHandshakeBytes == null || tlsHandshakeBytes.Length < 4)
            throw new InvalidOperationException("Truncated TLS handshake buffer.");

        byte handshakeType = tlsHandshakeBytes[0];
        if (handshakeType != 0x02) // 0x02 = ServerHello
            throw new InvalidOperationException("Invalid TLS handshake type (expected ServerHello).");

        int handshakeLength = (tlsHandshakeBytes[1] << 16) | (tlsHandshakeBytes[2] << 8) | tlsHandshakeBytes[3];
        if (handshakeLength < 0 || handshakeLength + 4 != tlsHandshakeBytes.Length)
            throw new InvalidOperationException("Invalid TLS handshake length.");

        var serverHelloBytes = new byte[handshakeLength];
        Buffer.BlockCopy(tlsHandshakeBytes, 4, serverHelloBytes, 0, handshakeLength);
        return serverHelloBytes;
    }

    private ServerHello ParseServerHello(byte[] serverHelloBytes)
    {
        using var input = new MemoryStream(serverHelloBytes, writable: false);
        return ServerHello.Parse(input);
    }

    // (Unused by the PQ path; kept for EC-only experiments)
    private void ProcessKeyShareExtension(byte[] extensionData)
    {
        Asn1Sequence extensionSeq = Asn1Sequence.GetInstance(extensionData);
        int namedGroup = DerInteger.GetInstance(extensionSeq[0]).Value.IntValue;
        byte[] publicKeyBytes = DerOctetString.GetInstance(extensionSeq[1]).GetOctets();

        X9ECParameters ecParams = GetCurveParameters(namedGroup);
        ECPoint publicKey = ecParams.Curve.DecodePoint(publicKeyBytes);
    }

    private X9ECParameters GetCurveParameters(int namedGroup)
    {
        switch (namedGroup)
        {
            case 0x001d: // secp256r1 (P-256)
                return NistNamedCurves.GetByName("P-256");
            case 0x0017: // secp384r1 (P-384)
                return NistNamedCurves.GetByName("P-384");
            case 0x0018: // secp521r1 (P-521)
                return NistNamedCurves.GetByName("P-521");
            default:
                throw new ArgumentException("Unsupported named group: " + namedGroup);
        }
    }
}
