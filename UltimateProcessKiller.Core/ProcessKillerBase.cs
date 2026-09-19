using System.Diagnostics;

namespace UltimateProcessKiller;

/// <summary>
/// Shared plumbing for the platform killers: the supported-set gate, a portable liveness check, and
/// the "run the method then confirm the process is gone" verification that turns a raw call into a
/// <see cref="TerminationStatus.Success"/> or a hedged <see cref="TerminationStatus.Executed"/>.
/// </summary>
public abstract class ProcessKillerBase : IProcessKiller {
  /// <summary>How long to wait for a process to disappear before reporting it merely "Executed".</summary>
  protected virtual TimeSpan VerifyTimeout => TimeSpan.FromSeconds(3);

  public abstract IReadOnlyList<TerminationMethod> SupportedMethods { get; }

  public bool Supports(TerminationMethod method) {
    for (var i = 0; i < this.SupportedMethods.Count; ++i)
      if (this.SupportedMethods[i] == method)
        return true;

    return false;
  }

  public virtual bool IsZombie(int processId) => false;

  public virtual bool IsAlive(int processId) {
    if (processId <= 0)
      return false;

    try {
      using var process = Process.GetProcessById(processId);
      return !process.HasExited;
    } catch (ArgumentException) {
      return false; // no such process
    } catch (InvalidOperationException) {
      return false; // exited between lookup and query
    }
  }

  public TerminationResult Terminate(int processId, TerminationMethod method) {
    if (!this.Supports(method))
      return TerminationResult.NotSupported(method);

    // ReapZombie deliberately targets processes that are already dead, so it skips the liveness gate.
    if (method != TerminationMethod.ReapZombie && !this.IsAlive(processId) && !this.IsZombie(processId))
      return TerminationResult.NotFound(method);

    try {
      return this.Execute(processId, method);
    } catch (UnauthorizedAccessException e) {
      return new(method, TerminationStatus.AccessDenied, e.Message);
    } catch (Exception e) {
      return new(method, TerminationStatus.Failed, e.Message);
    }
  }

  /// <summary>Apply a supported method; exceptions are mapped to a result by the caller.</summary>
  protected abstract TerminationResult Execute(int processId, TerminationMethod method);

  /// <summary>Run a method, then poll until the process is gone or the verify timeout elapses.</summary>
  protected TerminationResult VerifyGone(TerminationMethod method, int processId, string detail) {
    if (this.WaitForExit(processId, this.VerifyTimeout))
      return new(method, TerminationStatus.Success, detail);

    return new(method, TerminationStatus.Executed, detail + "; process still present");
  }

  /// <summary>Poll <see cref="IsAlive"/> until it reports gone, or the timeout elapses.</summary>
  protected bool WaitForExit(int processId, TimeSpan timeout) {
    var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
    do {
      if (!this.IsAlive(processId))
        return true;

      Thread.Sleep(25);
    } while (Stopwatch.GetTimestamp() < deadline);

    return !this.IsAlive(processId);
  }
}
