using UltimateProcessKiller;

namespace UltimateProcessKiller.Tests;

[TestFixture]
public sealed class TerminationMethodInfoTests {
  [Test]
  public void EveryMethodHasAUniqueCliName() {
    var names = TerminationMethodInfo.All.Select(TerminationMethodInfo.CliName).ToArray();
    Assert.That(names, Is.Unique);
    Assert.That(names, Has.All.Not.Empty);
  }

  [Test]
  public void CliNameRoundTripsThroughParse() {
    foreach (var method in TerminationMethodInfo.All) {
      var parsed = TerminationMethodInfo.Parse(TerminationMethodInfo.CliName(method));
      Assert.That(parsed, Is.EqualTo(method), $"round trip failed for {method}");
    }
  }

  [Test]
  public void ParseAcceptsEnumNameCaseInsensitively() {
    Assert.That(TerminationMethodInfo.Parse("kill"), Is.EqualTo(TerminationMethod.Kill));
    Assert.That(TerminationMethodInfo.Parse("KILL"), Is.EqualTo(TerminationMethod.Kill));
    Assert.That(TerminationMethodInfo.Parse("NativeTerminate"), Is.EqualTo(TerminationMethod.NativeTerminate));
    Assert.That(TerminationMethodInfo.Parse("native-terminate"), Is.EqualTo(TerminationMethod.NativeTerminate));
  }

  [Test]
  public void ParseRejectsGarbage() => Assert.That(TerminationMethodInfo.Parse("nope"), Is.Null);

  [Test]
  public void DestructiveSetIsExactlyHandlesMemoryAndThreads() {
    var destructive = TerminationMethodInfo.All.Where(TerminationMethodInfo.IsDestructive).ToArray();
    Assert.That(destructive, Is.EquivalentTo(new[] {
      TerminationMethod.TerminateThreads,
      TerminationMethod.CloseHandles,
      TerminationMethod.OverwriteMemory,
    }));
  }

  [Test]
  public void MethodsAreOrderedGentlestFirst() {
    var all = TerminationMethodInfo.All.ToList();
    Assert.That(all[0], Is.EqualTo(TerminationMethod.RequestClose));
    Assert.That(all.IndexOf(TerminationMethod.RequestClose), Is.LessThan(all.IndexOf(TerminationMethod.OverwriteMemory)));
    Assert.That(all.IndexOf(TerminationMethod.Kill), Is.LessThan(all.IndexOf(TerminationMethod.InjectExit)));
  }
}
