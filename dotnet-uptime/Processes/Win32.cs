
using System.Runtime.InteropServices;

namespace MV10.DotnetUptime;

/// <summary>
/// Win32 API declarations for reading process command lines via the PEB.
/// </summary>
internal static partial class Win32
{
    public const int ProcessBasicInformation = 0;
    public const int ProcessWow64Information = 26;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING_32
    {
        public ushort Length;
        public ushort MaximumLength;
        public int Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        internal IntPtr Reserved1;
        internal IntPtr PebBaseAddress;
        internal IntPtr Reserved2_0;
        internal IntPtr Reserved2_1;
        internal IntPtr UniqueProcessId;
        internal IntPtr InheritedFromUniqueProcessId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer,
        IntPtr dwSize,
        IntPtr lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        out IntPtr lpPtr,
        IntPtr dwSize,
        ref IntPtr lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        ref UNICODE_STRING lpBuffer,
        IntPtr dwSize,
        IntPtr lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        ref UNICODE_STRING_32 lpBuffer,
        IntPtr dwSize,
        IntPtr lpNumberOfBytesRead);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWow64Process(
        IntPtr hProcess,
        [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [LibraryImport("ntdll.dll", SetLastError = true)]
    public static partial int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref IntPtr processInformation,
        int processInformationLength,
        ref int returnLength);
}
