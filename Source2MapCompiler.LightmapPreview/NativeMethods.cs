using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Source2MapCompiler.LightmapPreview;

internal static partial class NativeMethods
{
    public const uint ProcessVmRead = 0x0010;
    public const uint ProcessQueryLimitedInformation = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ReadProcessMemory(nint process, nint address, void* buffer, nint size, out nint read);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(nint process, int informationClass, out ProcessBasicInformation information, int length, out int returnLength);

    public static Win32Exception LastError(string what)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), what);
    }
}
