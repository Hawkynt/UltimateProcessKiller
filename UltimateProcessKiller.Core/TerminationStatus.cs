namespace UltimateProcessKiller;

/// <summary>The outcome of applying a single <see cref="TerminationMethod"/>.</summary>
public enum TerminationStatus {
  /// <summary>The method ran and the process is confirmed gone.</summary>
  Success,

  /// <summary>The method ran without error, but the process is still present or its exit could not be confirmed.</summary>
  Executed,

  /// <summary>No process with that id exists (any more).</summary>
  NotFound,

  /// <summary>The current token lacks the privilege to touch the target.</summary>
  AccessDenied,

  /// <summary>The method is not available on this platform.</summary>
  NotSupported,

  /// <summary>The method ran but failed.</summary>
  Failed,
}
