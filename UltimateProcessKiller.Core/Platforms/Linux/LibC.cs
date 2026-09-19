using System.Runtime.InteropServices;

namespace UltimateProcessKiller.Platforms.Linux;

/// <summary>Thin bindings to the libc / kernel calls the Linux killer needs.</summary>
internal static partial class LibC {
  // Signals.
  public const int SIGTERM = 15;
  public const int SIGKILL = 9;
  public const int SIGSTOP = 19;
  public const int SIGCONT = 18;
  public const int SIGCHLD = 17;

  // errno values we care about.
  public const int ESRCH = 3;  // no such process
  public const int EPERM = 1;  // exists, but not permitted
  public const int ECHILD = 10; // not our child

  // ptrace requests (x86 numbering; the values are shared across the arches we target).
  public const int PTRACE_PEEKTEXT = 1;
  public const int PTRACE_POKETEXT = 4;
  public const int PTRACE_CONT = 7;
  public const int PTRACE_KILL = 8;
  public const int PTRACE_GETREGS = 12;
  public const int PTRACE_SETREGS = 13;
  public const int PTRACE_ATTACH = 16;
  public const int PTRACE_DETACH = 17;

  // waitpid options.
  public const int WNOHANG = 1;

  [LibraryImport("libc", SetLastError = true)]
  public static partial int kill(int pid, int sig);

  [LibraryImport("libc", SetLastError = true)]
  public static partial int waitpid(int pid, out int status, int options);

  [LibraryImport("libc", SetLastError = true)]
  public static partial long ptrace(int request, int pid, nint addr, nint data);

  [LibraryImport("libc", SetLastError = true)]
  public static partial long syscall(long number, long a1, long a2, long a3);

  /// <summary>Send <paramref name="sig"/> to a single thread (tgkill(2)), for TerminateThreads.</summary>
  public static int TgKill(int tgid, int tid, int sig) {
    var number = SysTgkill();
    if (number < 0)
      return -1;

    return (int)syscall(number, tgid, tid, sig);
  }

  // tgkill syscall number by architecture.
  private static long SysTgkill() => RuntimeInformation.ProcessArchitecture switch {
    Architecture.X64 => 234,
    Architecture.Arm64 => 131,
    Architecture.Arm => 268,
    Architecture.X86 => 270,
    _ => -1,
  };
}
