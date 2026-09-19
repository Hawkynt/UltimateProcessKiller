using UltimateProcessKiller;
using UltimateProcessKiller.Cli;

var options = CliOptions.Parse(args);

IProcessKiller killer;
try {
  killer = ProcessKiller.ForCurrentPlatform();
} catch (PlatformNotSupportedException e) {
  Console.Error.WriteLine(e.Message);
  return CliRunner.ExitFailure;
}

var runner = new CliRunner(killer, ProcessSnapshotSource.List, Console.Out);
return runner.Run(options);
