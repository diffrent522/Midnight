using System;
using System.Security.Cryptography;
using System.Text;

namespace Midnight.Services
{
    public interface ISecurityService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
    }

    public class SecurityService : ISecurityService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Midnight_Entropy_v1");

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            try
            {
                byte[] cipherBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch (Exception ex)
            {
                LogService.Error($"Encryption failed: {ex.Message}", "Security");
                return string.Empty;
            }
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;
            try
            {
                byte[] plainBytes = ProtectedData.Unprotect(Convert.FromBase64String(cipherText), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                LogService.Error($"Decryption failed: {ex.Message}", "Security");
                return string.Empty;
            }
        }
    }
}

