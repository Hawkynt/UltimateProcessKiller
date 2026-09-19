using UltimateProcessKiller;

namespace UltimateProcessKiller.Tests;

/// <summary>
/// An in-memory killer for testing the escalation and CLI logic without touching real processes. It
/// records every attempt and lets a test decide which method finally "works".
/// </summary>
internal sealed class FakeProcessKiller : IProcessKiller {
  private readonly HashSet<TerminationMethod> _supported;
  private bool _alive = true;

  public FakeProcessKiller(params TerminationMethod[] supported)
    => this._supported = supported.Length > 0 ? new HashSet<TerminationMethod>(supported) : new HashSet<TerminationMethod>(TerminationMethodInfo.All);

  /// <summary>The method that, when tried, actually ends the process. Null = nothing works.</summary>
  public TerminationMethod? Effective { get; set; }

  public bool IsZombieTarget { get; set; }

  public List<TerminationMethod> Attempts { get; } = new();

  public IReadOnlyList<TerminationMethod> SupportedMethods =>
    TerminationMethodInfo.All.Where(this._supported.Contains).ToArray();

  public bool Supports(TerminationMethod method) => this._supported.Contains(method);

  public bool IsAlive(int processId) => this._alive;

  public bool IsZombie(int processId) => this._alive && this.IsZombieTarget;

  public TerminationResult Terminate(int processId, TerminationMethod method) {
    this.Attempts.Add(method);
    if (!this._supported.Contains(method))
      return TerminationResult.NotSupported(method);

    if (this.Effective == method) {
      this._alive = false;
      return new TerminationResult(method, TerminationStatus.Success, "fake success");
    }

    return new TerminationResult(method, TerminationStatus.Executed, "fake no-op");
  }
}
