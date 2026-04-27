using System.Runtime.InteropServices;
using System.Threading;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Plugin;

internal static class WindowsFontFilePicker
{
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const uint SigDnFileSysPath = 0x80058000;

    public static FontPickResult PickFontFile()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return FontPickResult.Unsupported();
        }

        FontPickResult? result = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = PickFontFileOnStaThread();
            }
            catch (Exception ex)
            {
                result = FontPickResult.Error(ex.Message);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        return result ?? FontPickResult.Error("打开字体文件选择器失败。");
    }

    private static FontPickResult PickFontFileOnStaThread()
    {
        IFileOpenDialog? dialog = null;
        IShellItem? item = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.SetTitle("选择替换字体文件");
            dialog.SetOptions(FileOpenOptions.ForceFileSystem | FileOpenOptions.FileMustExist | FileOpenOptions.PathMustExist);
            dialog.SetFileTypes(3, new[]
            {
                new DialogFilterSpec("字体文件 (*.ttf;*.otf)", "*.ttf;*.otf"),
                new DialogFilterSpec("TrueType 字体 (*.ttf)", "*.ttf"),
                new DialogFilterSpec("OpenType 字体 (*.otf)", "*.otf")
            });
            dialog.SetFileTypeIndex(1);
            dialog.SetDefaultExtension("ttf");

            var hr = dialog.Show(IntPtr.Zero);
            if (hr == ErrorCancelled)
            {
                return FontPickResult.Cancelled();
            }

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            dialog.GetResult(out item);
            var path = GetFileSystemPath(item);
            if (string.IsNullOrWhiteSpace(path))
            {
                return FontPickResult.Error("未能读取所选字体文件路径。");
            }

            var fontName = FontNameReader.TryReadFamilyName(path, out var parsedName)
                ? parsedName
                : Path.GetFileNameWithoutExtension(path);
            return FontPickResult.Selected(path, fontName);
        }
        finally
        {
            if (item != null)
            {
                Marshal.ReleaseComObject(item);
            }

            if (dialog != null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    private static string? GetFileSystemPath(IShellItem item)
    {
        item.GetDisplayName(SigDnFileSysPath, out var pointer);
        try
        {
            return Marshal.PtrToStringUni(pointer);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] DialogFilterSpec[] filterSpecs);

        void SetFileTypeIndex(uint fileTypeIndex);

        void GetFileTypeIndex(out uint fileTypeIndex);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(FileOpenOptions options);

        void GetOptions(out FileOpenOptions options);

        void SetDefaultFolder(IShellItem shellItem);

        void SetFolder(IShellItem shellItem);

        void GetFolder(out IShellItem shellItem);

        void GetCurrentSelection(out IShellItem shellItem);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem shellItem);

        void AddPlace(IShellItem shellItem, uint place);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);

        void GetResults(out IntPtr shellItems);

        void GetSelectedItems(out IntPtr shellItems);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handlerId, ref Guid interfaceId, out IntPtr ppv);

        void GetParent(out IShellItem shellItem);

        void GetDisplayName(uint sigdnName, out IntPtr name);

        void GetAttributes(uint attributeMask, out uint attributes);

        void Compare(IShellItem shellItem, uint hint, out int order);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly struct DialogFilterSpec
    {
        public DialogFilterSpec(string name, string spec)
        {
            Name = name;
            Spec = spec;
        }

        [MarshalAs(UnmanagedType.LPWStr)]
        public readonly string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public readonly string Spec;
    }

    [Flags]
    private enum FileOpenOptions : uint
    {
        ForceFileSystem = 0x00000040,
        PathMustExist = 0x00000800,
        FileMustExist = 0x00001000
    }
}
