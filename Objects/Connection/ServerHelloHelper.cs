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

    public KemExtension FindServerHello(string input)
    {
        KemExtension kemExtension = new KemExtension();
        string[] lines = input.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

        bool foundServerHello = false;
        string serverHelloHex = "";

        foreach (string line in lines)
        {
            if (foundServerHello && (line.StartsWith("<<<") || line.StartsWith(">>>")))
            {
                break;
            }
            if (line.Contains("ServerHello"))
            {
                foundServerHello = true;
            }
            else if (foundServerHello && !line.StartsWith("<<<"))
            {
                // remove everything after >>> (if present)


                // remove leading and trailing white space
                string hexString = line.Trim();

                // remove any white space within the hex string
                hexString = hexString.Replace(" ", "");
                // concatenate the hex string to the output string


                serverHelloHex += hexString;
            }
            else
            {
                foundServerHello = false;
            }
        }

        Sb.Append("SERVERHELLO is : " + serverHelloHex);
        if (serverHelloHex == "")
        {
            Sb.Append("No ServerHello found.");
            return kemExtension;
        }
        byte[] tlsRecordBytes = Hex.Decode(serverHelloHex); // Use Bouncy Castle's Hex.Decode method
        byte[] serverHelloBytes = ExtractServerHelloMessage(tlsRecordBytes);
        ServerHello serverHello = ParseServerHello(serverHelloBytes);

        // Write all fields in serverHello to console
        Sb.Append("ServerHello:");
        Sb.Append($"- Version: {serverHello.Version}");
        Sb.Append($"- Random: {BitConverter.ToString(serverHello.Random).Replace("-", "")}");
        Sb.Append($"- SessionID: {BitConverter.ToString(serverHello.SessionID).Replace("-", "")}");
        Sb.Append($"- CipherSuite: {serverHello.CipherSuite}");

        Sb.Append("- Extensions:");
        foreach (var extension in serverHello.Extensions)
        {
            Sb.Append($"  - ExtensionType: {extension.Key} (0x{extension.Key:X})");
            Sb.Append($"  - ExtensionData: {BitConverter.ToString(extension.Value).Replace("-", "")}");
            var pair = new KeyValuePair<int, byte[]>(extension.Key, extension.Value);
            kemExtension = DecodeKeyShareExtension(pair);
            if (kemExtension.IsQuantumSafe)
            {
                break;
            }
        }
        if (serverHelloBytes.Length > 100)
        {
            kemExtension.LongServerHello = true;
        }
        return kemExtension;
    }

    public KemExtension DecodeKeyShareExtension(KeyValuePair<int, byte[]> extension)
    {
        KemExtension kemExtension = new KemExtension();
       try {
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
       catch {
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
