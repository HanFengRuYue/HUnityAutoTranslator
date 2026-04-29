using System.Runtime.InteropServices;
using System.Text;

namespace HUnityAutoTranslator.Plugin;

internal static class WindowsConsoleEncoding
{
    private const uint Utf8CodePage = 65001;

    public static void ConfigureUtf8()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
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
