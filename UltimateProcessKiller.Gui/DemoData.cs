using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// Deterministic data for <c>--demo</c> so the screenshots are reproducible: a fixed process list
/// and a killer that only pretends, chosen so a shot never depends on whatever happens to be running.
/// </summary>
internal static class DemoData {
  public static IReadOnlyList<ProcessSnapshot> Processes { get; } = new[] {
    new ProcessSnapshot(4821, "stubborn-daemon"),
    new ProcessSnapshot(1337, "hung-editor"),
    new ProcessSnapshot(2048, "runaway-worker"),
    new ProcessSnapshot(777, "zombie-parent"),
    new ProcessSnapshot(9001, "leaky-service"),
    new ProcessSnapshot(160, "systemd"),
    new ProcessSnapshot(512, "pulseaudio"),
    new ProcessSnapshot(1024, "compositor"),
  };

  /// <summary>A killer that reports every strategy as supported but never touches anything.</summary>
  public sealed class NoOpKiller : IProcessKiller {
    public IReadOnlyList<TerminationMethod> SupportedMethods => TerminationMethodInfo.All;
    public bool Supports(TerminationMethod method) => true;
    public bool IsAlive(int processId) => true;
    public bool IsZombie(int processId) => false;
    public TerminationResult Terminate(int processId, TerminationMethod method)
      => new(method, TerminationStatus.Executed, "demo — nothing was touched");
  }
}
