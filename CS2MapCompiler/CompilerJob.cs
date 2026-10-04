using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CS2MapCompiler;

// Ties resourcecompiler to the app's lifetime. Processes added here go in a Windows job object that kills everything in it
// once the job's handle closes, and Windows closes that handle when the app exits for any reason, including a crash, being
// ended from Task Manager or the debugger stopping. Processes started by those processes, like vrad3, are put in the same
// job by Windows, so they go too. The handle is never closed by the app itself, it only goes away with the process
[SupportedOSPlatform("windows")]
internal static class CompilerJob
{
    private const int JobObjectExtendedLimitInformationClass = 9;

    // The same flags Source 2's tier0 sets on the job it puts its own child processes in. Allowing breakaway lets a process that
    // asks to leave the job, like a crash reporter, still start, instead of failing because the job won't let it go
    private const uint KillOnJobClose = 0x2000;
    private const uint BreakawayOk = 0x800;

    private static readonly Lazy<nint> Job = new(Create);

    // Called right after the process starts, before resourcecompiler has started anything of its own
    public static void Add(Process process)
    {
        if (!AssignProcessToJobObject(Job.Value, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject");
        }
    }

    private static nint Create()
    {
        var job = CreateJobObjectW(0, null);

        if (job == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject");
        }

        var limits = new ExtendedLimitInformation { BasicLimitInformation = { LimitFlags = KillOnJobClose | BreakawayOk } };

        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, ref limits, Marshal.SizeOf<ExtendedLimitInformation>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject");
        }

        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint job, int informationClass, ref ExtendedLimitInformation information, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);
}
