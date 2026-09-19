using UltimateProcessKiller;

namespace UltimateProcessKiller.Cli;

/// <summary>What the user asked the CLI to do.</summary>
internal enum CliCommand {
  Help,
  ListProcesses,
  ListStrategies,
  Terminate,
}

/// <summary>The parsed command line. <see cref="Error"/> is set when parsing failed.</summary>
internal sealed class CliOptions {
  public CliCommand Command { get; private set; } = CliCommand.Help;
  public int? Pid { get; private set; }
  public string? Name { get; private set; }
  public TerminationMethod? Strategy { get; private set; }
  public bool Escalate { get; private set; }
  public bool AllowDestructive { get; private set; }
  public bool Json { get; private set; }
  public bool DryRun { get; private set; }
  public bool All { get; private set; }        // act on every match of --name, not just a single one
  public string? Error { get; private set; }

  public static CliOptions Parse(string[] args) {
    var options = new CliOptions();
    if (args.Length == 0)
      return options; // Help

    var explicitCommand = false;
    for (var i = 0; i < args.Length; ++i) {
      var arg = args[i];
      switch (arg) {
        case "-h" or "--help":
          options.Command = CliCommand.Help;
          return options;
        case "--list" or "-l":
          options.Command = CliCommand.ListProcesses;
          explicitCommand = true;
          break;
        case "--strategies":
          options.Command = CliCommand.ListStrategies;
          explicitCommand = true;
          break;
        case "--pid" or "-p":
          if (!TryNext(args, ref i, out var pidText) || !int.TryParse(pidText, out var pid))
            return Fail(options, "--pid needs an integer process id");
          options.Pid = pid;
          break;
        case "--name" or "-n":
          if (!TryNext(args, ref i, out var name))
            return Fail(options, "--name needs a process name");
          options.Name = name;
          break;
        case "--strategy" or "-s":
          if (!TryNext(args, ref i, out var strategyText))
            return Fail(options, "--strategy needs a strategy name");
          var parsed = TerminationMethodInfo.Parse(strategyText);
          if (parsed is null)
            return Fail(options, $"unknown strategy '{strategyText}' (see --strategies)");
          options.Strategy = parsed;
          break;
        case "--escalate" or "-e":
          options.Escalate = true;
          break;
        case "--allow-destructive":
          options.AllowDestructive = true;
          break;
        case "--json":
          options.Json = true;
          break;
        case "--dry-run":
          options.DryRun = true;
          break;
        case "--all":
          options.All = true;
          break;
        default:
          return Fail(options, $"unrecognised argument '{arg}'");
      }
    }

    if (!explicitCommand) {
      if (options.Pid is null && options.Name is null)
        return options; // nothing to act on -> Help
      options.Command = CliCommand.Terminate;
    }

    if (options.Command == CliCommand.Terminate) {
      if (options.Pid is not null && options.Name is not null)
        return Fail(options, "give either --pid or --name, not both");
      if (!options.Escalate && options.Strategy is null)
        return Fail(options, "choose --strategy <name> or --escalate");
      if (options.Escalate && options.Strategy is not null)
        return Fail(options, "--escalate and --strategy are mutually exclusive");
    }

    return options;
  }

  private static bool TryNext(string[] args, ref int i, out string value) {
    if (i + 1 < args.Length) {
      value = args[++i];
      return true;
    }

    value = "";
    return false;
  }

  private static CliOptions Fail(CliOptions options, string message) {
    options.Error = message;
    return options;
  }
}
