using UltimateProcessKiller;
using UltimateProcessKiller.Cli;

namespace UltimateProcessKiller.Tests;

[TestFixture]
public sealed class CliRunnerTests {
  private static (int exit, string text) Run(FakeProcessKiller killer, params string[] args) {
    var writer = new StringWriter();
    var runner = new CliRunner(killer, () => new[] { new ProcessSnapshot(7, "demo") }, writer);
    var exit = runner.Run(CliOptions.Parse(args));
    return (exit, writer.ToString());
  }

  [Test]
  public void HelpReturnsZeroAndPrintsUsage() {
    var (exit, text) = Run(new FakeProcessKiller(), "--help");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(text, Does.Contain("USAGE"));
  }

  [Test]
  public void UsageErrorReturnsTwo() {
    var (exit, text) = Run(new FakeProcessKiller(), "--pid", "x");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitUsage));
    Assert.That(text, Does.Contain("error:"));
  }

  [Test]
  public void ListUsesInjectedSource() {
    var (exit, text) = Run(new FakeProcessKiller(), "--list");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(text, Does.Contain("demo"));
    Assert.That(text, Does.Contain("7"));
  }

  [Test]
  public void StrategiesListsSupported() {
    var (exit, text) = Run(new FakeProcessKiller(TerminationMethod.Kill, TerminationMethod.OverwriteMemory), "--strategies");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(text, Does.Contain("kill"));
    Assert.That(text, Does.Contain("[destructive]"));
  }

  [Test]
  public void SuccessfulKillReturnsZero() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.Kill };
    var (exit, text) = Run(killer, "--pid", "100", "--strategy", "kill");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(text, Does.Contain("Success"));
  }

  [Test]
  public void FailedKillReturnsOne() {
    var killer = new FakeProcessKiller { Effective = null };
    var (exit, _) = Run(killer, "--pid", "100", "--strategy", "kill");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitFailure));
  }

  [Test]
  public void DryRunTouchesNothing() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.Kill };
    var (exit, text) = Run(killer, "--pid", "100", "--strategy", "kill", "--dry-run");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(text, Does.Contain("[dry-run]"));
    Assert.That(killer.Attempts, Is.Empty);
  }

  [Test]
  public void DestructiveStrategyIsGatedWithoutFlag() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.OverwriteMemory };
    var (exit, text) = Run(killer, "--pid", "100", "--strategy", "overwrite-memory");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitFailure));
    Assert.That(text, Does.Contain("destructive"));
    Assert.That(killer.Attempts, Is.Empty);
  }

  [Test]
  public void DestructiveStrategyRunsWithFlag() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.OverwriteMemory };
    var (exit, _) = Run(killer, "--pid", "100", "--strategy", "overwrite-memory", "--allow-destructive");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(killer.Attempts, Does.Contain(TerminationMethod.OverwriteMemory));
  }

  [Test]
  public void UnsupportedStrategyReportsAndFails() {
    var killer = new FakeProcessKiller(TerminationMethod.Kill); // supports only Kill
    var (exit, text) = Run(killer, "--pid", "100", "--strategy", "native-terminate");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitFailure));
    Assert.That(text, Does.Contain("not available"));
  }

  [Test]
  public void JsonOutputIsEmittedPerAttempt() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.Kill };
    var (_, text) = Run(killer, "--pid", "100", "--strategy", "kill", "--json");
    Assert.That(text, Does.Contain("\"pid\":100"));
    Assert.That(text, Does.Contain("\"strategy\":\"kill\""));
    Assert.That(text, Does.Contain("\"status\":\"Success\""));
  }

  [Test]
  public void EscalateReportsTerminated() {
    var killer = new FakeProcessKiller { Effective = TerminationMethod.KillTree };
    var (exit, text) = Run(killer, "--pid", "100", "--escalate");
    Assert.That(exit, Is.EqualTo(CliRunner.ExitOk));
    Assert.That(text, Does.Contain("terminated"));
  }
}
