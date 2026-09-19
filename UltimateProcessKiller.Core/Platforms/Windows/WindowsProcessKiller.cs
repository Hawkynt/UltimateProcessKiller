using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static UltimateProcessKiller.Platforms.Windows.NativeMethods;

namespace UltimateProcessKiller.Platforms.Windows;

/// <summary>The Windows killer: the full ladder, from a WM_CLOSE request down to a memory overwrite.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsProcessKiller : ProcessKillerBase {
  private static readonly TerminationMethod[] _supported = {
    TerminationMethod.RequestClose,
    TerminationMethod.Kill,
    TerminationMethod.KillTree,
    TerminationMethod.SuspendAndTerminate,
    TerminationMethod.AttachDebugger,
    TerminationMethod.TerminateThreads,
    TerminationMethod.NativeTerminate,
    TerminationMethod.InjectExit,
    TerminationMethod.CloseHandles,
    TerminationMethod.OverwriteMemory,
  };

  public override IReadOnlyList<TerminationMethod> SupportedMethods => _supported;

  protected override TerminationResult Execute(int processId, TerminationMethod method) => method switch {
    TerminationMethod.RequestClose => this.RequestClose(processId),
    TerminationMethod.Kill => this.ManagedKill(processId, entireTree: false),
    TerminationMethod.KillTree => this.ManagedKill(processId, entireTree: true),
    TerminationMethod.SuspendAndTerminate => this.SuspendAndTerminate(processId),
    TerminationMethod.AttachDebugger => this.AttachDebugger(processId),
    TerminationMethod.TerminateThreads => this.TerminateThreads(processId),
    TerminationMethod.NativeTerminate => this.NativeTerminate(processId),
    TerminationMethod.InjectExit => this.InjectExit(processId),
    TerminationMethod.CloseHandles => this.CloseHandles(processId),
    TerminationMethod.OverwriteMemory => this.OverwriteMemory(processId),
    _ => TerminationResult.NotSupported(method),
  };

  private TerminationResult ManagedKill(int pid, bool entireTree) {
    try {
      using var process = Process.GetProcessById(pid);
      process.Kill(entireTree);
      var method = entireTree ? TerminationMethod.KillTree : TerminationMethod.Kill;
      return this.VerifyGone(method, pid, entireTree ? "Process.Kill(entireProcessTree)" : "Process.Kill()");
    } catch (ArgumentException) {
      return TerminationResult.NotFound(entireTree ? TerminationMethod.KillTree : TerminationMethod.Kill);
    }
  }

  private TerminationResult RequestClose(int pid) {
    var posted = 0;
    User32.EnumWindows((hWnd, _) => {
      User32.GetWindowThreadProcessId(hWnd, out var owner);
      if (owner == pid && User32.PostMessageW(hWnd, WM_CLOSE, 0, 0))
        ++posted;

      return true;
    }, 0);

    if (posted == 0)
      return new(TerminationMethod.RequestClose, TerminationStatus.Failed, "no top-level window to close");

    return this.VerifyGone(TerminationMethod.RequestClose, pid, $"posted WM_CLOSE to {posted} window(s)");
  }

  private TerminationResult SuspendAndTerminate(int pid) {
    using var handle = OpenOrThrow(pid, PROCESS_SUSPEND_RESUME | PROCESS_TERMINATE, TerminationMethod.SuspendAndTerminate);
    NtDll.NtSuspendProcess(handle.Handle);
    var status = NtDll.NtTerminateProcess(handle.Handle, 0);
    return status == STATUS_SUCCESS
      ? this.VerifyGone(TerminationMethod.SuspendAndTerminate, pid, "NtSuspendProcess then NtTerminateProcess")
      : new(TerminationMethod.SuspendAndTerminate, TerminationStatus.Failed, $"NtTerminateProcess status 0x{status:X8}");
  }

  private TerminationResult NativeTerminate(int pid) {
    using var handle = OpenOrThrow(pid, PROCESS_TERMINATE, TerminationMethod.NativeTerminate);
    var status = NtDll.NtTerminateProcess(handle.Handle, 0);
    return status == STATUS_SUCCESS
      ? this.VerifyGone(TerminationMethod.NativeTerminate, pid, "NtTerminateProcess")
      : new(TerminationMethod.NativeTerminate, TerminationStatus.Failed, $"NtTerminateProcess status 0x{status:X8}");
  }

  private TerminationResult TerminateThreads(int pid) {
    using var process = SafeProcess(pid, TerminationMethod.TerminateThreads);
    if (process is null)
      return TerminationResult.NotFound(TerminationMethod.TerminateThreads);

    var terminated = 0;
    var total = 0;
    foreach (ProcessThread thread in process.Threads) {
      ++total;
      var handle = Kernel32.OpenThread(THREAD_TERMINATE, false, thread.Id);
      if (handle == 0)
        continue;

      if (Kernel32.TerminateThread(handle, 0))
        ++terminated;

      Kernel32.CloseHandle(handle);
    }

    return this.VerifyGone(TerminationMethod.TerminateThreads, pid, $"TerminateThread on {terminated}/{total} thread(s)");
  }

  private TerminationResult AttachDebugger(int pid) {
    // With kill-on-exit left at its default, detaching (or this process ending) tears the debuggee down.
    if (!Kernel32.DebugActiveProcess(pid))
      return FromLastError(TerminationMethod.AttachDebugger, "DebugActiveProcess");

    Kernel32.DebugSetProcessKillOnExit(true);
    Kernel32.DebugActiveProcessStop(pid);   // detaching while kill-on-exit is set terminates the target
    return this.VerifyGone(TerminationMethod.AttachDebugger, pid, "attached debugger with kill-on-exit");
  }

  private TerminationResult InjectExit(int pid) {
    // kernel32 is mapped at the same address in every process of a session, so ExitProcess's address
    // here is also its address in the target.
    var kernel32 = Kernel32.GetModuleHandleW("kernel32.dll");
    var exitProcess = kernel32 == 0 ? 0 : Kernel32.GetProcAddress(kernel32, "ExitProcess");
    if (exitProcess == 0)
      return new(TerminationMethod.InjectExit, TerminationStatus.Failed, "could not resolve ExitProcess");

    using var handle = OpenOrThrow(pid, PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION, TerminationMethod.InjectExit);
    var thread = Kernel32.CreateRemoteThread(handle.Handle, 0, 0, exitProcess, 0, 0, out _);
    if (thread == 0)
      return FromLastError(TerminationMethod.InjectExit, "CreateRemoteThread");

    Kernel32.CloseHandle(thread);
    return this.VerifyGone(TerminationMethod.InjectExit, pid, "CreateRemoteThread -> ExitProcess");
  }

  private TerminationResult CloseHandles(int pid) {
    using var handle = OpenOrThrow(pid, PROCESS_QUERY_INFORMATION, TerminationMethod.CloseHandles);
    var closed = 0;
    // Handle values are multiples of four; sweep the low range and pull each out from under the target.
    for (var value = 4; value < 0x4000; value += 4)
      if (Kernel32.DuplicateHandle(handle.Handle, value, 0, 0, 0, false, DUPLICATE_CLOSE_SOURCE))
        ++closed;

    return new(TerminationMethod.CloseHandles, closed > 0 ? TerminationStatus.Executed : TerminationStatus.Failed,
      $"closed {closed} remote handle(s)");
  }

  private TerminationResult OverwriteMemory(int pid) {
    using var handle = OpenOrThrow(pid, PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_TERMINATE, TerminationMethod.OverwriteMemory);
    nint address = 0;
    long zeroed = 0;
    while (Kernel32.VirtualQueryEx(handle.Handle, address, out var info, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) != 0) {
      if (info.State == MEM_COMMIT && IsWritable(info.Protect)) {
        var size = (long)info.RegionSize;
        var buffer = new byte[Math.Min(size, 1 << 20)];
        long done = 0;
        while (done < size) {
          var chunk = (nuint)Math.Min(buffer.Length, size - done);
          if (Kernel32.WriteProcessMemory(handle.Handle, info.BaseAddress + (nint)done, buffer, chunk, out var written))
            zeroed += (long)written;

          done += (long)chunk;
        }
      }

      var next = (long)info.BaseAddress + (long)info.RegionSize;
      if (next <= address)
        break;

      address = (nint)next;
    }

    NtDll.NtTerminateProcess(handle.Handle, 0);
    return this.VerifyGone(TerminationMethod.OverwriteMemory, pid, $"zeroed ~{zeroed / 1024} KiB then NtTerminateProcess");
  }

  private static bool IsWritable(uint protect) =>
    protect is PAGE_READWRITE or PAGE_WRITECOPY or PAGE_EXECUTE_READWRITE or PAGE_EXECUTE_WRITECOPY;

  private static Process? SafeProcess(int pid, TerminationMethod method) {
    try {
      return Process.GetProcessById(pid);
    } catch (ArgumentException) {
      return null;
    }
  }

  private static TerminationResult FromLastError(TerminationMethod method, string what) {
    var error = Marshal.GetLastWin32Error();
    const int ERROR_ACCESS_DENIED = 5;
    if (error == ERROR_ACCESS_DENIED)
      throw new UnauthorizedAccessException($"{what}: access denied");

    return new(method, TerminationStatus.Failed, $"{what}: Win32 error {error}");
  }

  private static ProcessHandle OpenOrThrow(int pid, uint access, TerminationMethod method) {
    var handle = Kernel32.OpenProcess(access, false, pid);
    if (handle != 0)
      return new ProcessHandle(handle);

    var error = Marshal.GetLastWin32Error();
    const int ERROR_ACCESS_DENIED = 5;
    const int ERROR_INVALID_PARAMETER = 87;
    if (error == ERROR_ACCESS_DENIED)
      throw new UnauthorizedAccessException($"OpenProcess: access denied (need a more privileged token)");
    if (error == ERROR_INVALID_PARAMETER)
      throw new ArgumentException("no such process");

    throw new InvalidOperationException($"OpenProcess failed: Win32 error {error}");
  }

  private readonly struct ProcessHandle(nint handle) : IDisposable {
    public nint Handle { get; } = handle;
    public void Dispose() => Kernel32.CloseHandle(this.Handle);
  }
}
