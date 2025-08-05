using System;
using System.Security.Cryptography;
using System.Text;
namespace NetworkMonitor.Utils.Helpers;

public static class HashHelper
{
    public static string ComputeSha256Hash(string rawData)
    {
        // Create a SHA256   
        using (SHA256 sha256Hash = SHA256.Create())
        {
            // ComputeHash - returns byte array  
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

            // Convert byte array to a string   
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }

      public static string ComputeSha3_256Hash(string rawData)
    {
        using (SHA256 sha3 = SHA256.Create()) // Create an instance of SHA3-256
        {
            // Convert the input string to a byte array and compute the hash
            byte[] bytes = sha3.ComputeHash(Encoding.UTF8.GetBytes(rawData));

            // Convert the byte array to a hexadecimal string
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
