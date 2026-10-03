# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

The README carries the user-facing story (vision, full support matrix, CLI usage). This file covers what is needed to work on the code: commands, cross-cutting architecture that spans several files, and repo conventions enforced by CI.

## Commands

```bash
dotnet build UltimateProcessKiller.sln -c Release
dotnet test UltimateProcessKiller.sln -c Release

# single test (NUnit adapter)
dotnet test UltimateProcessKiller.sln -c Release --filter "FullyQualifiedName~LinuxProcessKillerTests.RequestClose_KillsAProcessThatHonoursSigterm"
dotnet test UltimateProcessKiller.sln -c Release --filter "FullyQualifiedName~LinuxProcessKillerTests"   # whole fixture

# run the front ends
dotnet run --project UltimateProcessKiller.Cli -- --list
dotnet run --project UltimateProcessKiller.Cli -- --pid 1234 --escalate
dotnet run --project UltimateProcessKiller.Gui  -- --demo --scene overview   # fake data, no real kills

# single-file publish (profiles: win-x64, linux-x64, osx-arm64, in each app's Properties/PublishProfiles)
dotnet publish UltimateProcessKiller.Cli -c Release -p:PublishProfile=linux-x64

# the AOT gate CI runs — must stay warning-free (IL2xxx/IL3xxx become errors)
dotnet publish UltimateProcessKiller.Cli -c Release -r linux-x64 --self-contained -p:PublishAot=true -p:TreatWarningsAsErrors=true

# GUI headless smoke (needs libgtk-3 + xvfb)
xvfb-run -a --server-args="-screen 0 960x640x24" dotnet run --project UltimateProcessKiller.Gui -c Release -- --demo --exit-after 3
```

Tests are integration tests that kill **real child processes** the test host spawns (children of the test process, so ptrace rungs pass under `yama ptrace_scope=1`). Platform fixtures self-skip via `Assert.Ignore` in `[SetUp]`. `TestProcesses` guarantees cleanup — never spawn targets outside it.

## Architecture (what spans files)

Four projects: **Core** (library, BCL-only, no dependencies), **Cli** (`upk`), **Gui** (`upk-gui`, NativeForms), **Tests** (NUnit 4). TFM `net10.0` and shared metadata come from `Directory.Build.props`; Release builds strip all debug info.

The core contract is `IProcessKiller` → `ProcessKillerBase` → one killer per OS under `Core/Platforms/{Windows,Linux,MacOS}/`. Key invariants:

- **`TerminationMethod` enum order *is* the escalation order** (gentlest → most destructive). `ProcessExecutioner.Escalate` walks `killer.SupportedMethods` in that order and stops at the first `ProcessGone` result. A test (`CrossPlatformTests.SupportedMethodsAreOrderedGentlestFirst`) asserts each platform's `_supported` array stays in ascending enum order — a new method must be inserted in the enum and in every platform array at its invasiveness rank, not appended.
- **Each platform declares exactly the methods it honestly backs** (`_supported` array + `Execute` switch). Unsupported ones return `NotSupported`, never a runtime failure. macOS ships the portable POSIX rungs only (Mach rungs need entitlements); Linux `inject-exit` is x86-64 only; `ReapZombie` is Linux-only and deliberately excluded from the general ladder (only reached via `EscalationOptions.Only`).
- **`ProcessKillerBase.Terminate` is the only entry**: it gates on `Supports`, checks liveness (skipping the gate for `ReapZombie`, which targets already-dead processes), and maps exceptions to statuses. Platform code signals failure by throwing `UnauthorizedAccessException` (→ `AccessDenied`) or returning a `TerminationResult`. `VerifyGone` polls `IsAlive` for 3 s and hedges to `Executed` when the call succeeded but the process is still there — `Executed` is the "polite rung didn't take" status, not an error.
- **Native interop stays local to each platform folder**: `Platforms/Windows/NativeMethods.cs` (source-generated `LibraryImport` over ntdll/kernel32/user32), `Platforms/Linux/LibC.cs` (signals, ptrace, tgkill) + `Proc.cs` (`/proc` reads) + `LinuxProcessKiller.Injection.cs` (partial class with the ptrace register-poking rungs). Adding a deep rung means touching only that platform's folder.
- **AOT/trim-clean is a hard requirement** (`IsAotCompatible=true` on all three apps; CI publishes both with `PublishAot` and warnings-as-errors). Hence: no reflection — strategy metadata lives as explicit switches in `TerminationMethodInfo` (`CliName` kebab-case names, `IsDestructive`, `Describe`, `Parse`), with `Enum.GetValues` used only once for `All`.
- **Destructive gating is centralized**: `TerminationMethodInfo.IsDestructive` drives the CLI's `--allow-destructive`, the escalation skip, and the GUI checkbox. Add a destructive rung by editing that one method.

Front ends add nothing but presentation. `CliRunner` takes the killer and process source as constructor parameters so `CliRunnerTests`/`FakeProcessKiller` can drive it without real processes — keep `Program.cs` thin. The GUI (`MainForm`) runs terminations on `Task.Run` with `BeginInvoke` for UI updates; its `--demo` / `--scene` / `--exit-after` flags are what the CI screenshot job and the headless smoke test use, so don't remove them.

Linux liveness semantics: `IsAlive` treats `Z`/`X` states as not-alive; zombies are a separate concept (`IsZombie`) that only `ReapZombie` operates on.

## Repo conventions

- **Commit subjects are bucketed by prefix** — `+` Added, `*` Changed, `#` Fixed, `-` Removed, `!` TODO. `update-changelog.mjs` generates release notes from these; a non-conforming subject lands in "Other".
- **Versions come from the `VERSION` file + commit count** (computed by `.github/workflows/scripts/version.pl`), never from git tags. Release tags are `vyyyyMMdd` (at most one per day); nightlies are `nightly-yyyyMMdd` and publish automatically after a green CI run on `main`.
- **Formatting**: `.editorconfig` — 2-space indent, LF, final newline (tabs only in `.sln`). House style: explicit `this.`, file-scoped namespaces, XML docs on public API, `switch` expressions for the per-method dispatch.
- **Screenshots** (`docs/screenshots/*.png`) are regenerated only by the manual `screenshots.yml` workflow_dispatch (deterministic Xvfb capture via `shoot.sh`); don't hand-edit or casually touch them — runner font differences would churn the repo.
- CI runs the full test matrix on Linux/Windows/macOS plus the AOT gate plus a GUI smoke on every push/PR to `main`; anything that breaks those (AOT warnings, unsorted `SupportedMethods`, GUI startup) fails the build.
