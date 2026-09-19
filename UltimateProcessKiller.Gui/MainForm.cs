using System.Drawing;
using Hawkynt.NativeForms;
using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// The whole GUI: a process list on the left, the strategy ladder on the right, and a running log of
/// what each attempt did. Everything is laid out at absolute bounds, the toolkit's own idiom.
/// </summary>
internal sealed class MainForm : Form {
  private readonly IProcessKiller _killer;
  private readonly Func<IReadOnlyList<ProcessSnapshot>> _source;
  private readonly bool _demo;

  private readonly ListBox _processes = new() { Bounds = new(16, 52, 520, 496), ItemHeight = 20 };
  private readonly ComboBox _strategy = new() { Bounds = new(560, 76, 344, 26), DropDownStyle = ComboBoxStyle.DropDownList };
  private readonly CheckBox _allowDestructive = new() { Bounds = new(560, 112, 344, 22), Text = "Allow destructive rungs" };
  private readonly Button _apply = new() { Bounds = new(560, 146, 344, 34), Text = "Apply strategy" };
  private readonly Button _escalate = new() { Bounds = new(560, 186, 344, 34), Text = "Escalate the ladder" };
  private readonly Button _refresh = new() { Bounds = new(16, 16, 160, 28), Text = "Refresh list" };
  private readonly ListBox _log = new() { Bounds = new(560, 262, 344, 258), ItemHeight = 18 };
  private readonly Label _status = new() { Bounds = new(16, 560, 888, 22), Text = "Ready." };

  private readonly List<ProcessSnapshot> _shown = new();
  private readonly List<TerminationMethod> _methods = new();
  private readonly List<string> _logLines = new();

  public MainForm(IProcessKiller killer, Func<IReadOnlyList<ProcessSnapshot>> source, bool demo) {
    this._killer = killer;
    this._source = source;
    this._demo = demo;

    this.Text = "UltimateProcessKiller";
    this.Bounds = new(Point.Empty, new Size(920, 600));

    this._processes.DisplaySelector = static o => Format((ProcessSnapshot)o!);
    foreach (var method in killer.SupportedMethods) {
      this._methods.Add(method);
      var label = TerminationMethodInfo.CliName(method)
        + (TerminationMethodInfo.IsDestructive(method) ? "  (destructive)" : "");
      this._strategy.Items.Add(label);
    }

    if (this._methods.Count > 0)
      this._strategy.SelectedIndex = 0;

    this._refresh.Click += (_, _) => this.ReloadProcesses();
    this._apply.Click += (_, _) => this.ApplySelectedStrategy();
    this._escalate.Click += (_, _) => this.EscalateSelected();

    this.Controls.AddRange(
      this._refresh,
      Caption("Processes", 16, 34),
      this._processes,
      Caption("Strategy", 560, 58),
      this._strategy,
      this._allowDestructive,
      this._apply,
      this._escalate,
      Caption("Log", 560, 244),
      this._log,
      this._status);

    this.ReloadProcesses();

    if (demo)
      this.SeedDemoLog();
  }

  private static Label Caption(string text, int x, int y) =>
    new() { Bounds = new(x, y, 320, 18), Text = text };

  private static string Format(ProcessSnapshot p) => $"{p.Id,8}  {p.Name}";

  private void ReloadProcesses() {
    this._shown.Clear();
    this._shown.AddRange(this._source());
    this._processes.Items.Clear();
    foreach (var snapshot in this._shown)
      this._processes.Items.Add(snapshot);

    this.SetStatus($"{this._shown.Count} process(es).");
  }

  private ProcessSnapshot? Selected() {
    var index = this._processes.SelectedIndex;
    return index >= 0 && index < this._shown.Count ? this._shown[index] : null;
  }

  private TerminationMethod? SelectedMethod() {
    var index = this._strategy.SelectedIndex;
    return index >= 0 && index < this._methods.Count ? this._methods[index] : null;
  }

  private void ApplySelectedStrategy() {
    if (this.Selected() is not { } target) {
      this.SetStatus("Select a process first.");
      return;
    }

    if (this.SelectedMethod() is not { } method)
      return;

    if (TerminationMethodInfo.IsDestructive(method) && !this._allowDestructive.Checked) {
      this.SetStatus($"{TerminationMethodInfo.CliName(method)} is destructive — tick the box to use it.");
      return;
    }

    if (this._demo) {
      this.Append($"[demo] would apply {TerminationMethodInfo.CliName(method)} to {target.Name} ({target.Id})");
      return;
    }

    var result = this._killer.Terminate(target.Id, method);
    this.Append($"{target.Name} ({target.Id}): {TerminationMethodInfo.CliName(result.Method)} -> {result.Status}");
    this.SetStatus(result.Detail);
    this.ReloadProcesses();
  }

  private void EscalateSelected() {
    if (this.Selected() is not { } target) {
      this.SetStatus("Select a process first.");
      return;
    }

    if (this._demo) {
      this.Append($"[demo] would escalate the ladder against {target.Name} ({target.Id})");
      return;
    }

    var report = ProcessExecutioner.Escalate(this._killer, target.Id, new EscalationOptions {
      AllowDestructive = this._allowDestructive.Checked,
      OnResult = r => this.Append($"  {TerminationMethodInfo.CliName(r.Method)} -> {r.Status}"),
    });

    this.Append(report.Succeeded ? $"{target.Name} ({target.Id}): terminated" : $"{target.Name} ({target.Id}): survived every rung");
    this.ReloadProcesses();
  }

  private void SeedDemoLog() {
    this._status.Text = "Escalated pid 4821 (stubborn-daemon): terminated after 3 rungs.";
    this.Append("stubborn-daemon (4821): request-close -> Executed");
    this.Append("stubborn-daemon (4821): kill -> Executed");
    this.Append("stubborn-daemon (4821): suspend-terminate -> Success");
    this.Append("stubborn-daemon (4821): terminated");
    if (this._shown.Count > 0)
      this._processes.SelectedIndex = 0;
  }

  private void Append(string line) {
    this._logLines.Add(line);
    this._log.Items.Add(line);
  }

  private void SetStatus(string text) => this._status.Text = text;
}
