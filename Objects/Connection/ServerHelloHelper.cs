using System;
using System.Text;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Utilities.Encoders;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Security;
using NetworkMonitor.Objects;



class ServerHelloHelper
{
    private StringBuilder _sb = new StringBuilder();

    public StringBuilder Sb { get => _sb; set => _sb = value; }

    private static readonly byte[] HrrRandom =
        Org.BouncyCastle.Utilities.Encoders.Hex.Decode(
            "cf21ad74e59a6111be1d8c021e65b891c2a211167abb8c5e079e09e2c8a8339c");

    private static bool IsHelloRetryRequestRandom(byte[] random)
    {
        return random != null && random.Length == 32 && random.SequenceEqual(HrrRandom);
    }

    public KemExtension FindServerHello(string input)
    {
        var kemExtension = new KemExtension();
        var lines = input.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        // 1) Collect ALL ServerHello hex blobs from the transcript
        var serverHelloHexes = new List<string>();
        bool collecting = false;
        var sbHex = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            // Start of a ServerHello block (OpenSSL prints “…, ServerHello” on the same line)
            if (line.IndexOf("ServerHello", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // If we were somehow collecting, flush previous first
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
                // keep scanning; there may be more ServerHellos later
                continue;
            }

            // While collecting, accumulate hex lines
            if (collecting)
            {
                // Strip spaces from hex
                sbHex.Append(line.Replace(" ", ""));
            }
        }
        // Flush tail if file ended mid-block
        if (collecting && sbHex.Length > 0)
        {
            serverHelloHexes.Add(sbHex.ToString());
        }

        if (serverHelloHexes.Count == 0)
        {
            Sb.Append("No ServerHello found.");
            return kemExtension;
        }

        // 2) Parse each ServerHello; PASS on first non-HRR that is quantum-safe
        KemExtension lastSeen = new KemExtension();
        foreach (var shHex in serverHelloHexes)
        {
            Sb.Append("SERVERHELLO is : " + shHex);

            try
            {
                var tlsHandshakeBytes = Hex.Decode(shHex);        // this should be the raw ServerHello handshake (starts with 0x02)
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

                    // your existing decoder decides if it’s “quantum safe”
                    var pair = new KeyValuePair<int, byte[]>(extension.Key, extension.Value);
                    thisKem = DecodeKeyShareExtension(pair);

                    if (thisKem.IsQuantumSafe)
                    {
                        // Record longness based on this SH
                        if (serverHelloBytes.Length > 100) thisKem.LongServerHello = true;

                        if (!isHrr)
                        {
                            // success: first non-HRR PQ/hybrid SH — return it
                            return thisKem;
                        }
                        else
                        {
                            // HRR hinted PQ/hybrid; keep for diagnostics but continue looking for final SH
                            lastSeen = thisKem;
                            Sb.Append("HRR indicated PQ/hybrid group; continuing to final ServerHello.");
                            break;
                        }
                    }
                }

                // keep the last seen (non-PQ) for possible return if nothing PQ shows up
                if (!thisKem.IsQuantumSafe)
                {
                    if (serverHelloBytes.Length > 100) thisKem.LongServerHello = true;
                    lastSeen = thisKem;
                }
            }
            catch (Exception ex)
            {
                Sb.Append($"Failed to parse a ServerHello block: {ex.Message}");
                // continue to next block
            }
        }

        // 3) If we reach here, no non-HRR ServerHello had PQ/hybrid — return best effort (likely not PQ)
        return lastSeen;
    }

    public KemExtension DecodeKeyShareExtension(KeyValuePair<int, byte[]> extension)
    {
        KemExtension kemExtension = new KemExtension();
        try
        {
            int extensionType = extension.Key;

            if (extensionType == 0x0033) // 0x0033 corresponds to key_share extension
            {
                Sb.Append("This is a key_share extension.");

                // Create a KemExtension object from the above data

                kemExtension.GroupHexStringID = "0x" + BitConverter.ToString(extension.Value.Take(2).ToArray()).Replace("-", "");
                kemExtension.GroupID = (extension.Value[0] << 8) + extension.Value[1];
                Sb.Append("KemExtension object created:");
                Sb.Append($"- GroupHexID: {kemExtension.GroupHexStringID}");
                Sb.Append($"- GroupID: {kemExtension.GroupID}");
                kemExtension.IsQuantumSafe = true;
                if (extension.Value.Length > 2)
                {
                    kemExtension.KeyShareLength = (extension.Value[2] << 8) + extension.Value[3];
                    kemExtension.Data = extension.Value.Skip(4).Take(kemExtension.KeyShareLength).ToArray();
                    Sb.Append($"- KeyShareLength: {kemExtension.KeyShareLength}");
                    Sb.Append($"- Data: {BitConverter.ToString(kemExtension.Data).Replace("-", "")}");
                }
            }
            else
            {
                Sb.Append("This is not a key_share extension.");
            }
        }
        catch
        {
            // Dont do anthing with an exception as we want as much data as possible from the extension.

        }


        return kemExtension;
    }
    private byte[] ExtractServerHelloMessage(byte[] tlsHandshakeBytes)
    {
        byte handshakeType = tlsHandshakeBytes[0];
        if (handshakeType != 0x02) // 0x02 represents ServerHello handshake message type
        {
            throw new InvalidOperationException("Invalid TLS handshake type");
        }

        int handshakeLength = ((tlsHandshakeBytes[1] << 16) | (tlsHandshakeBytes[2] << 8) | tlsHandshakeBytes[3]);
        if (handshakeLength + 4 != tlsHandshakeBytes.Length)
        {
            throw new InvalidOperationException("Invalid TLS handshake length");
        }

        byte[] serverHelloBytes = new byte[handshakeLength];
        Buffer.BlockCopy(tlsHandshakeBytes, 4, serverHelloBytes, 0, handshakeLength);

        return serverHelloBytes;
    }

    private ServerHello ParseServerHello(byte[] serverHelloBytes)
    {
        MemoryStream input = new MemoryStream(serverHelloBytes);
        ServerHello serverHello = ServerHello.Parse(input);

        return serverHello;
    }



    private byte[] HexStringToByteArray(string hex)
    {
        int length = hex.Length;
        byte[] bytes = new byte[length / 2];
        for (int i = 0; i < length; i += 2)
        {
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }
        return bytes;
    }

    private void ProcessKeyShareExtension(byte[] extensionData)
    {
        // Parse the extension data
        Asn1Sequence extensionSeq = Asn1Sequence.GetInstance(extensionData);
        int namedGroup = DerInteger.GetInstance(extensionSeq[0]).Value.IntValue;
        byte[] publicKeyBytes = DerOctetString.GetInstance(extensionSeq[1]).GetOctets();

        // Get the curve parameters for the named group
        X9ECParameters ecParams = GetCurveParameters(namedGroup);

        // Decode the public key bytes
        ECPoint publicKey = ecParams.Curve.DecodePoint(publicKeyBytes);

        /* // Create a ECDH key agreement engine using the curve parameters
         ECDHBasicAgreement agreement = new ECDHBasicAgreement();
         agreement.Init(new ECPrivateKeyParameters(ecParams.Curve.DecodePoint(publicKeyBytes), ecParams));

         // Generate the shared secret
         byte[] sharedSecret = agreement.CalculateAgreement(new ECPublicKeyParameters(publicKey, ecParams));

         // Print the shared secret
         _sb.Append("Shared Secret: " + Hex.ToHexString(sharedSecret));
         */
    }

    private X9ECParameters GetCurveParameters(int namedGroup)
    {
        switch (namedGroup)
        {
            case 0x001d: // secp256r1
                return NistNamedCurves.GetByName("P-256");
            case 0x0017: // secp384r1
                return NistNamedCurves.GetByName("P-384");
            case 0x0018: // secp521r1
                return NistNamedCurves.GetByName("P-521");
            default:
                throw new ArgumentException("Unsupported named group: " + namedGroup);
        }
    }
}
