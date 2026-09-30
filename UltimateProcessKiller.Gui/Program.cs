using System.Globalization;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.MacOS;
using Hawkynt.NativeForms.Backends.Windows;
using UltimateProcessKiller;
using UltimateProcessKiller.Gui;

// Register the native backends this build ships; only the one matching the OS is ever realized.
BackendRegistry.Register(new Win32Backend());
BackendRegistry.Register(new GtkBackend());
BackendRegistry.Register(new CocoaBackend());

var demo = Array.IndexOf(args, "--demo") >= 0;

IProcessKiller killer;
Func<IReadOnlyList<ProcessSnapshot>> source;
if (demo) {
  killer = new DemoData.NoOpKiller();
  source = () => DemoData.Processes;
} else {
  try {
    killer = ProcessKiller.ForCurrentPlatform();
  } catch (PlatformNotSupportedException e) {
    Console.Error.WriteLine(e.Message);
    return 1;
  }

  source = ProcessSnapshotSource.List;
}

var sceneIndex = Array.IndexOf(args, "--scene");
var scene = sceneIndex >= 0 && sceneIndex + 1 < args.Length ? args[sceneIndex + 1] : "overview";

var form = new MainForm(killer, source, demo, scene);

// --exit-after <seconds> auto-closes the window; the screenshot job uses it so a capture run can
// never leave a GUI process hanging on the CI runner.
var exitIndex = Array.IndexOf(args, "--exit-after");
if (exitIndex >= 0 && exitIndex + 1 < args.Length
    && double.TryParse(args[exitIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) {
  form.Load += (_, _) => {
    var timer = new Hawkynt.NativeForms.Timer { Interval = Math.Max(1, (int)(seconds * 1000)) };
    timer.Tick += (_, _) => {
      timer.Stop();
      form.Close();
    };
    timer.Start();
  };
}

Application.Run(form);
return 0;
