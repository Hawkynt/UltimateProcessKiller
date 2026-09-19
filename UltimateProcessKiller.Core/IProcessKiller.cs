namespace UltimateProcessKiller;

/// <summary>A platform's implementation of the termination strategies it can honestly offer.</summary>
public interface IProcessKiller {
  /// <summary>The methods this platform supports, in escalation order.</summary>
  IReadOnlyList<TerminationMethod> SupportedMethods { get; }

  /// <summary>True when <paramref name="method"/> is implemented on this platform.</summary>
  bool Supports(TerminationMethod method);

  /// <summary>Apply one method to one process.</summary>
  TerminationResult Terminate(int processId, TerminationMethod method);

  /// <summary>True when a process with that id currently exists.</summary>
  bool IsAlive(int processId);

  /// <summary>True when the process exists but is a zombie/defunct entry (Linux); false elsewhere.</summary>
  bool IsZombie(int processId);
}
