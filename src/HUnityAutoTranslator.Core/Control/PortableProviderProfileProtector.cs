using System.Security.Cryptography;
using System.Text;

namespace HUnityAutoTranslator.Core.Control;

internal static class PortableProviderProfileProtector
{
    private const string Prefix = "hutprovider:v1:";
    private const string KeyMaterial = "HUnityAutoTranslator.ProviderProfiles.Portable.v1";

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Provider profile content must not be empty.", nameof(value));
        }

        var data = Encoding.UTF8.GetBytes(value);
        var iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey("enc");
        aes.IV = iv;

        byte[] cipherText;
        using (var encryptor = aes.CreateEncryptor())
        {
            cipherText = encryptor.TransformFinalBlock(data, 0, data.Length);
        }

        var payloadWithoutMac = Combine(iv, cipherText);
        var mac = ComputeHmac(payloadWithoutMac);
        return Prefix + Convert.ToBase64String(Combine(payloadWithoutMac, mac));
    }

    public static string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue) ||
            !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Provider profile payload is not recognized.");
        }

        var payload = Convert.FromBase64String(protectedValue.Substring(Prefix.Length));
        if (payload.Length <= 48)
        {
            throw new CryptographicException("Provider profile payload is too short.");
        }

        var macOffset = payload.Length - 32;
        var payloadWithoutMac = Slice(payload, 0, macOffset);
        var expectedMac = ComputeHmac(payloadWithoutMac);
        var actualMac = Slice(payload, macOffset, 32);
        if (!FixedTimeEquals(expectedMac, actualMac))
        {
            throw new CryptographicException("Provider profile payload authentication failed.");
        }

        var iv = Slice(payloadWithoutMac, 0, 16);
        var cipherText = Slice(payloadWithoutMac, 16, payloadWithoutMac.Length - 16);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = DeriveKey("enc");
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length));
    }

    private static byte[] DeriveKey(string purpose)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(KeyMaterial + "\n" + purpose));
    }

    private static byte[] ComputeHmac(byte[] data)
    {
        using var hmac = new HMACSHA256(DeriveKey("mac"));
        return hmac.ComputeHash(data);
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < left.Length; i++)
        {
            diff |= left[i] ^ right[i];
        }

        return diff == 0;
    }

    private static byte[] Combine(byte[] left, byte[] right)
    {
        var combined = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, combined, 0, left.Length);
        Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
        return combined;
    }

    private static byte[] Slice(byte[] value, int offset, int count)
    {
        var result = new byte[count];
        Buffer.BlockCopy(value, offset, result, 0, count);
        return result;
    }
}
