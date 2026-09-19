using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltimateProcessKiller.Platforms.MacOS;

/// <summary>
/// The macOS killer. The Mach-level rungs (task_for_pid, thread_terminate) need code-signing
/// entitlements a portable tool cannot assume, so macOS ships the portable POSIX rungs only; the
/// rest report NotSupported honestly rather than failing at runtime.
/// </summary>
internal sealed partial class MacProcessKiller : ProcessKillerBase {
  private const int SIGTERM = 15;
  private const int SIGKILL = 9;
  private const int SIGSTOP = 17;   // note: differs from Linux (19)
  private const int ESRCH = 3;
  private const int EPERM = 1;

  [LibraryImport("libc", SetLastError = true)]
  private static partial int kill(int pid, int sig);

  private static readonly TerminationMethod[] _supported = {
    TerminationMethod.RequestClose,
    TerminationMethod.Kill,
    TerminationMethod.KillTree,
    TerminationMethod.SuspendAndTerminate,
  };

  public override IReadOnlyList<TerminationMethod> SupportedMethods => _supported;

  public override bool IsAlive(int processId) {
    if (processId <= 0)
      return false;

    // kill(pid, 0) probes existence without sending a signal.
    if (kill(processId, 0) == 0)
      return true;

    return Marshal.GetLastPInvokeError() == EPERM; // exists, but we lack permission
  }

  protected override TerminationResult Execute(int processId, TerminationMethod method) => method switch {
    TerminationMethod.RequestClose => this.Signal(processId, SIGTERM, method, "sent SIGTERM"),
    TerminationMethod.Kill => this.Signal(processId, SIGKILL, method, "sent SIGKILL"),
    TerminationMethod.KillTree => this.KillTree(processId),
    TerminationMethod.SuspendAndTerminate => this.SuspendAndTerminate(processId),
    _ => TerminationResult.NotSupported(method),
  };

  private TerminationResult Signal(int pid, int sig, TerminationMethod method, string detail) {
    if (kill(pid, sig) == 0)
      return this.VerifyGone(method, pid, detail);

    var errno = Marshal.GetLastPInvokeError();
    return errno switch {
      ESRCH => new(method, TerminationStatus.Success, detail + "; already gone"),
      EPERM => throw new UnauthorizedAccessException($"{detail}: permission denied"),
      _ => new(method, TerminationStatus.Failed, $"{detail}: errno {errno}"),
    };
  }

  private TerminationResult SuspendAndTerminate(int pid) {
    kill(pid, SIGSTOP);
    return this.Signal(pid, SIGKILL, TerminationMethod.SuspendAndTerminate, "SIGSTOP then SIGKILL");
  }

  private TerminationResult KillTree(int pid) {
    try {
      using var process = Process.GetProcessById(pid);
      process.Kill(entireProcessTree: true);
      return this.VerifyGone(TerminationMethod.KillTree, pid, "Process.Kill(entireProcessTree)");
    } catch (ArgumentException) {
      return TerminationResult.NotFound(TerminationMethod.KillTree);
    }
  }
}
