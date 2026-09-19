namespace UltimateProcessKiller;

/// <summary>Static, reflection-free metadata about each <see cref="TerminationMethod"/>.</summary>
public static class TerminationMethodInfo {
  /// <summary>
  /// True for methods that can corrupt the target's state, open files or the wider system beyond a
  /// clean kill. Escalation only reaches these when explicitly allowed.
  /// </summary>
  public static bool IsDestructive(TerminationMethod method) => method switch {
    TerminationMethod.CloseHandles => true,
    TerminationMethod.OverwriteMemory => true,
    TerminationMethod.TerminateThreads => true,
    _ => false,
  };

  /// <summary>The kebab-case name used on the command line (<c>--strategy</c>).</summary>
  public static string CliName(TerminationMethod method) => method switch {
    TerminationMethod.RequestClose => "request-close",
    TerminationMethod.Kill => "kill",
    TerminationMethod.KillTree => "kill-tree",
    TerminationMethod.SuspendAndTerminate => "suspend-terminate",
    TerminationMethod.AttachDebugger => "attach-debugger",
    TerminationMethod.TerminateThreads => "terminate-threads",
    TerminationMethod.NativeTerminate => "native-terminate",
    TerminationMethod.InjectExit => "inject-exit",
    TerminationMethod.CloseHandles => "close-handles",
    TerminationMethod.OverwriteMemory => "overwrite-memory",
    TerminationMethod.ReapZombie => "reap-zombie",
    _ => method.ToString().ToLowerInvariant(),
  };

  /// <summary>One-line human description of what the method does.</summary>
  public static string Describe(TerminationMethod method) => method switch {
    TerminationMethod.RequestClose => "ask the process to close (WM_CLOSE / SIGTERM)",
    TerminationMethod.Kill => "forced kill (Process.Kill / SIGKILL)",
    TerminationMethod.KillTree => "kill the process and its descendants",
    TerminationMethod.SuspendAndTerminate => "freeze then kill (suspend+terminate / SIGSTOP+SIGKILL)",
    TerminationMethod.AttachDebugger => "attach a debugger and take the target down with it",
    TerminationMethod.TerminateThreads => "terminate the process's threads one by one",
    TerminationMethod.NativeTerminate => "native syscall kill (NtTerminateProcess)",
    TerminationMethod.InjectExit => "inject an exit call so the process unwinds itself",
    TerminationMethod.CloseHandles => "close the process's handles/descriptors out from under it",
    TerminationMethod.OverwriteMemory => "overwrite the process's memory with zeroes",
    TerminationMethod.ReapZombie => "reap a zombie via its parent (or reparent to init)",
    _ => method.ToString(),
  };

  /// <summary>Parse a CLI strategy name; returns null when unrecognised.</summary>
  public static TerminationMethod? Parse(string name) {
    foreach (var method in All)
      if (string.Equals(CliName(method), name, StringComparison.OrdinalIgnoreCase)
          || string.Equals(method.ToString(), name, StringComparison.OrdinalIgnoreCase))
        return method;

    return null;
  }

  /// <summary>Every method, in escalation order (gentlest first).</summary>
  public static readonly IReadOnlyList<TerminationMethod> All = Enum.GetValues<TerminationMethod>();
}
