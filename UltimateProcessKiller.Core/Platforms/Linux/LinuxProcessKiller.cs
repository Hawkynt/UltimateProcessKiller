using System.Runtime.InteropServices;
using static UltimateProcessKiller.Platforms.Linux.LibC;

namespace UltimateProcessKiller.Platforms.Linux;

/// <summary>The Linux killer: POSIX signals for the portable rungs, ptrace and /proc for the deep ones.</summary>
internal sealed partial class LinuxProcessKiller : ProcessKillerBase {
  private static readonly TerminationMethod[] _supported = {
    TerminationMethod.RequestClose,
    TerminationMethod.Kill,
    TerminationMethod.KillTree,
    TerminationMethod.SuspendAndTerminate,
    TerminationMethod.AttachDebugger,
    TerminationMethod.TerminateThreads,
    TerminationMethod.InjectExit,
    TerminationMethod.OverwriteMemory,
    TerminationMethod.ReapZombie,
  };

  public override IReadOnlyList<TerminationMethod> SupportedMethods => _supported;

  public override bool IsAlive(int processId) {
    if (processId <= 0)
      return false;

    var state = Proc.State(processId);
    return state is not ('\0' or 'Z' or 'X');
  }

  public override bool IsZombie(int processId) => Proc.IsZombie(processId);

  protected override TerminationResult Execute(int processId, TerminationMethod method) => method switch {
    TerminationMethod.RequestClose => this.Signal(processId, SIGTERM, method, "sent SIGTERM"),
    TerminationMethod.Kill => this.Signal(processId, SIGKILL, method, "sent SIGKILL"),
    TerminationMethod.KillTree => this.KillTree(processId),
    TerminationMethod.SuspendAndTerminate => this.SuspendAndTerminate(processId),
    TerminationMethod.AttachDebugger => this.AttachDebugger(processId),
    TerminationMethod.TerminateThreads => this.TerminateThreads(processId),
    TerminationMethod.InjectExit => this.InjectExit(processId),
    TerminationMethod.OverwriteMemory => this.OverwriteMemory(processId),
    TerminationMethod.ReapZombie => this.ReapZombie(processId),
    _ => TerminationResult.NotSupported(method),
  };

  /// <summary>Send a signal, mapping errno to the right status, then verify the process is gone.</summary>
  private TerminationResult Signal(int pid, int sig, TerminationMethod method, string detail) {
    if (kill(pid, sig) == 0)
      return this.VerifyGone(method, pid, detail);

    return FromErrno(method, detail);
  }

  private static TerminationResult FromErrno(TerminationMethod method, string detail) {
    var errno = Marshal.GetLastPInvokeError();
    return errno switch {
      ESRCH => new(method, TerminationStatus.Success, detail + "; already gone"),
      EPERM => throw new UnauthorizedAccessException($"{detail}: permission denied (errno EPERM)"),
      _ => new(method, TerminationStatus.Failed, $"{detail}: errno {errno}"),
    };
  }

  private TerminationResult KillTree(int pid) {
    // Kill descendants leaf-first so a parent cannot re-fork after we clear its children.
    var order = new List<int>();
    Collect(pid, order);
    order.Reverse();
    foreach (var member in order)
      kill(member, SIGKILL);

    kill(pid, SIGKILL);
    return this.VerifyGone(TerminationMethod.KillTree, pid, $"killed {order.Count} descendant(s) then the process");

    static void Collect(int root, List<int> into) {
      foreach (var dir in SafeEnumerate("/proc"))
        if (int.TryParse(Path.GetFileName(dir), out var candidate) && Proc.ParentPid(candidate) == root) {
          into.Add(candidate);
          Collect(candidate, into);
        }
    }

    static IEnumerable<string> SafeEnumerate(string path) {
      try {
        return Directory.EnumerateDirectories(path);
      } catch {
        return Array.Empty<string>();
      }
    }
  }

  private TerminationResult SuspendAndTerminate(int pid) {
    kill(pid, SIGSTOP);
    if (kill(pid, SIGKILL) == 0)
      return this.VerifyGone(TerminationMethod.SuspendAndTerminate, pid, "SIGSTOP then SIGKILL");

    return FromErrno(TerminationMethod.SuspendAndTerminate, "SIGSTOP then SIGKILL");
  }

  private TerminationResult TerminateThreads(int pid) {
    var tids = Proc.ThreadIds(pid);
    if (tids.Count == 0)
      return new(TerminationMethod.TerminateThreads, TerminationStatus.Failed, "no threads enumerated");

    var signalled = 0;
    foreach (var tid in tids)
      if (TgKill(pid, tid, SIGKILL) == 0)
        ++signalled;

    return this.VerifyGone(TerminationMethod.TerminateThreads, pid, $"tgkill SIGKILL to {signalled}/{tids.Count} thread(s)");
  }

  private TerminationResult AttachDebugger(int pid) {
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0)
      return FromErrno(TerminationMethod.AttachDebugger, "ptrace(ATTACH)");

    waitpid(pid, out _, 0);         // wait for the tracee to stop
    kill(pid, SIGKILL);             // a tracer's exit would kill the tracee anyway; be explicit
    ptrace(PTRACE_CONT, pid, 0, SIGKILL);
    ptrace(PTRACE_DETACH, pid, 0, 0);
    return this.VerifyGone(TerminationMethod.AttachDebugger, pid, "attached as debugger, killed the tracee");
  }

  private TerminationResult ReapZombie(int pid) {
    if (!Proc.Exists(pid))
      return new(TerminationMethod.ReapZombie, TerminationStatus.Success, "already reaped");

    if (!Proc.IsZombie(pid))
      return new(TerminationMethod.ReapZombie, TerminationStatus.Failed, "not a zombie; use a kill method");

    // If we happen to be the parent, reaping is a plain waitpid.
    if (waitpid(pid, out _, WNOHANG) == pid)
      return new(TerminationMethod.ReapZombie, TerminationStatus.Success, "reaped as parent (waitpid)");

    var parent = Proc.ParentPid(pid);

    // Nudge the real parent to reap by delivering SIGCHLD.
    if (parent > 1) {
      kill(parent, SIGCHLD);
      if (this.WaitForZombieToClear(pid))
        return new(TerminationMethod.ReapZombie, TerminationStatus.Success, $"parent {parent} reaped after SIGCHLD");

      // Parent is stuck. Killing it reparents the zombie to init/subreaper, which reaps it.
      kill(parent, SIGKILL);
      if (this.WaitForZombieToClear(pid))
        return new(TerminationMethod.ReapZombie, TerminationStatus.Success, $"killed stuck parent {parent}; init reaped the zombie");
    }

    return Proc.Exists(pid)
      ? new(TerminationMethod.ReapZombie, TerminationStatus.Executed, "zombie persists; its parent may be init or unkillable")
      : new(TerminationMethod.ReapZombie, TerminationStatus.Success, "zombie cleared");
  }

  private bool WaitForZombieToClear(int pid) {
    for (var i = 0; i < 40; ++i) {
      if (!Proc.Exists(pid) || Proc.State(pid) != 'Z')
        return true;

      Thread.Sleep(25);
    }

    return !Proc.Exists(pid);
  }
}
