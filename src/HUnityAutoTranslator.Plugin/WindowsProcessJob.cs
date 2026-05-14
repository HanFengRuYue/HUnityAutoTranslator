using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace HUnityAutoTranslator.Plugin;

/// <summary>
/// 把子进程挂到 Windows Job Object 上，并打开 KillOnJobClose 标志。
/// 当宿主进程（Unity）退出或被任务管理器强杀时，Windows 会自动把 Job 里的所有子进程一并清掉，
/// 避免 llama-server.exe 之类的后台辅助进程被孤立残留。
/// </summary>
internal static class WindowsProcessJob
{
    private static readonly object Gate = new();
    private static IntPtr _jobHandle = IntPtr.Zero;
    private static bool _initialized;
    private static bool _supported;

    public static bool Assign(Process process, ManualLogSource logger)
    {
        if (process == null || process.HasExited)
        {
            return false;
        }

        IntPtr job;
        lock (Gate)
        {
            if (!_initialized)
            {
                _initialized = true;
                // 整个插件只跑在 Windows 上（BepInEx + Unity + llama-server.exe 都是 Windows 专属），
                // 不再用 RuntimeInformation 判平台 —— Unity 6 的 Mono 不带
                // System.Runtime.InteropServices.RuntimeInformation 4.0.2.0，会 FileNotFoundException。
                // 直接尝试调 kernel32 创建 Job Object，失败就降级、不影响插件主流程。
                _supported = TryCreateJob(out _jobHandle, logger);
            }

            if (!_supported || _jobHandle == IntPtr.Zero)
            {
                return false;
            }

            job = _jobHandle;
        }

        try
        {
            if (!AssignProcessToJobObject(job, process.Handle))
            {
                var error = Marshal.GetLastWin32Error();
                // ERROR_ACCESS_DENIED(5)：进程已经在其它 Job 里，Win 8+ 通过嵌套 Job 解决，
                // 在更老系统上只能放弃绑定，不再视为严重错误。
                logger.LogDebug($"无法把 llama.cpp 进程加入 Job Object（错误码 {error}），将依赖手动停止。");
                return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception ex)
        {
            logger.LogDebug($"绑定 llama.cpp 进程到 Job Object 失败：{ex.Message}");
            return false;
        }
    }

    private static bool TryCreateJob(out IntPtr handle, ManualLogSource logger)
    {
        handle = IntPtr.Zero;
        try
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger.LogDebug($"创建 Job Object 失败：错误码 {error}。");
                return false;
            }

            var basicLimit = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            };
            var extendedLimit = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = basicLimit
            };

            var length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(extendedLimit, buffer, fDeleteOld: false);
                if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)length))
                {
                    var error = Marshal.GetLastWin32Error();
                    logger.LogDebug($"设置 Job Object KillOnJobClose 失败：错误码 {error}。");
                    CloseHandle(handle);
                    handle = IntPtr.Zero;
                    return false;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
