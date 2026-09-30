using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// The whole GUI: a process list on the left, the strategy ladder on the right, and a running log of
/// what each attempt did. Controls are grouped in labelled boxes and laid out at absolute bounds, the
/// toolkit's own idiom.
/// </summary>
internal sealed class MainForm : Form {
  private const int Width_ = 940;
  private const int Height_ = 620;

  private readonly IProcessKiller _killer;
  private readonly Func<IReadOnlyList<ProcessSnapshot>> _source;
  private readonly bool _demo;
  private readonly string _scene;
  private readonly IPlatformBackend _backend = BackendRegistry.Resolve();

  private readonly ListBox _processes = new() { Bounds = new(14, 24, 486, 476), ItemHeight = 20 };
  private readonly Button _refresh = new() { Bounds = new(14, 508, 130, 32), Text = "Refresh" };
  private readonly Label _count = new() { Bounds = new(156, 514, 336, 20), Text = "" };

  private readonly ComboBox _strategy = new() { Bounds = new(16, 50, 350, 34), DropDownStyle = ComboBoxStyle.DropDownList };
  private readonly CheckBox _allowDestructive = new() { Bounds = new(16, 90, 350, 24), Text = "Allow destructive rungs" };
  private readonly Button _apply = new() { Bounds = new(16, 124, 172, 40), Text = "Apply strategy" };
  private readonly Button _escalate = new() { Bounds = new(196, 124, 172, 40), Text = "Escalate ladder" };
  private readonly Label _hint = new() { Bounds = new(16, 176, 352, 56), Text = "" };

  private readonly ListBox _log = new() { Bounds = new(16, 24, 352, 258), ItemHeight = 18 };
  private readonly Label _status = new() { Bounds = new(20, 582, 900, 22), Text = "Ready.", BorderStyle = BorderStyle.None };

  private readonly List<ProcessSnapshot> _shown = new();
  private readonly List<TerminationMethod> _methods = new();

  public MainForm(IProcessKiller killer, Func<IReadOnlyList<ProcessSnapshot>> source, bool demo, string scene = "overview") {
    this._killer = killer;
    this._source = source;
    this._demo = demo;
    this._scene = scene;

    this.Text = "UltimateProcessKiller";
    this.Bounds = new(Point.Empty, new Size(Width_, Height_));
    this.MinimumSize = new(Width_, Height_);
    this.SetIcon(16, 16, DiscPixels(16, Color.Crimson));

    this._processes.DisplaySelector = static o => Format((ProcessSnapshot)o!);
    foreach (var method in killer.SupportedMethods) {
      this._methods.Add(method);
      var label = TerminationMethodInfo.CliName(method)
        + (TerminationMethodInfo.IsDestructive(method) ? "  — destructive" : "");
      this._strategy.Items.Add(label);
    }

    if (this._methods.Count > 0)
      this._strategy.SelectedIndex = 0;

    this._refresh.Image = this.Swatch(Color.SteelBlue);
    this._apply.Image = this.Swatch(Color.SeaGreen);
    this._escalate.Image = this.Swatch(Color.DarkOrange);

    this._refresh.Click += (_, _) => this.ReloadProcesses();
    this._apply.Click += (_, _) => this.ApplySelectedStrategy();
    this._escalate.Click += (_, _) => this.EscalateSelected();
    this._strategy.SelectedIndexChanged += (_, _) => this.UpdateHint();

    var processesBox = new GroupBox { Bounds = new(16, 12, 504, 556), Text = "Processes" };
    processesBox.Controls.AddRange(this._processes, this._refresh, this._count);

    var actionBox = new GroupBox { Bounds = new(532, 12, 392, 250), Text = "Strategy" };
    actionBox.Controls.AddRange(
      new Label { Bounds = new(16, 26, 352, 18), Text = "Choose how hard to try:" },
      this._strategy,
      this._allowDestructive,
      this._apply,
      this._escalate,
      this._hint);

    var logBox = new GroupBox { Bounds = new(532, 274, 392, 294), Text = "Log" };
    logBox.Controls.Add(this._log);

    this.Controls.AddRange(processesBox, actionBox, logBox, this._status);

    this.ReloadProcesses();
    this.UpdateHint();

    if (demo)
      this.SeedDemoScene();
  }

  private static string Format(ProcessSnapshot p) => $"{p.Id,7}   {p.Name}";

  private void ReloadProcesses() {
    this._shown.Clear();
    this._shown.AddRange(this._source());
    this._processes.Items.Clear();
    foreach (var snapshot in this._shown)
      this._processes.Items.Add(snapshot);

    this._count.Text = $"{this._shown.Count} process(es)";
  }

  private ProcessSnapshot? Selected() {
    var index = this._processes.SelectedIndex;
    return index >= 0 && index < this._shown.Count ? this._shown[index] : null;
  }

  private TerminationMethod? SelectedMethod() {
    var index = this._strategy.SelectedIndex;
    return index >= 0 && index < this._methods.Count ? this._methods[index] : null;
  }

  private void UpdateHint() {
    if (this.SelectedMethod() is { } method)
      this._hint.Text = TerminationMethodInfo.Describe(method)
        + (TerminationMethodInfo.IsDestructive(method) ? "\nDestructive: tick the box above to allow it." : "");
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
    this.Append($"{target.Name} ({target.Id}): {TerminationMethodInfo.CliName(result.Method)} → {result.Status}");
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
      OnResult = r => this.Append($"  {TerminationMethodInfo.CliName(r.Method)} → {r.Status}"),
    });

    this.Append(report.Succeeded ? $"{target.Name} ({target.Id}): terminated" : $"{target.Name} ({target.Id}): survived every rung");
    this.ReloadProcesses();
  }

  private void SeedDemoScene() {
    if (this._shown.Count > 0)
      this._processes.SelectedIndex = 0;

    if (this._scene == "destructive") {
      this._allowDestructive.Checked = true;
      this.SelectMethod(TerminationMethod.OverwriteMemory);
      this.UpdateHint();
      this.SetStatus("overwrite-memory zeroed ~184 MiB of runaway-worker, then terminated it.");
      this.Append("runaway-worker (2048): request-close → Executed");
      this.Append("runaway-worker (2048): kill → Executed");
      this.Append("runaway-worker (2048): suspend-terminate → Executed");
      this.Append("runaway-worker (2048): native-terminate → Executed");
      this.Append("runaway-worker (2048): overwrite-memory → Success");
      this.Append("runaway-worker (2048): terminated");
      return;
    }

    this.SetStatus("Escalated stubborn-daemon (4821): terminated after 3 rungs.");
    this.Append("stubborn-daemon (4821): request-close → Executed");
    this.Append("stubborn-daemon (4821): kill → Executed");
    this.Append("stubborn-daemon (4821): suspend-terminate → Success");
    this.Append("stubborn-daemon (4821): terminated");
  }

  private void SelectMethod(TerminationMethod method) {
    var index = this._methods.IndexOf(method);
    if (index >= 0)
      this._strategy.SelectedIndex = index;
  }

  private void Append(string line) => this._log.Items.Add(line);

  private void SetStatus(string text) => this._status.Text = text;

  // --- little generated icons, so the app ships no image files -----------------------------------

  private IImage Swatch(Color color) => this._backend.CreateImage(12, 12, SquarePixels(12, color));

  private static int[] SquarePixels(int size, Color color) {
    var argb = color.ToArgb();
    var pixels = new int[size * size];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = argb;

    return pixels;
  }

  private static int[] DiscPixels(int size, Color color) {
    var argb = color.ToArgb();
    var pixels = new int[size * size];
    var r = size / 2.0;
    for (var y = 0; y < size; ++y)
      for (var x = 0; x < size; ++x) {
        var dx = x + 0.5 - r;
        var dy = y + 0.5 - r;
        pixels[y * size + x] = dx * dx + dy * dy <= r * r ? argb : 0;
      }

    return pixels;
  }
}
