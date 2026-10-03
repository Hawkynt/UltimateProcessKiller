using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// Deterministic data for <c>--demo</c> so the screenshots are reproducible: a fixed process tree and
/// a killer that only pretends, chosen so a shot never depends on whatever happens to be running.
/// </summary>
internal static class DemoData {
  // Parent ids wire these into a small tree rooted at systemd, so the tree view has something to show.
  public static IReadOnlyList<ProcessSnapshot> Processes { get; } = new[] {
    new ProcessSnapshot(160, "systemd", 0),
    new ProcessSnapshot(300, "NetworkManager", 160),
    new ProcessSnapshot(310, "sshd", 160),
    new ProcessSnapshot(311, "bash", 310),
    new ProcessSnapshot(512, "pulseaudio", 160),
    new ProcessSnapshot(1024, "compositor", 160),
    new ProcessSnapshot(1025, "xterm", 1024),
    new ProcessSnapshot(1337, "hung-editor", 1025),
    new ProcessSnapshot(777, "zombie-parent", 160),
    new ProcessSnapshot(778, "defunct-child", 777),
    new ProcessSnapshot(9001, "leaky-service", 160),
    new ProcessSnapshot(9002, "leaky-worker", 9001),
    new ProcessSnapshot(4821, "stubborn-daemon", 160),
    new ProcessSnapshot(2048, "runaway-worker", 4821),
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
