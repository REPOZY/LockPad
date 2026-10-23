# Contributing to LockPad

## Building LockPad

Requirements: Windows 10 or 11 and a current [.NET SDK](https://dotnet.microsoft.com/download).
LockPad is developed with .NET 10; the SDK builds the .NET Framework 4.8 app
through a reference-assemblies package.

```powershell
git clone https://github.com/REPOZY/LockPad.git
cd LockPad
dotnet build LockPad.sln -c Release
dotnet test LockPad.sln -c Release
```

Start `src\LockPad.App\bin\Release\net48\LockPad.exe`.

**Release builds.** Build a release from the app project alone, so that no
build logic from the test packages runs in that build:
`dotnet build src\LockPad.App\LockPad.App.csproj -c Release`. For the portable
copy, put these four files from `src\LockPad.App\bin\Release\net48` in one
folder: `LockPad.exe`, `LockPad.Core.dll`, `KeePassLib.dll` and
`LockPad.exe.config`. The `.pdb` files are debug information and are not needed.

If `dotnet` is not on your PATH (for example after a user-only install), call it
by its full path: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build LockPad.sln -c Release`.

## Project layout

```
LockPad.sln
lib/KeePassLib/       Official KeePassLib 2.61.1, unmodified (do not edit)
lib/KeePassData/      KeePass's common-password list, used by the strength meter
src/LockPad.Core/     No UI: storage, the save pipeline, crypto settings, paths, settings
src/LockPad.App/      WPF app: views, view models, clipboard, dialogs, themes
tests/LockPad.Tests/  NUnit tests for Core and the App services
docs/                 User guide, design specs, plans, manual test checklist, review records
marketing/video/      Source of the promo video (Remotion)
```

Dependencies go one way: App uses Core, Core uses KeePassLib. Core has no WPF
reference.

## Tests

```powershell
dotnet test LockPad.sln
```

- **Unit and integration tests** cover the save pipeline (with failure
  injection), locking and closing, clipboard ownership, settings, path rules,
  capture protection, and every fixed finding from the security reviews.
- **Repository guards** fail the run if `lib/KeePassLib` no longer matches
  `lib/keepasslib.sha256`, a network API appears in `src/`, C# source contains
  non-ASCII characters, the code switches on test-only settings, a package is not
  pinned to one exact version, or an MSBuild file other than the reviewed root
  `Directory.Build.props`, `.targets` and `.rsp` appears.
- **Interop tests** (`[Category("Interop")]`) check that LockPad and KeePassXC can
  read each other's files when `C:\Program Files\KeePassXC\keepassxc-cli.exe`
  exists; otherwise they are skipped.
- **Real-clipboard tests** (`[Category("RealClipboard")]`) use the real Windows
  clipboard: avoid copying anything while they run. Their harmless test strings
  can appear in your Win+V history.

Every file a test creates goes into a temporary folder; the tests never touch
your real notebooks. Manual checks are in `docs/manual-test-checklist.md`.

## Rules

- Never edit anything in `lib/KeePassLib/`. To update it, replace the folder with
  a newer official release, verify the package hash against
  [keepass.info](https://keepass.info/integrity.html), and regenerate
  `lib/keepasslib.sha256` (see `lib/KEEPASSLIB.md`).
- No network code in `src/`, no auto-update, no telemetry. (KeePassLib contains
  network code; LockPad only ever passes it local paths, and a guard test checks
  this.)
- Keep C# and XAML source pure ASCII: write other characters as `(char)0xNNNN` in
  C# and `&#xNNNN;` in XAML.
- LockPad must never write to a file without the LockPad notebook marker
  (`LockPad.Notebook` in the database's custom data).
- New NuGet packages need a strong reason. The current ones are pinned to exact
  versions (`[x.y.z]`) in the project files and `Directory.Build.props`, and
  `nuget.config` allows only nuget.org. Restore runs in locked mode against the
  committed `packages.lock.json` files, which record each package's content
  hash. To change a package, run `dotnet restore LockPad.sln --force-evaluate`,
  then review and commit the updated lock files.
- Security-relevant changes get a review and a red-team pass; findings and their
  dispositions are recorded in `docs/reviews/`.

## Design documents

- Core design: `docs/specs/2026-10-15-*-design.md` (with the amendments made
  during the build); later designs are in the same folder.
- Implementation plans: `docs/plans/`.
- Review records: `docs/reviews/`.
