using UltimateProcessKiller;
using UltimateProcessKiller.Platforms.Linux;

namespace UltimateProcessKiller.Tests;

/// <summary>
/// Real kills against real child processes on Linux. The targets are children of the test host, so
/// the ptrace-based rungs (attach-debugger, inject-exit, overwrite-memory) are permitted even under
/// yama ptrace_scope=1. The whole fixture self-skips off Linux.
/// </summary>
[TestFixture]
public sealed class LinuxProcessKillerTests {
  private TestProcesses _procs = null!;
  private LinuxProcessKiller _killer = null!;

  [SetUp]
  public void SetUp() {
    if (!OperatingSystem.IsLinux())
      Assert.Ignore("Linux-only");

    this._procs = new TestProcesses();
    this._killer = new LinuxProcessKiller();
  }

  [TearDown]
  public void TearDown() => this._procs?.Dispose();

  private void AssertKilled(int pid, TerminationResult result) {
    Assert.That(result.ProcessGone, Is.True, $"{result.Method} did not end pid {pid}: {result.Detail}");
    Assert.That(this._killer.IsAlive(pid), Is.False, $"{result.Method} left pid {pid} alive");
  }

  [Test]
  public void RequestClose_KillsAProcessThatHonoursSigterm() {
    var pid = this._procs.Sleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.RequestClose));
  }

  [Test]
  public void RequestClose_LeavesAStubbornProcessAlive() {
    var pid = this._procs.StubbornSleeper().Id;
    var result = this._killer.Terminate(pid, TerminationMethod.RequestClose);
    Assert.That(result.Status, Is.EqualTo(TerminationStatus.Executed));
    Assert.That(this._killer.IsAlive(pid), Is.True);

    // and the forceful rung still gets it
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.Kill));
  }

  [Test]
  public void Kill_EndsAStubbornProcess() {
    var pid = this._procs.StubbornSleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.Kill));
  }

  [Test]
  public void SuspendAndTerminate_EndsAStubbornProcess() {
    var pid = this._procs.StubbornSleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.SuspendAndTerminate));
  }

  [Test]
  public void TerminateThreads_EndsAStubbornProcess() {
    var pid = this._procs.StubbornSleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.TerminateThreads));
  }

  [Test]
  public void AttachDebugger_EndsAChildProcess() {
    var pid = this._procs.StubbornSleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.AttachDebugger));
  }

  [Test]
  public void InjectExit_EndsAChildProcess() {
    if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
      Assert.Ignore("inject-exit is implemented for x86_64 only");

    var pid = this._procs.StubbornSleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.InjectExit));
  }

  [Test]
  public void OverwriteMemory_EndsAChildProcess() {
    var pid = this._procs.StubbornSleeper().Id;
    this.AssertKilled(pid, this._killer.Terminate(pid, TerminationMethod.OverwriteMemory));
  }

  [Test]
  public void KillTree_TakesTheChildrenToo() {
    var parent = this._procs.Tree(children: 2);
    var parentPid = parent.Id;

    // Give the shell a moment to fork its children.
    var childPids = WaitForChildren(parentPid, expected: 2);
    Assert.That(childPids.Count, Is.GreaterThanOrEqualTo(1), "shell never forked its children");

    var result = this._killer.Terminate(parentPid, TerminationMethod.KillTree);
    Assert.That(result.ProcessGone, Is.True, result.Detail);

    foreach (var child in childPids)
      Assert.That(WaitGone(child), Is.True, $"child {child} survived the tree kill");
  }

  [Test]
  public void Terminate_OnMissingPidReportsNotFound() {
    var result = this._killer.Terminate(2_000_000_000, TerminationMethod.Kill);
    Assert.That(result.Status, Is.EqualTo(TerminationStatus.NotFound));
  }

  [Test]
  public void NativeTerminate_IsNotSupportedOnLinux() {
    var pid = this._procs.Sleeper().Id;
    Assert.That(this._killer.Terminate(pid, TerminationMethod.NativeTerminate).Status, Is.EqualTo(TerminationStatus.NotSupported));
  }

  private static List<int> WaitForChildren(int parentPid, int expected) {
    for (var i = 0; i < 100; ++i) {
      var kids = ChildrenOf(parentPid);
      if (kids.Count >= expected)
        return kids;

      Thread.Sleep(20);
    }

    return ChildrenOf(parentPid);
  }

  private static List<int> ChildrenOf(int parentPid) {
    var kids = new List<int>();
    foreach (var dir in Directory.EnumerateDirectories("/proc"))
      if (int.TryParse(Path.GetFileName(dir), out var pid) && Proc.ParentPid(pid) == parentPid)
        kids.Add(pid);

    return kids;
  }

  private static bool WaitGone(int pid) {
    for (var i = 0; i < 100; ++i) {
      if (!Proc.Exists(pid) || Proc.State(pid) is 'Z' or 'X')
        return true;

      Thread.Sleep(20);
    }

    return !Proc.Exists(pid);
  }
}
