namespace UltimateProcessKiller;

/// <summary>
/// A single way to end a process, from the gentlest request to the most destructive intervention.
/// The values are ordered by invasiveness, which is also the order <see cref="ProcessExecutioner"/>
/// walks them when escalating.
/// </summary>
public enum TerminationMethod {
  /// <summary>Ask the process to close itself (WM_CLOSE on Windows, SIGTERM on POSIX).</summary>
  RequestClose,

  /// <summary>The ordinary forced kill (<see cref="System.Diagnostics.Process.Kill()"/> / SIGKILL).</summary>
  Kill,

  /// <summary>Kill the process together with its whole descendant tree.</summary>
  KillTree,

  /// <summary>Freeze the process so it cannot react, then kill it (suspend + terminate / SIGSTOP + SIGKILL).</summary>
  SuspendAndTerminate,

  /// <summary>Attach as a debugger; leaving kill-on-exit set takes the target down with the debugger.</summary>
  AttachDebugger,

  /// <summary>Terminate the process's threads one by one (TerminateThread / tgkill / thread_terminate).</summary>
  TerminateThreads,

  /// <summary>The native syscall kill that can succeed where the managed one is blocked (NtTerminateProcess).</summary>
  NativeTerminate,

  /// <summary>Make the process exit from inside itself so its own runtime unwinds (inject a call to exit).</summary>
  InjectExit,

  /// <summary>Close the process's handles/descriptors out from under it, freeing its locks (destructive).</summary>
  CloseHandles,

  /// <summary>Overwrite the process's committed memory with zeroes — the nuclear option (destructive).</summary>
  OverwriteMemory,

  /// <summary>Reap a zombie: make its parent collect it, or reparent it to init so init reaps it (Linux).</summary>
  ReapZombie,
}
