using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltimateProcessKiller.Platforms.Windows;

/// <summary>A pid → parent-pid map from a Toolhelp process snapshot, for the GUI's process tree.</summary>
[SupportedOSPlatform("windows")]
internal static partial class ProcessTree {
  private const uint TH32CS_SNAPPROCESS = 0x00000002;

  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  private unsafe struct PROCESSENTRY32W {
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public nuint th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    public fixed char szExeFile[260];
  }

  [LibraryImport("kernel32.dll", SetLastError = true)]
  private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool Process32FirstW(nint snapshot, ref PROCESSENTRY32W entry);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool Process32NextW(nint snapshot, ref PROCESSENTRY32W entry);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool CloseHandle(nint handle);

  public static Dictionary<int, int> Parents() {
    var map = new Dictionary<int, int>();
    var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == -1 || snapshot == 0)
      return map;

    try {
      var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
      if (!Process32FirstW(snapshot, ref entry))
        return map;

      do {
        map[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
      } while (Process32NextW(snapshot, ref entry));
    } finally {
      CloseHandle(snapshot);
    }

    return map;
  }
}
