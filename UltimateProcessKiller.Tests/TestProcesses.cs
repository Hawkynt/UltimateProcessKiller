using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltimateProcessKiller.Tests;

/// <summary>
/// Spawns disposable child processes for integration tests and guarantees they are cleaned up, so a
/// failing assertion can never leak a runaway <c>sleep</c> onto the machine or the CI runner.
/// </summary>
internal sealed partial class TestProcesses : IDisposable {
  private readonly List<Process> _spawned = new();

  [LibraryImport("libc", SetLastError = true)]
  private static partial int kill(int pid, int sig);

  /// <summary>A plain sleeper that dies on SIGTERM (the gentle rungs work on it).</summary>
  public Process Sleeper(int seconds = 120) => this.Start("/bin/sleep", seconds.ToString());

  /// <summary>
  /// A shell that ignores SIGTERM, to prove the forceful rungs are needed. It prints "ready" only
  /// once the trap is installed, and this call blocks until then — otherwise a test could deliver
  /// SIGTERM in the window before the trap exists and see the default (fatal) disposition instead.
  /// </summary>
  public Process StubbornSleeper() {
    var process = this.Start("/bin/sh", "-c", "trap '' TERM; echo ready; while :; do sleep 0.2; done");
    WaitForReady(process);
    return process;
  }

  private static void WaitForReady(Process process) {
    var line = process.StandardOutput.ReadLine();       // blocks until the trap is armed
    if (line is null)
      throw new InvalidOperationException("stubborn helper exited before signalling readiness");
  }

  /// <summary>A parent with <paramref name="children"/> child sleepers, for tree kills.</summary>
  public Process Tree(int children = 2) {
    var backgrounds = string.Concat(Enumerable.Repeat("sleep 120 & ", children));
    return this.Start("/bin/sh", "-c", backgrounds + "wait");
  }

  public Process Start(string file, params string[] args) {
    var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in args)
      info.ArgumentList.Add(arg);

    var process = Process.Start(info)!;
    this._spawned.Add(process);
    return process;
  }

  public void Dispose() {
    foreach (var process in this._spawned) {
      try {
        if (!process.HasExited)
          process.Kill(entireProcessTree: true);
      } catch {
        // best effort
      } finally {
        process.Dispose();
      }
    }
  }

  /// <summary>SIGKILL a raw pid (used to clean up orphans the tests deliberately created).</summary>
  public static void ForceKill(int pid) {
    try {
      kill(pid, 9);
    } catch {
      // ignore
    }
  }
}
