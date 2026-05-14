using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HUnityAutoTranslator.Core.Control;

internal static class ApiKeyProtector
{
    private const string DpapiPrefix = "dpapi-current-user:v1:";
    private const string LocalAesPrefix = "local-aes:v1:";
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("HUnityAutoTranslator.ApiKey.v1");

    public static string Protect(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));
        }

        var bytes = Encoding.UTF8.GetBytes(apiKey);
        return IsWindows()
            ? DpapiPrefix + Convert.ToBase64String(ProtectWithDpapi(bytes))
            : LocalAesPrefix + Convert.ToBase64String(ProtectWithLocalAes(bytes));
    }

    public static string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            if (protectedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                if (!IsWindows())
                {
                    return null;
                }

                var bytes = Convert.FromBase64String(protectedValue.Substring(DpapiPrefix.Length));
                return Encoding.UTF8.GetString(UnprotectWithDpapi(bytes));
            }

            if (protectedValue.StartsWith(LocalAesPrefix, StringComparison.Ordinal))
            {
                var bytes = Convert.FromBase64String(protectedValue.Substring(LocalAesPrefix.Length));
                return Encoding.UTF8.GetString(UnprotectWithLocalAes(bytes));
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or InvalidOperationException)
        {
            return null;
        }

        return null;
    }

    private static bool IsWindows()
    {
        // 避免依赖 System.Runtime.InteropServices.RuntimeInformation：部分 Unity Mono 运行时不带这个程序集，
        // 在 PluginRuntime.Start() 路径里调用会触发 FileNotFoundException。
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
    }

    private static byte[] ProtectWithDpapi(byte[] data)
    {
        var input = CreateBlob(data);
        var entropy = CreateBlob(Entropy);
        try
        {
            if (!CryptProtectData(ref input, "HUnityAutoTranslator API key", ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            {
                throw new InvalidOperationException($"DPAPI protect failed: {Marshal.GetLastWin32Error()}");
            }

            try
            {
                return CopyBlob(output);
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            FreeBlob(input);
            FreeBlob(entropy);
        }
    }

    private static byte[] UnprotectWithDpapi(byte[] data)
    {
        var input = CreateBlob(data);
        var entropy = CreateBlob(Entropy);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var output))
            {
                throw new InvalidOperationException($"DPAPI unprotect failed: {Marshal.GetLastWin32Error()}");
            }

            try
            {
                return CopyBlob(output);
            }
            finally
            {
                LocalFree(output.pbData);
            }
        }
        finally
        {
            FreeBlob(input);
            FreeBlob(entropy);
        }
    }

    private static byte[] ProtectWithLocalAes(byte[] data)
    {
        var iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = GetLocalAesEncryptionKey();
        aes.IV = iv;

        byte[] cipherText;
        using (var encryptor = aes.CreateEncryptor())
        {
            cipherText = encryptor.TransformFinalBlock(data, 0, data.Length);
        }

        var payloadWithoutMac = Combine(iv, cipherText);
        var mac = ComputeHmac(payloadWithoutMac);
        return Combine(payloadWithoutMac, mac);
    }

    private static byte[] UnprotectWithLocalAes(byte[] payload)
    {
        if (payload.Length <= 48)
        {
            throw new CryptographicException("Encrypted API key payload is too short.");
        }

        var macOffset = payload.Length - 32;
        var payloadWithoutMac = Slice(payload, 0, macOffset);
        var expectedMac = ComputeHmac(payloadWithoutMac);
        var actualMac = Slice(payload, macOffset, 32);
        if (!FixedTimeEquals(expectedMac, actualMac))
        {
            throw new CryptographicException("Encrypted API key payload authentication failed.");
        }

        var iv = Slice(payloadWithoutMac, 0, 16);
        var cipherText = Slice(payloadWithoutMac, 16, payloadWithoutMac.Length - 16);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = GetLocalAesEncryptionKey();
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
    }

    private static byte[] GetLocalAesEncryptionKey()
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(GetLocalKeyMaterial() + "\nenc"));
    }

    private static byte[] GetLocalAesMacKey()
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(GetLocalKeyMaterial() + "\nmac"));
    }

    private static string GetLocalKeyMaterial()
    {
        return string.Join("\n", new[]
        {
            "HUnityAutoTranslator.ApiKeyProtector.v1",
            Environment.UserDomainName ?? string.Empty,
            Environment.UserName ?? string.Empty,
            Environment.MachineName ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty
        });
    }

    private static byte[] ComputeHmac(byte[] data)
    {
        using var hmac = new HMACSHA256(GetLocalAesMacKey());
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

    private static DataBlob CreateBlob(byte[] data)
    {
        var blob = new DataBlob
        {
            cbData = data.Length,
            pbData = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, blob.pbData, data.Length);
        return blob;
    }

    private static byte[] CopyBlob(DataBlob blob)
    {
        var data = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, data, 0, blob.cbData);
        return data;
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.pbData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(blob.pbData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string szDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        ref DataBlob pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
