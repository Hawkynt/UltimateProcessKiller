using UltimateProcessKiller;

namespace UltimateProcessKiller.Cli;

/// <summary>
/// The CLI's behaviour, decoupled from <c>Main</c> and from the real OS: it takes a killer and a
/// process source, so tests can drive it with fakes and assert on its output and exit code.
/// </summary>
internal sealed class CliRunner(IProcessKiller killer, Func<IReadOnlyList<ProcessSnapshot>> listProcesses, TextWriter output) {
  public const int ExitOk = 0;
  public const int ExitFailure = 1;
  public const int ExitUsage = 2;

  private readonly IProcessKiller killer = killer;
  private readonly Func<IReadOnlyList<ProcessSnapshot>> listProcesses = listProcesses;
  private readonly TextWriter output = output;

  public int Run(CliOptions options) {
    if (options.Error is not null) {
      this.output.WriteLine($"error: {options.Error}");
      this.output.WriteLine("run 'upk --help' for usage");
      return ExitUsage;
    }

    return options.Command switch {
      CliCommand.Help => this.PrintHelp(),
      CliCommand.ListStrategies => this.PrintStrategies(),
      CliCommand.ListProcesses => this.PrintProcesses(),
      CliCommand.Terminate => this.Terminate(options),
      _ => this.PrintHelp(),
    };
  }

  private int Terminate(CliOptions options) {
    var targets = this.ResolveTargets(options, out var resolveError);
    if (resolveError is not null) {
      this.output.WriteLine($"error: {resolveError}");
      return ExitFailure;
    }

    if (targets.Count == 0) {
      this.output.WriteLine("no matching process");
      return ExitFailure;
    }

    var allGone = true;
    foreach (var pid in targets)
      allGone &= this.TerminateOne(pid, options);

    return allGone ? ExitOk : ExitFailure;
  }

  private bool TerminateOne(int pid, CliOptions options) {
    if (options.DryRun) {
      var plan = options.Escalate ? "escalate" : TerminationMethodInfo.CliName(options.Strategy!.Value);
      this.output.WriteLine($"[dry-run] would {plan} pid {pid}");
      return true;
    }

    if (options.Escalate) {
      var report = ProcessExecutioner.Escalate(this.killer, pid, new EscalationOptions {
        AllowDestructive = options.AllowDestructive,
        OnResult = r => { if (!options.Json) this.output.WriteLine($"  {DescribeResult(pid, r)}"); },
      });

      if (options.Json)
        foreach (var attempt in report.Attempts)
          this.output.WriteLine(ToJson(pid, attempt));
      else
        this.output.WriteLine(report.Succeeded ? $"pid {pid}: terminated" : $"pid {pid}: SURVIVED every rung");

      return report.Succeeded;
    }

    var method = options.Strategy!.Value;
    if (!this.killer.Supports(method)) {
      this.output.WriteLine(options.Json
        ? ToJson(pid, TerminationResult.NotSupported(method))
        : $"pid {pid}: {TerminationMethodInfo.CliName(method)} is not available on this platform");
      return false;
    }

    if (TerminationMethodInfo.IsDestructive(method) && !options.AllowDestructive) {
      this.output.WriteLine($"pid {pid}: {TerminationMethodInfo.CliName(method)} is destructive; pass --allow-destructive to use it");
      return false;
    }

    var result = this.killer.Terminate(pid, method);
    this.output.WriteLine(options.Json ? ToJson(pid, result) : DescribeResult(pid, result));
    return result.ProcessGone;
  }

  private IReadOnlyList<int> ResolveTargets(CliOptions options, out string? error) {
    error = null;
    if (options.Pid is { } pid)
      return new[] { pid };

    var matches = ProcessSnapshotSource.ByName(options.Name!);
    if (matches.Count > 1 && !options.All) {
      error = $"{matches.Count} processes match '{options.Name}'; pass --all to target them all, or use --pid";
      return Array.Empty<int>();
    }

    var ids = new int[matches.Count];
    for (var i = 0; i < matches.Count; ++i)
      ids[i] = matches[i].Id;

    return ids;
  }

  private int PrintProcesses() {
    foreach (var snapshot in this.listProcesses())
      this.output.WriteLine($"{snapshot.Id,8}  {snapshot.Name}");

    return ExitOk;
  }

  private int PrintStrategies() {
    this.output.WriteLine($"strategies supported on this platform ({this.killer.SupportedMethods.Count}):");
    foreach (var method in this.killer.SupportedMethods) {
      var flag = TerminationMethodInfo.IsDestructive(method) ? " [destructive]" : "";
      this.output.WriteLine($"  {TerminationMethodInfo.CliName(method),-18} {TerminationMethodInfo.Describe(method)}{flag}");
    }

    return ExitOk;
  }

  private int PrintHelp() {
    this.output.WriteLine(HelpText);
    return ExitOk;
  }

  private static string DescribeResult(int pid, TerminationResult r) =>
    $"pid {pid}: {TerminationMethodInfo.CliName(r.Method)} -> {r.Status} ({r.Detail})";

  private static string ToJson(int pid, TerminationResult r) =>
    $"{{\"pid\":{pid},\"strategy\":\"{TerminationMethodInfo.CliName(r.Method)}\",\"status\":\"{r.Status}\",\"detail\":{JsonString(r.Detail)}}}";

  private static string JsonString(string value) {
    var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    return $"\"{escaped}\"";
  }

  public const string HelpText = """
    UltimateProcessKiller (upk) — end stubborn processes with an escalating ladder of strategies.

    USAGE
      upk --pid <id>   --strategy <name>   [--allow-destructive] [--json] [--dry-run]
      upk --name <n>   --strategy <name>   [--all] [--allow-destructive] [--json]
      upk --pid <id>   --escalate          [--allow-destructive] [--json]
      upk --list
      upk --strategies

    TARGET
      -p, --pid <id>       Target a single process id.
      -n, --name <name>    Target processes by name (add --all for every match).

    ACTION
      -s, --strategy <n>   Apply one strategy (see --strategies for the list).
      -e, --escalate       Walk the ladder gentlest-first, stopping at the first that works.
          --allow-destructive  Permit rungs that can corrupt the target (handles, memory, threads).
          --dry-run        Print what would happen without touching anything.

    OTHER
      -l, --list           List running processes.
          --strategies     List the strategies available on this platform.
          --json           Emit one JSON object per attempt.
      -h, --help           Show this help.
    """;
}
