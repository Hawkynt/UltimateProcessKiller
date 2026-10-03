using System.Drawing;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Drawing;
using UltimateProcessKiller;

namespace UltimateProcessKiller.Gui;

/// <summary>
/// The whole GUI: a searchable process tree on the left, the strategy ladder on the right, and a
/// running log of what each attempt did. The window is resizable — the tree and log stretch, the
/// strategy panel keeps its width. Every action is reachable three ways: the buttons, the right-click
/// context menu on a process, or a keyboard shortcut in the menu bar. Destructive rungs ask for a
/// confirmation even when the switch allows them. Real terminations run off the UI thread so the
/// window stays responsive (and closable) while a stubborn process is being worked.
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
  private readonly ToolTip _tips = new();

  private readonly TextBox _search = new() { Bounds = new(16, 24, 336, 30), PlaceholderText = "filter by name or pid" };
  private readonly CheckBox _autoRefresh = new() { Bounds = new(364, 28, 120, 24), Text = "auto" };
  // Stretched with the Processes box on every edge; the refresh row below it is pinned to the box's
  // bottom, so a shorter window shortens the tree instead of hiding the buttons under it.
  private readonly TreeListView _tree = new() { Bounds = new(12, 62, 492, 444), ItemHeight = 22, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
  private readonly Button _refresh = new() { Bounds = new(12, 512, 130, 32), Text = "Refresh", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
  private readonly Label _count = new() { Bounds = new(152, 518, 340, 20), Text = "", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
  // The legend rides the box's bottom edge; the refresh row sits just above it. The stack is packed
  // tight enough that at the minimum window height both still fit inside the box.
  private readonly Label _legend = new() {
    Bounds = new(12, 552, 492, 16),
    Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
    Text = "● bullet colour groups processes by name — same colour, same name",
    ForeColor = Color.FromArgb(120, 126, 136),
  };

  private readonly ComboBox _strategy = new() { Bounds = new(16, 50, 364, 34), DropDownStyle = ComboBoxStyle.DropDownList };
  private readonly ToggleSwitch _allowDestructive = new() { Bounds = new(16, 92, 364, 26), Text = "Allow destructive rungs" };
  private readonly Button _apply = new() { Bounds = new(16, 126, 178, 40), Text = "Apply strategy" };
  private readonly Button _escalate = new() { Bounds = new(202, 126, 178, 40), Text = "Escalate ladder" };
  private readonly InfoBar _warnBar = new() {
    Bounds = new(16, 174, 364, 40),
    Severity = InfoBarSeverity.Warning,
    Title = "Destructive rung",
    Message = "Can corrupt files the target holds open.",
    Visible = false,
  };
  // Read-only multiline text, not a label: long strategy descriptions (overwrite-memory's warning
  // plus the target line) exceed the panel, and the toolkit's multiline text view scrolls natively.
  private readonly TextBox _desc = new() { Bounds = new(16, 222, 364, 66), Multiline = true, ReadOnly = true };

  private readonly ListBox _log = new() { Bounds = new(16, 24, 364, 236), ItemHeight = 18, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
  private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Ready.", Spring = true };
  private readonly StatusStrip _statusStrip = new();
  private readonly Hawkynt.NativeForms.Timer _ticker = new() { Interval = 2000 };

  private readonly ContextMenuStrip _treeMenu;
  private List<ProcessSnapshot> _shown = new();
  private bool _busy;
  private string _filter = "";

  public MainForm(IProcessKiller killer, Func<IReadOnlyList<ProcessSnapshot>> source, bool demo, string scene = "overview") {
    this._killer = killer;
    this._source = source;
    this._demo = demo;
    this._scene = scene;

    this.Text = "UltimateProcessKiller";
    this.Bounds = new(Point.Empty, new Size(Width_, Height_));
    this.MinimumSize = new(820, 600);
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
    this._treeMenu = this.BuildContextMenu();
    this._tree.AfterSelect += (_, _) => this.UpdateDescription();
    this._tree.MouseDoubleClick += (_, _) => this.ApplySelectedStrategy();
    // Shown manually on right-button release: the toolkit's automatic ContextMenuStrip handling eats
    // the press that dismisses an already-open menu, so re-opening on another row needs a second
    // click. Showing from MouseUp makes one right-click per row enough.
    this._tree.MouseUp += (_, e) => {
      if (e.Button == MouseButtons.Right)
        this._treeMenu.Show(this._tree, e.Location);
    };

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
    this._search.TextChanged += (_, _) => {
      this._filter = this._search.Text.Trim();
      this.RebuildTree();
    };
    this._autoRefresh.CheckedChanged += (_, _) => {
      if (this._autoRefresh.Checked)
        this._ticker.Start();
      else
        this._ticker.Stop();
    };
    this._ticker.Tick += (_, _) => {
      if (!this._busy)
        this.RebuildTree();
    };

    this._log.Font = new Font(OperatingSystem.IsWindows() ? "Consolas" : "monospace", 9f);

    this._tips.SetToolTip(this._search, "Type to narrow the tree; matches auto-expand.");
    this._tips.SetToolTip(this._refresh, "Re-list processes (F5).");
    this._tips.SetToolTip(this._apply, "Apply the selected strategy (Ctrl+Enter).");
    this._tips.SetToolTip(this._escalate, "Walk the whole ladder, gentlest first (Ctrl+E).");
    this._tips.SetToolTip(this._allowDestructive, "Permit rungs that can corrupt the target (Ctrl+D).");

    var header = new Label {
      Bounds = new(58, 34, 700, 28),
      Text = "UltimateProcessKiller",
      Font = new Font("Segoe UI", 15f, FontStyle.Bold),
      ForeColor = Color.FromArgb(30, 34, 42),
    };
    var subtitle = new Label {
      Bounds = new(60, 64, 800, 18),
      Text = "End the process everything else gave up on.",
      ForeColor = Color.FromArgb(96, 102, 112),
    };
    var logo = new Label { Bounds = new(16, 34, 34, 34), Image = this.Icon(Icons.Target(28, Color.Crimson)) };

    var processesBox = new GroupBox { Bounds = new(16, 88, 516, 592), Text = "Processes", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right };
    processesBox.Controls.AddRange(this._search, this._autoRefresh, this._tree, this._refresh, this._count, this._legend);

    var actionBox = new GroupBox { Bounds = new(548, 88, 396, 300), Text = "Strategy", Anchor = AnchorStyles.Top | AnchorStyles.Right };
    actionBox.Controls.AddRange(
      new Label { Bounds = new(16, 28, 360, 18), Text = "Choose how hard to try:" },
      this._strategy,
      this._allowDestructive,
      this._apply,
      this._escalate,
      this._warnBar,
      this._desc);

    // Anchored top as well as bottom: its top edge stays pinned below the Strategy box, so shrinking
    // the window shortens the log instead of sliding it up under the strategy panel.
    var logBox = new GroupBox { Bounds = new(548, 396, 396, 284), Text = "Log", Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right };
    logBox.Controls.Add(this._log);

    this._statusStrip.Bounds = new(0, Height_ - 24, Width_, 24);
    this._statusStrip.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
    this._statusStrip.Items.Add(this._statusLabel);

    this.Controls.AddRange(this.BuildMenuStrip(), header, logo, subtitle, processesBox, actionBox, logBox, this._statusStrip);

    this.RebuildTree();
    this.UpdateDescription();

    if (demo)
      this.SeedDemoScene();
  }

  private IImage Icon(int[] pixels) {
    var side = (int)Math.Round(Math.Sqrt(pixels.Length));
    return this._backend.CreateImage(side, side, pixels);
  }

  private MenuStrip BuildMenuStrip() {
    var file = new ToolStripMenuItem {
      Text = "&File",
      DropDownItems = {
        new ToolStripMenuItem { Text = "&Refresh", ShortcutKeys = Keys.F5, },
        new ToolStripSeparator(),
        new ToolStripMenuItem { Text = "E&xit", ShortcutKeys = Keys.Control | Keys.Q, },
      },
    };
    file.DropDownItems[0].Click += (_, _) => this.RebuildTree();
    ((ToolStripMenuItem)file.DropDownItems[2]).Click += (_, _) => this.Close();

    var process = new ToolStripMenuItem {
      Text = "&Process",
      DropDownItems = {
        new ToolStripMenuItem { Text = "&Apply strategy", ShortcutKeys = Keys.Control | Keys.Enter, },
        new ToolStripMenuItem { Text = "&Escalate ladder", ShortcutKeys = Keys.Control | Keys.E, },
        new ToolStripMenuItem { Text = "&Force kill", ShortcutKeys = Keys.Control | Keys.K, },
        new ToolStripSeparator(),
        new ToolStripMenuItem { Text = "&Copy pid", ShortcutKeys = Keys.Control | Keys.C, },
      },
    };
    process.DropDownItems[0].Click += (_, _) => this.ApplySelectedStrategy();
    process.DropDownItems[1].Click += (_, _) => this.EscalateSelected();
    process.DropDownItems[2].Click += (_, _) => this.ForceKillSelected();
    process.DropDownItems[4].Click += (_, _) => this.CopyPid();

    var view = new ToolStripMenuItem {
      Text = "&View",
      DropDownItems = {
        new ToolStripMenuItem { Text = "&Auto-refresh", ShortcutKeys = Keys.Control | Keys.T, CheckOnClick = true, },
        new ToolStripMenuItem { Text = "Allow &destructive", ShortcutKeys = Keys.Control | Keys.D, CheckOnClick = true, },
      },
    };
    // The menu checks and the in-panel switches mirror each other. Side effects (timer, description)
    // run directly from each handler rather than relying on a programmatic Checked re-firing events.
    var autoItem = (ToolStripMenuItem)view.DropDownItems[0];
    autoItem.CheckedChanged += (_, _) => {
      this._autoRefresh.Checked = autoItem.Checked;
      if (autoItem.Checked)
        this._ticker.Start();
      else
        this._ticker.Stop();
    };
    var destructiveItem = (ToolStripMenuItem)view.DropDownItems[1];
    destructiveItem.CheckedChanged += (_, _) => {
      this._allowDestructive.Checked = destructiveItem.Checked;
      this.UpdateDescription();
    };
    this._allowDestructive.CheckedChanged += (_, _) => {
      destructiveItem.Checked = this._allowDestructive.Checked;
      this.UpdateDescription();
    };

    var help = new ToolStripMenuItem {
      Text = "&Help",
      DropDownItems = {
        new ToolStripMenuItem { Text = "&About", },
      },
    };
    help.DropDownItems[0].Click += (_, _) => MessageBox.Show(
      this,
      "UltimateProcessKiller — an escalating ladder of termination strategies for the process that survives every ordinary kill.\n\nStrategies run gentlest-first; destructive rungs are gated behind an explicit allow.",
      "About UltimateProcessKiller",
      MessageBoxButtons.OK,
      MessageBoxIcon.Information);

    var menu = new MenuStrip { Bounds = new(0, 0, Width_, 24), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    menu.Items.AddRange(new ToolStripItem[] { file, process, view, help });
    return menu;
  }

  private ContextMenuStrip BuildContextMenu() {
    var menu = new ContextMenuStrip();
    var apply = new ToolStripMenuItem { Text = "Apply selected strategy" };
    apply.Click += (_, _) => this.ApplySelectedStrategy();
    var escalate = new ToolStripMenuItem { Text = "Escalate ladder" };
    escalate.Click += (_, _) => this.EscalateSelected();
    var force = new ToolStripMenuItem { Text = "Force kill" };
    force.Click += (_, _) => this.ForceKillSelected();
    var copy = new ToolStripMenuItem { Text = "Copy pid" };
    copy.Click += (_, _) => this.CopyPid();
    menu.Items.AddRange(new ToolStripItem[] { apply, escalate, force, new ToolStripSeparator(), copy });

    if (OperatingSystem.IsLinux()) {
      var reap = new ToolStripMenuItem { Text = "Reap zombie" };
      reap.Click += (_, _) => this.ApplyMethod(TerminationMethod.ReapZombie, confirm: false);
      menu.Items.Add(reap);
    }

    return menu;
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

  /// <summary>Rebuild the process tree from the current snapshot, wiring children under their parents.
  /// A non-empty filter keeps only name/pid matches and expands their branches.</summary>
  private void RebuildTree() {
    var all = this._source();
    this._shown = all.Where(Matches(this._filter)).ToList();
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
      if (this._demo || this._filter.Length > 0)
        root.ExpandAll();
      else
        root.Expand();
    }

    this._count.Text = this._filter.Length > 0
      ? $"{this._shown.Count} of {all.Count} process(es) match '{this._filter}'"
      : $"{all.Count} process(es)";
  }

  private static Func<ProcessSnapshot, bool> Matches(string filter) => snapshot =>
    filter.Length == 0
    || snapshot.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
    || snapshot.Id.ToString().Contains(filter, StringComparison.Ordinal);

  private ProcessSnapshot? Selected() => this._tree.SelectedNode?.Tag is ProcessSnapshot s ? s : null;

  private TerminationMethod? SelectedMethod() => this._strategy.SelectedItem is TerminationMethod m ? m : null;

  private void UpdateDescription() {
    if (this.SelectedMethod() is not { } method) {
      this._desc.Text = "";
      this._warnBar.Visible = false;
      return;
    }

    var destructive = TerminationMethodInfo.IsDestructive(method);
    this._warnBar.Visible = destructive;

    var target = this.Selected() is { } s ? $"Target: {s.Name} ({s.Id})" : "Target: (select a process)";
    this._desc.Text =
      $"{TerminationMethodInfo.CliName(method)}\n" +
      $"{TerminationMethodInfo.Describe(method)}\n" +
      (destructive
        ? "⚠ Destructive — can corrupt files; use the switch above."
        : "Reasonably safe to try before the harsher rungs.") +
      $"\n{target}";
    this._desc.Select(0, 0); // a fresh strategy starts the read at the top, not at the old scroll position
  }

  private void SetBusy(bool busy) {
    this._busy = busy;
    this._apply.Enabled = !busy;
    this._escalate.Enabled = !busy;
    this._refresh.Enabled = !busy;
  }

  private void ApplySelectedStrategy() {
    if (this.SelectedMethod() is not { } method)
      return;

    this.ApplyMethod(method, confirm: TerminationMethodInfo.IsDestructive(method));
  }

  private void ApplyMethod(TerminationMethod method, bool confirm) {
    if (this._busy)
      return;

    if (this.Selected() is not { } target) {
      this.SetStatus("Select a process first.");
      return;
    }

    if (TerminationMethodInfo.IsDestructive(method) && !this._allowDestructive.Checked) {
      this.SetStatus($"{TerminationMethodInfo.CliName(method)} is destructive — flip the switch to use it.");
      return;
    }

    if (confirm && !this._demo && MessageBox.Show(
          this,
          $"{TerminationMethodInfo.Describe(method)}\n\nThis can corrupt files the target holds open. Proceed against {target.Name} ({target.Id})?",
          "Destructive rung",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Warning) != DialogResult.Yes) {
      this.SetStatus("Cancelled.");
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
        Toast.Show(this, result.ProcessGone ? "Process gone" : result.Status.ToString(),
          $"{target.Name} ({target.Id}): {result.Detail}",
          result.ProcessGone ? InfoBarSeverity.Success : InfoBarSeverity.Warning, 4000);
      });
    });
  }

  private void ForceKillSelected() => this.ApplyMethod(TerminationMethod.Kill, confirm: false);

  private void CopyPid() {
    if (this.Selected() is { } target) {
      Clipboard.SetText(target.Id.ToString());
      this.SetStatus($"Copied pid {target.Id}.");
    }
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
    if (allowDestructive && MessageBox.Show(
          this,
          $"Escalation may reach destructive rungs (thread termination, handle closing, memory overwrite).\n\nProceed against {target.Name} ({target.Id})?",
          "Destructive escalation",
          MessageBoxButtons.YesNo,
          MessageBoxIcon.Warning) != DialogResult.Yes) {
      this.SetStatus("Cancelled.");
      return;
    }

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
        Toast.Show(this, report.Succeeded ? "Process gone" : "Survived everything",
          $"{target.Name} ({target.Id}): {report.Attempts.Count} rung(s) tried",
          report.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error, 4000);
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

  private void Append(string line) {
    this._log.Items.Add(line);
    this._log.EnsureVisible(this._log.Items.Count - 1);
  }

  private void SetStatus(string text) => this._statusLabel.Text = text;
}
