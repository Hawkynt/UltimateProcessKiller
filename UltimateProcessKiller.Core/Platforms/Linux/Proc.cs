using System.Globalization;

namespace UltimateProcessKiller.Platforms.Linux;

/// <summary>Reads the bits of <c>/proc</c> the Linux killer needs: liveness, state, parent, threads, maps.</summary>
internal static class Proc {
  /// <summary>True when <c>/proc/&lt;pid&gt;</c> exists at all (alive, stopped or zombie).</summary>
  public static bool Exists(int pid) => Directory.Exists($"/proc/{pid}");

  /// <summary>The single-letter state from <c>/proc/&lt;pid&gt;/stat</c> ('R','S','D','T','Z','X'), or '\0'.</summary>
  public static char State(int pid) {
    try {
      // stat format: "pid (comm) STATE ...". comm can contain spaces and parens, so read after the last ')'.
      var stat = File.ReadAllText($"/proc/{pid}/stat");
      var close = stat.LastIndexOf(')');
      if (close < 0 || close + 2 >= stat.Length)
        return '\0';

      return stat[close + 2];
    } catch {
      return '\0';
    }
  }

  public static bool IsZombie(int pid) => State(pid) == 'Z';

  /// <summary>The parent pid from <c>/proc/&lt;pid&gt;/status</c>, or -1.</summary>
  public static int ParentPid(int pid) {
    try {
      foreach (var line in File.ReadLines($"/proc/{pid}/status"))
        if (line.StartsWith("PPid:", StringComparison.Ordinal))
          return int.TryParse(line.AsSpan(5).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ppid) ? ppid : -1;
    } catch {
      // fall through
    }

    return -1;
  }

  /// <summary>The thread ids of a process (contents of <c>/proc/&lt;pid&gt;/task</c>).</summary>
  public static IReadOnlyList<int> ThreadIds(int pid) {
    var ids = new List<int>();
    try {
      foreach (var dir in Directory.EnumerateDirectories($"/proc/{pid}/task"))
        if (int.TryParse(Path.GetFileName(dir), out var tid))
          ids.Add(tid);
    } catch {
      // process gone or not readable
    }

    return ids;
  }

  /// <summary>Writable, private regions from <c>/proc/&lt;pid&gt;/maps</c> as (start, end) pairs.</summary>
  public static IReadOnlyList<(ulong Start, ulong End)> WritableRegions(int pid) {
    var regions = new List<(ulong, ulong)>();
    try {
      foreach (var line in File.ReadLines($"/proc/{pid}/maps")) {
        // format: "start-end perms offset dev inode pathname"
        var space = line.IndexOf(' ');
        if (space < 0 || space + 3 >= line.Length)
          continue;

        var perms = line.Substring(space + 1, 4);
        if (perms[1] != 'w')            // not writable
          continue;

        // Skip file-backed and special mappings; only touch anonymous heap/stack/private data.
        var path = line.Length > space + 6 ? line[(space + 6)..].TrimStart() : "";
        var name = LastField(path);
        if (name.Length > 0 && name[0] == '/')
          continue;                     // file-backed
        if (name is "[vvar]" or "[vdso]" or "[vsyscall]")
          continue;

        var dash = line.IndexOf('-');
        if (dash < 0 || dash >= space)
          continue;

        if (ulong.TryParse(line.AsSpan(0, dash), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var start)
            && ulong.TryParse(line.AsSpan(dash + 1, space - dash - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var end)
            && end > start)
          regions.Add((start, end));
      }
    } catch {
      // process gone or not readable
    }

    return regions;
  }

  private static string LastField(string mapsTail) {
    // The pathname (if any) is the trailing field; anonymous regions have none.
    var trimmed = mapsTail.Trim();
    return trimmed;
  }
}
