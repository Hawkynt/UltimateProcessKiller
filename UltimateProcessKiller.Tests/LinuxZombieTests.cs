using UltimateProcessKiller;
using UltimateProcessKiller.Platforms.Linux;

namespace UltimateProcessKiller.Tests;

/// <summary>Reaping a real zombie: a killed background child whose parent is too busy to wait for it.</summary>
[TestFixture]
public sealed class LinuxZombieTests {
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

  [Test]
  public void ReapZombie_ClearsAZombieByRemovingItsStuckParent() {
    // A parent that forks a child which exits, then blocks without reaping — a lasting zombie.
    var parent = this._procs.ZombieMaker();
    if (parent is null)
      Assert.Ignore("no Python available to fork a deterministic zombie");

    var shellPid = parent.Id;
    var zombiePid = WaitForZombieChild(shellPid);
    Assert.That(zombiePid, Is.GreaterThan(0), "no zombie appeared");
    Assert.That(this._killer.IsZombie(zombiePid), Is.True);
    Assert.That(this._killer.IsAlive(zombiePid), Is.False, "a zombie is not 'alive'");

    // Record the orphan-to-be (the shell's other, foreground sleep) so teardown can clean it up.
    var others = ChildrenOf(shellPid).Where(p => p != zombiePid).ToList();

    var result = this._killer.Terminate(zombiePid, TerminationMethod.ReapZombie);
    Assert.That(result.ProcessGone, Is.True, result.Detail);
    Assert.That(Proc.Exists(zombiePid), Is.False, "the zombie was not reaped");

    foreach (var orphan in others)
      TestProcesses.ForceKill(orphan);
  }

  [Test]
  public void ReapZombie_OnALiveProcessIsRejected() {
    var pid = this._procs.Sleeper().Id;
    var result = this._killer.Terminate(pid, TerminationMethod.ReapZombie);
    Assert.That(result.Status, Is.EqualTo(TerminationStatus.Failed));
    Assert.That(result.Detail, Does.Contain("not a zombie"));
  }

  private static int WaitForZombieChild(int parentPid) {
    for (var i = 0; i < 150; ++i) {
      foreach (var pid in ChildrenOf(parentPid))
        if (Proc.State(pid) == 'Z')
          return pid;

      Thread.Sleep(20);
    }

    return -1;
  }

  private static List<int> ChildrenOf(int parentPid) {
    var kids = new List<int>();
    foreach (var dir in Directory.EnumerateDirectories("/proc"))
      if (int.TryParse(Path.GetFileName(dir), out var pid) && Proc.ParentPid(pid) == parentPid)
        kids.Add(pid);

    return kids;
  }
}
