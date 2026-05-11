using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LetterboxdSync;

public static class CryptoHelpers
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("L3tt3rb0xdSyncK3y!@#$12345678901"); // 32 bytes for AES-256
    private static readonly byte[] IV = Encoding.UTF8.GetBytes("L3tt3rb0xdSyncIV"); // 16 bytes for AES

    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        using var aesAlg = Aes.Create();
        aesAlg.Key = Key;
        aesAlg.IV = IV;

        var encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);
        using var msEncrypt = new MemoryStream();
        using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
        using (var swEncrypt = new StreamWriter(csEncrypt))
        {
            swEncrypt.Write(plaintext);
        }

        return Convert.ToBase64String(msEncrypt.ToArray());
    }

    public static string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return ciphertext;

        try
        {
            var buffer = Convert.FromBase64String(ciphertext);
            using var aesAlg = Aes.Create();
            aesAlg.Key = Key;
            aesAlg.IV = IV;

            var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
            using var msDecrypt = new MemoryStream(buffer);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var srDecrypt = new StreamReader(csDecrypt);

            return srDecrypt.ReadToEnd();
        }
        catch
        {
            // If it fails to decrypt, it might be an old plaintext password
            return ciphertext;
        }
    }
}
