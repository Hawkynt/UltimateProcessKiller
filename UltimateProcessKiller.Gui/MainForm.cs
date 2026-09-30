using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// The whole GUI: a process list on the left, the strategy ladder on the right, and a running log of
/// what each attempt did. Controls are grouped in labelled boxes under a small header, and laid out at
/// absolute bounds — the toolkit's own idiom. Real terminations run off the UI thread so the window
/// stays responsive (and closable) while a stubborn process is being worked.
/// </summary>
internal sealed class MainForm : Form {
  private const int Width_ = 940;
  private const int Height_ = 700;

  private static readonly Color[] Palette = {
    Color.RoyalBlue, Color.SeaGreen, Color.DarkOrange, Color.MediumOrchid,
    Color.Crimson, Color.Teal, Color.Goldenrod, Color.SteelBlue,
  };

  private readonly IProcessKiller _killer;
  private readonly Func<IReadOnlyList<ProcessSnapshot>> _source;
  private readonly bool _demo;
  private readonly string _scene;
  private readonly IPlatformBackend _backend = BackendRegistry.Resolve();
  private readonly IImage[] _dots;

  private readonly ListBox _processes = new() { Bounds = new(14, 26, 476, 506), ItemHeight = 20 };
  private readonly Button _refresh = new() { Bounds = new(14, 548, 140, 32), Text = "Refresh" };
  private readonly Label _count = new() { Bounds = new(166, 554, 320, 20), Text = "" };

  private readonly ComboBox _strategy = new() { Bounds = new(16, 52, 350, 34), DropDownStyle = ComboBoxStyle.DropDownList };
  private readonly CheckBox _allowDestructive = new() { Bounds = new(16, 92, 352, 24), Text = "Allow destructive rungs" };
  private readonly Button _apply = new() { Bounds = new(16, 126, 172, 40), Text = "Apply strategy" };
  private readonly Button _escalate = new() { Bounds = new(196, 126, 172, 40), Text = "Escalate ladder" };
  private readonly Label _hint = new() { Bounds = new(16, 178, 352, 56), Text = "" };

  private readonly ListBox _log = new() { Bounds = new(16, 24, 360, 290), ItemHeight = 18 };
  private readonly Label _status = new() { Bounds = new(20, 668, 900, 22), Text = "Ready." };

  private readonly List<ProcessSnapshot> _shown = new();
  private readonly List<TerminationMethod> _methods = new();
  private bool _busy;

  public MainForm(IProcessKiller killer, Func<IReadOnlyList<ProcessSnapshot>> source, bool demo, string scene = "overview") {
    this._killer = killer;
    this._source = source;
    this._demo = demo;
    this._scene = scene;

    this.Text = "UltimateProcessKiller";
    this.Bounds = new(Point.Empty, new Size(Width_, Height_));
    this.MinimumSize = new(Width_, Height_);
    this.SetIcon(16, 16, Icons.Target(16, Color.Crimson));

    this._dots = new IImage[Palette.Length];
    for (var i = 0; i < Palette.Length; ++i)
      this._dots[i] = this._backend.CreateImage(14, 14, Icons.Dot(14, Palette[i]));

    this._processes.DisplaySelector = static o => Format((ProcessSnapshot)o!);
    this._processes.ImageSelector = o => this._dots[ColorIndex(((ProcessSnapshot)o!).Name)];

    foreach (var method in killer.SupportedMethods) {
      this._methods.Add(method);
      var label = TerminationMethodInfo.CliName(method)
        + (TerminationMethodInfo.IsDestructive(method) ? "  — destructive" : "");
      this._strategy.Items.Add(label);
    }

    if (this._methods.Count > 0)
      this._strategy.SelectedIndex = 0;

    this._refresh.Image = this.Icon(Icons.Refresh(16, Color.SteelBlue));
    this._apply.Image = this.Icon(Icons.Play(16, Color.SeaGreen));
    this._escalate.Image = this.Icon(Icons.ArrowUp(16, Color.DarkOrange));

    this._refresh.Click += (_, _) => this.ReloadProcesses();
    this._apply.Click += (_, _) => this.ApplySelectedStrategy();
    this._escalate.Click += (_, _) => this.EscalateSelected();
    this._strategy.SelectedIndexChanged += (_, _) => this.UpdateHint();

    var header = new Label {
      Bounds = new(58, 14, 700, 28),
      Text = "UltimateProcessKiller",
      Font = new Font("Segoe UI", 15f, FontStyle.Bold),
      ForeColor = Color.FromArgb(30, 34, 42),
    };
    var subtitle = new Label {
      Bounds = new(60, 44, 800, 18),
      Text = "End the process everything else gave up on.",
      ForeColor = Color.FromArgb(96, 102, 112),
    };
    var logo = new Label { Bounds = new(16, 14, 34, 34), Image = this.Icon(Icons.Target(28, Color.Crimson)) };

    var processesBox = new GroupBox { Bounds = new(16, 72, 504, 588), Text = "Processes" };
    processesBox.Controls.AddRange(this._processes, this._refresh, this._count);

    var actionBox = new GroupBox { Bounds = new(532, 72, 392, 250), Text = "Strategy" };
    actionBox.Controls.AddRange(
      new Label { Bounds = new(16, 28, 352, 18), Text = "Choose how hard to try:" },
      this._strategy,
      this._allowDestructive,
      this._apply,
      this._escalate,
      this._hint);

    var logBox = new GroupBox { Bounds = new(532, 334, 392, 326), Text = "Log" };
    logBox.Controls.Add(this._log);

    this.Controls.AddRange(logo, header, subtitle, processesBox, actionBox, logBox, this._status);

    this.ReloadProcesses();
    this.UpdateHint();

    if (demo)
      this.SeedDemoScene();
  }

  private IImage Icon(int[] pixels) {
    var side = (int)Math.Round(Math.Sqrt(pixels.Length));
    return this._backend.CreateImage(side, side, pixels);
  }

  private static string Format(ProcessSnapshot p) => $"{p.Id,7}   {p.Name}";

  private static int ColorIndex(string name) {
    var hash = 0;
    foreach (var c in name)
      hash = hash * 31 + c;

    return (hash & 0x7fffffff) % Palette.Length;
  }

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

  private void SetBusy(bool busy) {
    this._busy = busy;
    this._apply.Enabled = !busy;
    this._escalate.Enabled = !busy;
    this._refresh.Enabled = !busy;
  }

  private void ApplySelectedStrategy() {
    if (this._busy)
      return;

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

    this.SetBusy(true);
    this.SetStatus($"Applying {TerminationMethodInfo.CliName(method)} to {target.Name} ({target.Id})…");
    Task.Run(() => {
      var result = this._killer.Terminate(target.Id, method);
      this.BeginInvoke(() => {
        this.Append($"{target.Name} ({target.Id}): {TerminationMethodInfo.CliName(result.Method)} → {result.Status}");
        this.SetStatus(result.Detail);
        this.ReloadProcesses();
        this.SetBusy(false);
      });
    });
  }

  private void EscalateSelected() {
    if (this._busy)
      return;

    if (this.Selected() is not { } target) {
      this.SetStatus("Select a process first.");
      return;
    }

    if (this._demo) {
      this.Append($"[demo] would escalate the ladder against {target.Name} ({target.Id})");
      return;
    }

    var allowDestructive = this._allowDestructive.Checked;
    this.SetBusy(true);
    this.SetStatus($"Escalating the ladder against {target.Name} ({target.Id})…");
    Task.Run(() => {
      var report = ProcessExecutioner.Escalate(this._killer, target.Id, new EscalationOptions {
        AllowDestructive = allowDestructive,
        OnResult = r => this.BeginInvoke(() => this.Append($"  {TerminationMethodInfo.CliName(r.Method)} → {r.Status}")),
      });

      this.BeginInvoke(() => {
        this.Append(report.Succeeded ? $"{target.Name} ({target.Id}): terminated" : $"{target.Name} ({target.Id}): survived every rung");
        this.SetStatus(report.Succeeded ? $"{target.Name} terminated." : $"{target.Name} survived every rung.");
        this.ReloadProcesses();
        this.SetBusy(false);
      });
    });
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
}
