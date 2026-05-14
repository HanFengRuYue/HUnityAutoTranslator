using System.Runtime.InteropServices;
using System.Text;

namespace HUnityAutoTranslator.Plugin;

internal static class WindowsConsoleEncoding
{
    private const uint Utf8CodePage = 65001;

    public static void ConfigureUtf8()
    {
        // 用 Environment.OSVersion 判平台：System.Runtime.InteropServices.RuntimeInformation
        // 不是所有 Unity Mono 运行时都自带（BE.755 + Unity 6 / 部分 Unity 2019 都缺它），
        // 找不到时插件 Awake 阶段直接 FileNotFoundException。
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            return;
        }

        try
        {
            _ = SetConsoleOutputCP(Utf8CodePage);
            _ = SetConsoleCP(Utf8CodePage);
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.InputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Some Unity hosts have no attached console. Encoding setup must never block plugin startup.
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint wCodePageID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCP(uint wCodePageID);
}
