using System.Diagnostics;

namespace UltimateProcessKiller;

/// <summary>An immutable view of one process at the moment it was listed.</summary>
public readonly record struct ProcessSnapshot(int Id, string Name) {
  public override string ToString() => $"{this.Id}\t{this.Name}";
}

/// <summary>Cross-platform process discovery for the CLI and GUI.</summary>
public static class ProcessSnapshotSource {
  /// <summary>Every accessible process, ordered by name then id.</summary>
  public static IReadOnlyList<ProcessSnapshot> List() {
    var result = new List<ProcessSnapshot>();
    foreach (var process in Process.GetProcesses())
      try {
        result.Add(new ProcessSnapshot(process.Id, SafeName(process)));
      } catch {
        // A process can exit between enumeration and inspection; skip it.
      } finally {
        process.Dispose();
      }

    result.Sort(static (a, b) => {
      var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
      return byName != 0 ? byName : a.Id.CompareTo(b.Id);
    });
    return result;
  }

  /// <summary>Every process whose name matches <paramref name="name"/> (case-insensitive, with or without extension).</summary>
  public static IReadOnlyList<ProcessSnapshot> ByName(string name) {
    var trimmed = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    var matches = new List<ProcessSnapshot>();
    foreach (var snapshot in List())
      if (string.Equals(snapshot.Name, trimmed, StringComparison.OrdinalIgnoreCase))
        matches.Add(snapshot);

    return matches;
  }

  private static string SafeName(Process process) {
    try {
      return process.ProcessName;
    } catch {
      return "(unknown)";
    }
  }
}
