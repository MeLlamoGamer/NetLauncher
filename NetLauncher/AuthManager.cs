using System;
using System.Security.Cryptography;
using System.Text;

namespace NetLauncher
{
    public class OfflineSession
    {
        public string Username { get; set; }
        public string UUID { get; set; }
        public string AccessToken { get; set; } // cualquier string en modo offline
    }

    public class AuthManager
    {
        public static OfflineSession CreateOfflineSession(string username)
        {
            return new OfflineSession
            {
                Username = username,
                UUID = GenerateOfflineUUID(username),
                AccessToken = "0" // MC acepta cualquier valor en offline
            };
        }

        private static string GenerateOfflineUUID(string username)
        {
            // Estándar de Minecraft: UUID v3 con "OfflinePlayer:<nombre>"
            byte[] input = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);

            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(input);

                // Ajustar bits para UUID v3
                hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
                hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

                return $"{ToHex(hash, 0, 4)}-{ToHex(hash, 4, 2)}-{ToHex(hash, 6, 2)}-{ToHex(hash, 8, 2)}-{ToHex(hash, 10, 6)}";
            }
        }

        private static string ToHex(byte[] data, int start, int length)
        {
            var sb = new StringBuilder();
            for (int i = start; i < start + length; i++)
                sb.Append(data[i].ToString("x2"));
            return sb.ToString();
        }
    }
}