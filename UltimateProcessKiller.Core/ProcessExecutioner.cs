namespace UltimateProcessKiller;

/// <summary>Options that shape an escalation run.</summary>
public sealed class EscalationOptions {
  /// <summary>Include destructive methods (handle closing, memory overwrite, thread termination).</summary>
  public bool AllowDestructive { get; init; }

  /// <summary>Restrict escalation to this subset (still walked in escalation order). Null = all supported.</summary>
  public IReadOnlyList<TerminationMethod>? Only { get; init; }

  /// <summary>Called before each method is tried, for progress reporting.</summary>
  public Action<TerminationMethod>? OnTry { get; init; }

  /// <summary>Called after each method, with its result.</summary>
  public Action<TerminationResult>? OnResult { get; init; }
}

/// <summary>The record of an escalation: every rung tried, and whether the target is gone.</summary>
public sealed class EscalationReport {
  public required int ProcessId { get; init; }
  public required IReadOnlyList<TerminationResult> Attempts { get; init; }

  /// <summary>True when the process is confirmed gone by the time escalation stopped.</summary>
  public required bool Succeeded { get; init; }
}

/// <summary>Walks the termination ladder for a process, stopping at the first rung that works.</summary>
public static class ProcessExecutioner {
  /// <summary>
  /// Try each supported method in escalation order until the process is gone. Destructive rungs are
  /// skipped unless <see cref="EscalationOptions.AllowDestructive"/> is set.
  /// </summary>
  public static EscalationReport Escalate(IProcessKiller killer, int processId, EscalationOptions? options = null) {
    options ??= new EscalationOptions();
    var attempts = new List<TerminationResult>();

    if (!killer.IsAlive(processId) && !killer.IsZombie(processId))
      return new EscalationReport { ProcessId = processId, Attempts = attempts, Succeeded = true };

    foreach (var method in Ladder(killer, options)) {
      options.OnTry?.Invoke(method);
      var result = killer.Terminate(processId, method);
      attempts.Add(result);
      options.OnResult?.Invoke(result);

      if (result.ProcessGone)
        return new EscalationReport { ProcessId = processId, Attempts = attempts, Succeeded = true };
    }

    var gone = !killer.IsAlive(processId) && !killer.IsZombie(processId);
    return new EscalationReport { ProcessId = processId, Attempts = attempts, Succeeded = gone };
  }

  private static IEnumerable<TerminationMethod> Ladder(IProcessKiller killer, EscalationOptions options) {
    foreach (var method in killer.SupportedMethods) {
      if (options.Only is { } only && !only.Contains(method))
        continue;

      // ReapZombie is targeted, not a general rung; it is only reached when it is the explicit subset.
      if (method == TerminationMethod.ReapZombie && options.Only is null)
        continue;

      if (TerminationMethodInfo.IsDestructive(method) && !options.AllowDestructive)
        continue;

      yield return method;
    }
  }
}
