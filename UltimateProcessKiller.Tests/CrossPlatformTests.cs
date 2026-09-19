using UltimateProcessKiller;

namespace UltimateProcessKiller.Tests;

[TestFixture]
public sealed class CrossPlatformTests {
  [Test]
  public void FactoryReturnsAKillerThatCanAtLeastKill() {
    var killer = ProcessKiller.ForCurrentPlatform();
    Assert.That(killer.Supports(TerminationMethod.Kill), Is.True);
    Assert.That(killer.Supports(TerminationMethod.KillTree), Is.True);
    Assert.That(killer.SupportedMethods, Is.Not.Empty);
  }

  [Test]
  public void SupportedMethodsAreOrderedGentlestFirst() {
    var killer = ProcessKiller.ForCurrentPlatform();
    var indices = killer.SupportedMethods
      .Select(m => TerminationMethodInfo.All.ToList().IndexOf(m))
      .ToList();

    Assert.That(indices, Is.Ordered.Ascending);
  }

  [Test]
  public void ListIncludesTheTestHostItself() {
    var self = Environment.ProcessId;
    var snapshot = ProcessSnapshotSource.List();
    Assert.That(snapshot.Select(s => s.Id), Does.Contain(self));
  }

  [Test]
  public void IsAliveIsTrueForSelfAndFalseForImpossiblePid() {
    var killer = ProcessKiller.ForCurrentPlatform();
    Assert.That(killer.IsAlive(Environment.ProcessId), Is.True);
    Assert.That(killer.IsAlive(2_000_000_000), Is.False);
    Assert.That(killer.IsAlive(-1), Is.False);
  }

  [Test]
  public void ByNameFindsTheTestHostAndIgnoresExeSuffix() {
    var self = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
    Assert.That(ProcessSnapshotSource.ByName(self).Select(s => s.Id), Does.Contain(Environment.ProcessId));
    Assert.That(ProcessSnapshotSource.ByName(self + ".exe").Select(s => s.Id), Does.Contain(Environment.ProcessId));
  }
}
