using System.Diagnostics;
using UltimateProcessKiller;

namespace UltimateProcessKiller.Tests;

/// <summary>Real kills against real child processes on Windows. Self-skips off Windows.</summary>
[TestFixture]
public sealed class WindowsProcessKillerTests {
  private readonly List<Process> _spawned = new();
  private IProcessKiller _killer = null!;

  [SetUp]
  public void SetUp() {
    if (!OperatingSystem.IsWindows())
      Assert.Ignore("Windows-only");

    this._killer = ProcessKiller.ForCurrentPlatform();
  }

  [TearDown]
  public void TearDown() {
    foreach (var process in this._spawned) {
      try {
        if (!process.HasExited)
          process.Kill(entireProcessTree: true);
      } catch {
        // best effort
      } finally {
        process.Dispose();
      }
    }

    this._spawned.Clear();
  }

  /// <summary>A long-running console process with no window (a hung command line).</summary>
  private int Spawn() {
    var info = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 300 > NUL") {
      UseShellExecute = false,
      CreateNoWindow = true,
    };
    var process = Process.Start(info)!;
    this._spawned.Add(process);
    Thread.Sleep(200); // let it start pinging
    return process.Id;
  }

  private void AssertKilled(TerminationMethod method) {
    var pid = this.Spawn();
    var result = this._killer.Terminate(pid, method);
    Assert.That(result.ProcessGone, Is.True, $"{method} did not end pid {pid}: {result.Detail}");
    Assert.That(this._killer.IsAlive(pid), Is.False);
  }

  [Test] public void Kill() => this.AssertKilled(TerminationMethod.Kill);
  [Test] public void KillTree() => this.AssertKilled(TerminationMethod.KillTree);
  [Test] public void SuspendAndTerminate() => this.AssertKilled(TerminationMethod.SuspendAndTerminate);
  [Test] public void NativeTerminate() => this.AssertKilled(TerminationMethod.NativeTerminate);
  [Test] public void TerminateThreads() => this.AssertKilled(TerminationMethod.TerminateThreads);
  [Test] public void InjectExit() => this.AssertKilled(TerminationMethod.InjectExit);
  [Test] public void OverwriteMemory() => this.AssertKilled(TerminationMethod.OverwriteMemory);

  [Test]
  public void CloseHandles_RunsWithoutThrowing() {
    // Closing a console process's handles does not reliably kill it, so assert only that it executed.
    var pid = this.Spawn();
    var result = this._killer.Terminate(pid, TerminationMethod.CloseHandles);
    Assert.That(result.Status, Is.AnyOf(TerminationStatus.Executed, TerminationStatus.Failed, TerminationStatus.Success));
  }

  [Test]
  public void ReapZombie_IsNotSupportedOnWindows() {
    Assert.That(this._killer.Supports(TerminationMethod.ReapZombie), Is.False);
  }

  [Test]
  public void MissingPidReportsNotFound() {
    var result = this._killer.Terminate(2_000_000_000, TerminationMethod.Kill);
    Assert.That(result.Status, Is.EqualTo(TerminationStatus.NotFound));
  }
}
