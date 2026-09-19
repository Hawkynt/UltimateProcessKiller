using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltimateProcessKiller.Platforms.Windows;

/// <summary>The Win32 / NT surface the Windows killer calls, split by owning DLL.</summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeMethods {
  // Process access rights.
  public const uint PROCESS_TERMINATE = 0x0001;
  public const uint PROCESS_CREATE_THREAD = 0x0002;
  public const uint PROCESS_VM_OPERATION = 0x0008;
  public const uint PROCESS_VM_WRITE = 0x0020;
  public const uint PROCESS_QUERY_INFORMATION = 0x0400;
  public const uint PROCESS_SUSPEND_RESUME = 0x0800;

  // Thread access rights.
  public const uint THREAD_TERMINATE = 0x0001;

  // Handle duplication.
  public const uint DUPLICATE_CLOSE_SOURCE = 0x0001;

  // Memory.
  public const uint MEM_COMMIT = 0x1000;
  public const uint PAGE_READWRITE = 0x04;
  public const uint PAGE_WRITECOPY = 0x08;
  public const uint PAGE_EXECUTE_READWRITE = 0x40;
  public const uint PAGE_EXECUTE_WRITECOPY = 0x80;

  // Window messages.
  public const uint WM_CLOSE = 0x0010;

  // NTSTATUS.
  public const uint STATUS_SUCCESS = 0x00000000;

  [StructLayout(LayoutKind.Sequential)]
  public struct MEMORY_BASIC_INFORMATION {
    public nint BaseAddress;
    public nint AllocationBase;
    public uint AllocationProtect;
    public nint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
  }

  public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

  internal static partial class Kernel32 {
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenThread(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int threadId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateThread(nint hThread, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DebugActiveProcess(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DebugActiveProcessStop(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DebugSetProcessKillOnExit([MarshalAs(UnmanagedType.Bool)] bool killOnExit);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandle(nint sourceProcess, nint sourceHandle, nint targetProcess, nint targetHandle, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint VirtualQueryEx(nint hProcess, nint address, out MEMORY_BASIC_INFORMATION buffer, nuint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteProcessMemory(nint hProcess, nint baseAddress, byte[] buffer, nuint size, out nuint written);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateRemoteThread(nint hProcess, nint threadAttributes, nuint stackSize, nint startAddress, nint parameter, uint creationFlags, out uint threadId);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string moduleName);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint hModule, string procName);
  }

  internal static partial class NtDll {
    [LibraryImport("ntdll.dll", SetLastError = true)]
    public static partial uint NtTerminateProcess(nint processHandle, uint exitStatus);

    [LibraryImport("ntdll.dll", SetLastError = true)]
    public static partial uint NtSuspendProcess(nint processHandle);
  }

  internal static partial class User32 {
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial int GetWindowThreadProcessId(nint hWnd, out int processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);
  }
}
