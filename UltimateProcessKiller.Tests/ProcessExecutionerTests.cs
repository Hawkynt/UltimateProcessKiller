using UltimateProcessKiller;

namespace UltimateProcessKiller.Tests;

[TestFixture]
public sealed class ProcessExecutionerTests {
  [Test]
  public void StopsAtTheFirstRungThatWorks() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.Kill };
    var report = ProcessExecutioner.Escalate(killer, 42);

    Assert.That(report.Succeeded, Is.True);
    Assert.That(report.Attempts[^1].Method, Is.EqualTo(TerminationMethod.Kill));
    Assert.That(killer.Attempts, Does.Not.Contain(TerminationMethod.SuspendAndTerminate));
  }

  [Test]
  public void SkipsDestructiveRungsByDefault() {
    var killer = new FakeProcessKiller { Effective = null }; // nothing works
    var report = ProcessExecutioner.Escalate(killer, 42);

    Assert.That(report.Succeeded, Is.False);
    Assert.That(killer.Attempts, Does.Not.Contain(TerminationMethod.OverwriteMemory));
    Assert.That(killer.Attempts, Does.Not.Contain(TerminationMethod.CloseHandles));
    Assert.That(killer.Attempts, Does.Not.Contain(TerminationMethod.TerminateThreads));
  }

  [Test]
  public void ReachesDestructiveRungsWhenAllowed() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.OverwriteMemory };
    var report = ProcessExecutioner.Escalate(killer, 42, new EscalationOptions { AllowDestructive = true });

    Assert.That(report.Succeeded, Is.True);
    Assert.That(killer.Attempts, Does.Contain(TerminationMethod.OverwriteMemory));
  }

  [Test]
  public void NeverTriesReapZombieDuringAGeneralEscalation() {
    var killer = new FakeProcessKiller { Effective = null };
    ProcessExecutioner.Escalate(killer, 42, new EscalationOptions { AllowDestructive = true });
    Assert.That(killer.Attempts, Does.Not.Contain(TerminationMethod.ReapZombie));
  }

  [Test]
  public void AlreadyGoneProcessSucceedsWithNoAttempts() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.Kill };
    killer.Terminate(1, TerminationMethod.Kill);  // flips it dead
    killer.Attempts.Clear();

    var report = ProcessExecutioner.Escalate(killer, 1);
    Assert.That(report.Succeeded, Is.True);
    Assert.That(report.Attempts, Is.Empty);
  }

  [Test]
  public void WalksInGentlestFirstOrder() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.SuspendAndTerminate };
    ProcessExecutioner.Escalate(killer, 42);

    Assert.That(killer.Attempts, Is.EqualTo(new[] {
      TerminationMethod.RequestClose,
      TerminationMethod.Kill,
      TerminationMethod.KillTree,
      TerminationMethod.SuspendAndTerminate,
    }));
  }

  [Test]
  public void OnlySubsetRestrictsTheLadder() {
    var killer = new FakeProcessKiller { Effective = null };
    ProcessExecutioner.Escalate(killer, 42, new EscalationOptions {
      Only = new[] { TerminationMethod.Kill, TerminationMethod.NativeTerminate },
    });

    Assert.That(killer.Attempts, Is.EqualTo(new[] { TerminationMethod.Kill, TerminationMethod.NativeTerminate }));
  }
}
