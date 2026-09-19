namespace UltimateProcessKiller;

/// <summary>The result of applying one <see cref="TerminationMethod"/> to one process.</summary>
public readonly record struct TerminationResult(
  TerminationMethod Method,
  TerminationStatus Status,
  string Detail
) {
  /// <summary>True when the method ran and (as far as could be verified) achieved its effect.</summary>
  public bool Ok => this.Status is TerminationStatus.Success;

  /// <summary>True when the target is confirmed gone or was already gone.</summary>
  public bool ProcessGone => this.Status is TerminationStatus.Success or TerminationStatus.NotFound;

  public static TerminationResult NotSupported(TerminationMethod method) =>
    new(method, TerminationStatus.NotSupported, "not available on this platform");

  public static TerminationResult NotFound(TerminationMethod method) =>
    new(method, TerminationStatus.NotFound, "no such process");

  public override string ToString() => $"{this.Method}: {this.Status} ({this.Detail})";
}
