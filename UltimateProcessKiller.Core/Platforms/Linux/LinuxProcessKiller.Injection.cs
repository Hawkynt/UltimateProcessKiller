using System.Runtime.InteropServices;
using static UltimateProcessKiller.Platforms.Linux.LibC;

namespace UltimateProcessKiller.Platforms.Linux;

/// <summary>The ptrace-driven rungs: inject an exit syscall, and overwrite the target's memory.</summary>
internal sealed partial class LinuxProcessKiller {
  // x86_64 user_regs_struct is 27 unsigned longs; these are the fields we touch.
  private const int RegCount = 27;
  private const int IdxRax = 10;
  private const int IdxRdi = 14;
  private const int IdxOrigRax = 15;
  private const int IdxRip = 16;
  private const long SysExitGroupX64 = 231;
  private const ushort SyscallInsn = 0x050F;   // little-endian bytes 0F 05 = `syscall`

  /// <summary>
  /// Make the process exit from inside itself: attach, rewrite the current instruction to a
  /// <c>syscall</c>, point the registers at <c>exit_group(0)</c>, and let it run. x86_64 only.
  /// </summary>
  private TerminationResult InjectExit(int pid) {
    if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
      return new(TerminationMethod.InjectExit, TerminationStatus.NotSupported, "injection implemented for x86_64 only");

    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0)
      return FromErrno(TerminationMethod.InjectExit, "ptrace(ATTACH)");

    var regs = Marshal.AllocHGlobal(RegCount * sizeof(long));
    try {
      waitpid(pid, out _, 0);

      if (ptrace(PTRACE_GETREGS, pid, 0, regs) != 0)
        return this.DetachWithFailure(TerminationMethod.InjectExit, pid, "ptrace(GETREGS)");

      var rip = (nint)ReadReg(regs, IdxRip);

      // Overwrite the instruction at RIP with `syscall`, keeping the rest of the word intact.
      var original = ptrace(PTRACE_PEEKTEXT, pid, rip, 0);
      var patched = (long)(((ulong)original & ~0xFFFFUL) | SyscallInsn);
      if (ptrace(PTRACE_POKETEXT, pid, rip, (nint)patched) != 0)
        return this.DetachWithFailure(TerminationMethod.InjectExit, pid, "ptrace(POKETEXT)");

      WriteReg(regs, IdxRax, (ulong)SysExitGroupX64);
      WriteReg(regs, IdxOrigRax, (ulong)SysExitGroupX64);
      WriteReg(regs, IdxRdi, 0);                 // exit code 0
      if (ptrace(PTRACE_SETREGS, pid, 0, regs) != 0)
        return this.DetachWithFailure(TerminationMethod.InjectExit, pid, "ptrace(SETREGS)");

      // Let it run the injected syscall; it exits before executing anything else.
      ptrace(PTRACE_CONT, pid, 0, 0);
      waitpid(pid, out _, 0);
      return this.VerifyGone(TerminationMethod.InjectExit, pid, "injected exit_group(0) at RIP");
    } finally {
      Marshal.FreeHGlobal(regs);
    }
  }

  /// <summary>
  /// Attach, zero every writable private region via <c>/proc/&lt;pid&gt;/mem</c>, then SIGKILL. The
  /// nuclear option: the process cannot survive its own data being erased, and this guarantees it.
  /// </summary>
  private TerminationResult OverwriteMemory(int pid) {
    if (ptrace(PTRACE_ATTACH, pid, 0, 0) != 0)
      return FromErrno(TerminationMethod.OverwriteMemory, "ptrace(ATTACH)");

    long zeroed = 0;
    try {
      waitpid(pid, out _, 0);
      var regions = Proc.WritableRegions(pid);

      using (var mem = new FileStream($"/proc/{pid}/mem", FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {
        var buffer = new byte[64 * 1024];
        foreach (var (start, end) in regions) {
          var remaining = (long)(end - start);
          var position = (long)start;
          while (remaining > 0) {
            var chunk = (int)Math.Min(remaining, buffer.Length);
            try {
              mem.Seek(position, SeekOrigin.Begin);
              mem.Write(buffer, 0, chunk);
              zeroed += chunk;
            } catch {
              break; // some regions refuse writes even under ptrace; move on
            }

            position += chunk;
            remaining -= chunk;
          }
        }
      }
    } catch (Exception e) {
      return this.DetachWithFailure(TerminationMethod.OverwriteMemory, pid, $"overwrite failed: {e.Message}");
    }

    // Whatever survived, finish the job.
    kill(pid, SIGKILL);
    ptrace(PTRACE_CONT, pid, 0, SIGKILL);
    ptrace(PTRACE_DETACH, pid, 0, 0);
    return this.VerifyGone(TerminationMethod.OverwriteMemory, pid, $"zeroed ~{zeroed / 1024} KiB then SIGKILL");
  }

  private TerminationResult DetachWithFailure(TerminationMethod method, int pid, string detail) {
    var errno = Marshal.GetLastPInvokeError();
    ptrace(PTRACE_DETACH, pid, 0, 0);
    return new(method, TerminationStatus.Failed, $"{detail}: errno {errno}");
  }

  private static unsafe ulong ReadReg(nint regs, int index) => ((ulong*)regs)[index];
  private static unsafe void WriteReg(nint regs, int index, ulong value) => ((ulong*)regs)[index] = value;
}
