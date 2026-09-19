using UltimateProcessKiller;
using UltimateProcessKiller.Cli;

namespace UltimateProcessKiller.Tests;

[TestFixture]
public sealed class CliOptionsTests {
  private static CliOptions Parse(params string[] args) => CliOptions.Parse(args);

  [Test]
  public void NoArgsIsHelp() {
    var o = Parse();
    Assert.That(o.Command, Is.EqualTo(CliCommand.Help));
    Assert.That(o.Error, Is.Null);
  }

  [Test]
  public void HelpFlagWins() =>
    Assert.That(Parse("--pid", "5", "--help").Command, Is.EqualTo(CliCommand.Help));

  [Test]
  public void ListAndStrategiesAreCommands() {
    Assert.That(Parse("--list").Command, Is.EqualTo(CliCommand.ListProcesses));
    Assert.That(Parse("--strategies").Command, Is.EqualTo(CliCommand.ListStrategies));
  }

  [Test]
  public void PidWithStrategyParses() {
    var o = Parse("--pid", "1234", "--strategy", "kill");
    Assert.That(o.Command, Is.EqualTo(CliCommand.Terminate));
    Assert.That(o.Pid, Is.EqualTo(1234));
    Assert.That(o.Strategy, Is.EqualTo(TerminationMethod.Kill));
    Assert.That(o.Error, Is.Null);
  }

  [Test]
  public void EscalateParses() {
    var o = Parse("-p", "9", "-e");
    Assert.That(o.Escalate, Is.True);
    Assert.That(o.Error, Is.Null);
  }

  [Test]
  public void NonIntegerPidIsUsageError() =>
    Assert.That(Parse("--pid", "abc").Error, Is.Not.Null);

  [Test]
  public void UnknownStrategyIsError() =>
    Assert.That(Parse("--pid", "1", "--strategy", "bogus").Error, Does.Contain("unknown strategy"));

  [Test]
  public void PidAndNameTogetherIsError() =>
    Assert.That(Parse("--pid", "1", "--name", "x", "--strategy", "kill").Error, Does.Contain("not both"));

  [Test]
  public void TerminateWithoutActionIsError() =>
    Assert.That(Parse("--pid", "1").Error, Does.Contain("--strategy"));

  [Test]
  public void EscalateWithStrategyIsError() =>
    Assert.That(Parse("--pid", "1", "-e", "-s", "kill").Error, Does.Contain("mutually exclusive"));

  [Test]
  public void UnknownArgumentIsError() =>
    Assert.That(Parse("--frobnicate").Error, Does.Contain("unrecognised"));

  [Test]
  public void MissingValueForOptionIsError() =>
    Assert.That(Parse("--pid").Error, Is.Not.Null);

  [Test]
  public void FlagsAreCollected() {
    var o = Parse("--pid", "1", "--strategy", "overwrite-memory", "--allow-destructive", "--json", "--dry-run");
    Assert.That(o.AllowDestructive, Is.True);
    Assert.That(o.Json, Is.True);
    Assert.That(o.DryRun, Is.True);
    Assert.That(o.Strategy, Is.EqualTo(TerminationMethod.OverwriteMemory));
  }
}
