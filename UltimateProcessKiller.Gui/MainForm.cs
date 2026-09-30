using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// The whole GUI: a process tree on the left, the strategy ladder on the right, and a running log of
/// what each attempt did. Controls are grouped in labelled boxes under a small header, and laid out at
/// absolute bounds — the toolkit's own idiom. Real terminations run off the UI thread so the window
/// stays responsive (and closable) while a stubborn process is being worked.
/// </summary>
internal sealed class MainForm : Form {
  private const int Width_ = 960;
  private const int Height_ = 720;

  // Per-process bullet colours (indexed by a stable hash of the name).
  private static readonly Color[] Palette = {
    Color.RoyalBlue, Color.SeaGreen, Color.DarkOrange, Color.MediumOrchid,
    Color.Crimson, Color.Teal, Color.Goldenrod, Color.SteelBlue,
  };

  // Strategy categories, by how invasive they are — the colour of the combo-box bullet.
  private static readonly Color[] CategoryColors = {
    Color.SeaGreen,   // 0 gentle
    Color.RoyalBlue,  // 1 standard
    Color.DarkOrange, // 2 aggressive
    Color.Crimson,    // 3 destructive
    Color.MediumOrchid, // 4 special (reap)
  };

  private readonly IProcessKiller _killer;
  private readonly Func<IReadOnlyList<ProcessSnapshot>> _source;
  private readonly bool _demo;
  private readonly string _scene;
  private readonly IPlatformBackend _backend = BackendRegistry.Resolve();

  private readonly ImageList _processIcons = new(16);
  private readonly ImageList _methodIcons = new(16);

  private readonly TreeListView _tree = new() { Bounds = new(12, 24, 496, 520), ItemHeight = 22 };
  private readonly Button _refresh = new() { Bounds = new(12, 552, 150, 32), Text = "Refresh" };
  private readonly Label _count = new() { Bounds = new(172, 558, 330, 20), Text = "" };

  private readonly ComboBox _strategy = new() { Bounds = new(16, 50, 364, 34), DropDownStyle = ComboBoxStyle.DropDownList };
  private readonly CheckBox _allowDestructive = new() { Bounds = new(16, 92, 364, 24), Text = "Allow destructive rungs" };
  private readonly Button _apply = new() { Bounds = new(16, 124, 178, 40), Text = "Apply strategy" };
  private readonly Button _escalate = new() { Bounds = new(202, 124, 178, 40), Text = "Escalate ladder" };
  private readonly Label _desc = new() { Bounds = new(16, 176, 364, 104), Text = "", BorderStyle = BorderStyle.FixedSingle };

  private readonly ListBox _log = new() { Bounds = new(16, 24, 364, 252), ItemHeight = 18 };
  private readonly Label _status = new() { Bounds = new(20, 684, 920, 22), Text = "Ready." };

  private List<ProcessSnapshot> _shown = new();
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

    foreach (var color in Palette)
      this._processIcons.Add(Icons.Dot(16, color));
    foreach (var color in CategoryColors)
      this._methodIcons.Add(Icons.Dot(16, color));

    this._tree.ImageList = this._processIcons;
    this._tree.Columns.AddRange(new[] {
      new TreeListViewColumn("Process", 300),
      new TreeListViewColumn("PID", 84, static node => node.Tag is ProcessSnapshot s ? s.Id.ToString() : ""),
    });
    this._tree.AfterSelect += (_, _) => this.UpdateDescription();

    this._strategy.ImageList = this._methodIcons;
    this._strategy.DisplaySelector = static o => StrategyLabel((TerminationMethod)o!);
    this._strategy.ImageIndexSelector = static o => CategoryIndex((TerminationMethod)o!);
    foreach (var method in killer.SupportedMethods)
      this._strategy.Items.Add(method);

    if (this._strategy.Items.Count > 0)
      this._strategy.SelectedIndex = 0;

    this._refresh.Image = this.Icon(Icons.Refresh(16, Color.SteelBlue));
    this._apply.Image = this.Icon(Icons.Play(16, Color.SeaGreen));
    this._escalate.Image = this.Icon(Icons.ArrowUp(16, Color.DarkOrange));

    this._refresh.Click += (_, _) => this.RebuildTree();
    this._apply.Click += (_, _) => this.ApplySelectedStrategy();
    this._escalate.Click += (_, _) => this.EscalateSelected();
    this._strategy.SelectedIndexChanged += (_, _) => this.UpdateDescription();

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

    var processesBox = new GroupBox { Bounds = new(16, 72, 520, 600), Text = "Processes" };
    processesBox.Controls.AddRange(this._tree, this._refresh, this._count);

    var actionBox = new GroupBox { Bounds = new(548, 72, 396, 300), Text = "Strategy" };
    actionBox.Controls.AddRange(
      new Label { Bounds = new(16, 28, 360, 18), Text = "Choose how hard to try:" },
      this._strategy,
      this._allowDestructive,
      this._apply,
      this._escalate,
      this._desc);

    var logBox = new GroupBox { Bounds = new(548, 384, 396, 288), Text = "Log" };
    logBox.Controls.Add(this._log);

    this.Controls.AddRange(logo, header, subtitle, processesBox, actionBox, logBox, this._status);

    this.RebuildTree();
    this.UpdateDescription();

    if (demo)
      this.SeedDemoScene();
  }

  private IImage Icon(int[] pixels) {
    var side = (int)Math.Round(Math.Sqrt(pixels.Length));
    return this._backend.CreateImage(side, side, pixels);
  }

  private static string StrategyLabel(TerminationMethod method)
    => TerminationMethodInfo.CliName(method) + (TerminationMethodInfo.IsDestructive(method) ? "  — destructive" : "");

  private static int CategoryIndex(TerminationMethod method) => method switch {
    TerminationMethod.RequestClose => 0,
    TerminationMethod.Kill or TerminationMethod.KillTree => 1,
    TerminationMethod.SuspendAndTerminate or TerminationMethod.AttachDebugger
      or TerminationMethod.NativeTerminate or TerminationMethod.InjectExit => 2,
    TerminationMethod.TerminateThreads or TerminationMethod.CloseHandles or TerminationMethod.OverwriteMemory => 3,
    TerminationMethod.ReapZombie => 4,
    _ => 1,
  };

  private static int ColorIndex(string name) {
    var hash = 0;
    foreach (var c in name)
      hash = hash * 31 + c;

    return (hash & 0x7fffffff) % Palette.Length;
  }

  /// <summary>Rebuild the process tree from the current snapshot, wiring children under their parents.</summary>
  private void RebuildTree() {
    this._shown = this._source().ToList();
    this._tree.Nodes.Clear();

    var nodes = new Dictionary<int, TreeNode>();
    foreach (var snapshot in this._shown)
      nodes[snapshot.Id] = new TreeNode(snapshot.Name) { Tag = snapshot, ImageIndex = ColorIndex(snapshot.Name) };

    var roots = new List<TreeNode>();
    foreach (var snapshot in this._shown) {
      var node = nodes[snapshot.Id];
      if (snapshot.ParentId != 0 && snapshot.ParentId != snapshot.Id && nodes.TryGetValue(snapshot.ParentId, out var parent))
        parent.Nodes.Add(node);
      else
        roots.Add(node);
    }

    foreach (var root in roots) {
      this._tree.Nodes.Add(root);
      if (this._demo)
        root.ExpandAll();
      else
        root.Expand();
    }

    this._count.Text = $"{this._shown.Count} process(es)";
  }

  private ProcessSnapshot? Selected() => this._tree.SelectedNode?.Tag is ProcessSnapshot s ? s : null;

  private TerminationMethod? SelectedMethod() => this._strategy.SelectedItem is TerminationMethod m ? m : null;

  private void UpdateDescription() {
    if (this.SelectedMethod() is not { } method) {
      this._desc.Text = "";
      return;
    }

    var destructive = TerminationMethodInfo.IsDestructive(method);
    var target = this.Selected() is { } s ? $"Target: {s.Name} ({s.Id})" : "Target: (select a process)";
    this._desc.Text =
      $"{TerminationMethodInfo.CliName(method)}\n" +
      $"{TerminationMethodInfo.Describe(method)}\n" +
      (destructive
        ? "⚠ Destructive — can corrupt files; tick the box above to allow."
        : "Reasonably safe to try before the harsher rungs.") +
      $"\n{target}";
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
        this.RebuildTree();
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
        this.RebuildTree();
        this.SetBusy(false);
      });
    });
  }

  private void SeedDemoScene() {
    this.SelectNode(4821);

    if (this._scene == "destructive") {
      this._allowDestructive.Checked = true;
      this.SelectStrategy(TerminationMethod.OverwriteMemory);
      this.SelectNode(2048);
      this.UpdateDescription();
      this.SetStatus("overwrite-memory zeroed ~184 MiB of runaway-worker, then terminated it.");
      this.Append("runaway-worker (2048): request-close → Executed");
      this.Append("runaway-worker (2048): kill → Executed");
      this.Append("runaway-worker (2048): suspend-terminate → Executed");
      this.Append("runaway-worker (2048): native-terminate → Executed");
      this.Append("runaway-worker (2048): overwrite-memory → Success");
      this.Append("runaway-worker (2048): terminated");
      return;
    }

    this.UpdateDescription();
    this.SetStatus("Escalated stubborn-daemon (4821): terminated after 3 rungs.");
    this.Append("stubborn-daemon (4821): request-close → Executed");
    this.Append("stubborn-daemon (4821): kill → Executed");
    this.Append("stubborn-daemon (4821): suspend-terminate → Success");
    this.Append("stubborn-daemon (4821): terminated");
  }

  private void SelectStrategy(TerminationMethod method) {
    for (var i = 0; i < this._strategy.Items.Count; ++i)
      if (this._strategy.Items[i] is TerminationMethod m && m == method) {
        this._strategy.SelectedIndex = i;
        return;
      }
  }

  private void SelectNode(int pid) {
    var node = FindNode(this._tree.Nodes, pid);
    if (node is not null)
      this._tree.SelectedNode = node;
  }

  private static TreeNode? FindNode(TreeNodeCollection nodes, int pid) {
    foreach (TreeNode node in nodes) {
      if (node.Tag is ProcessSnapshot s && s.Id == pid)
        return node;

      if (FindNode(node.Nodes, pid) is { } found)
        return found;
    }

    return null;
  }

  private void Append(string line) => this._log.Items.Add(line);

  private void SetStatus(string text) => this._status.Text = text;
}
