using System.Runtime.InteropServices;
using UltimateProcessKiller.Platforms.Linux;
using UltimateProcessKiller.Platforms.MacOS;
using UltimateProcessKiller.Platforms.Windows;

namespace UltimateProcessKiller;

/// <summary>Entry point: hands back the killer that matches the running operating system.</summary>
public static class ProcessKiller {
  /// <summary>The killer implementation for the current platform.</summary>
  public static IProcessKiller ForCurrentPlatform() {
    if (OperatingSystem.IsWindows())
      return new WindowsProcessKiller();

    if (OperatingSystem.IsLinux())
      return new LinuxProcessKiller();

    if (OperatingSystem.IsMacOS())
      return new MacProcessKiller();

    throw new PlatformNotSupportedException(
      $"UltimateProcessKiller has no killer for {RuntimeInformation.OSDescription}."
    );
  }
}
