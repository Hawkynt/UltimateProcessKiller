using System.Diagnostics;
using System.Runtime.Versioning;

namespace UltimateProcessKiller;

/// <summary>An immutable view of one process at the moment it was listed.</summary>
public readonly record struct ProcessSnapshot(int Id, string Name, int ParentId = 0) {
  public override string ToString() => $"{this.Id}\t{this.Name}";
}

/// <summary>Cross-platform process discovery for the CLI and GUI.</summary>
public static class ProcessSnapshotSource {
  private static readonly IReadOnlyDictionary<int, int> _noParents = new Dictionary<int, int>();

  /// <summary>Every accessible process, ordered by name then id, each carrying its parent id where known.</summary>
  public static IReadOnlyList<ProcessSnapshot> List() {
    var parents = ParentMap();
    var result = new List<ProcessSnapshot>();
    foreach (var process in Process.GetProcesses())
      try {
        result.Add(new ProcessSnapshot(process.Id, SafeName(process), parents.TryGetValue(process.Id, out var ppid) ? ppid : 0));
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

  /// <summary>A pid → parent-pid map for the current platform, or empty where it cannot be determined.</summary>
  private static IReadOnlyDictionary<int, int> ParentMap() {
    try {
      if (OperatingSystem.IsLinux())
        return LinuxParents();

      if (OperatingSystem.IsWindows())
        return WindowsParents();
    } catch {
      // Any discovery failure just means a flat list — never a crash.
    }

    return _noParents;
  }

  [SupportedOSPlatform("linux")]
  private static Dictionary<int, int> LinuxParents() {
    var map = new Dictionary<int, int>();
    foreach (var dir in Directory.EnumerateDirectories("/proc")) {
      if (!int.TryParse(Path.GetFileName(dir), out var pid))
        continue;

      try {
        // stat is "pid (comm) STATE PPID ..."; comm can hold spaces/parens, so read after the last ')'.
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var close = stat.LastIndexOf(')');
        if (close < 0 || close + 2 >= stat.Length)
          continue;

        var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length > 1 && int.TryParse(fields[1], out var ppid))
          map[pid] = ppid;
      } catch {
        // process vanished; skip
      }
    }

    return map;
  }

  [SupportedOSPlatform("windows")]
  private static Dictionary<int, int> WindowsParents() => Platforms.Windows.ProcessTree.Parents();
}
