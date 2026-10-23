# LockPad Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-optimized:subagent-driven-development (recommended) or superpowers-optimized:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build LockPad, a WPF notepad that stores notebooks of plain-text notes in KDBX files (official KeePassLib), with crash-safe saving, auto-lock and a protected clipboard.
**Architecture:** `LockPad.App` (WPF, MVVM) → `LockPad.Core` (no UI: credentials, notebook model, save pipeline, settings, idle policy) → `lib/KeePassLib` (official KeePass 2.61.1 source, unmodified, built by our own SDK-style project). Tests in `tests/LockPad.Tests` cover Core, view-models and the clipboard service.
**Tech Stack:** C# 7.3, .NET Framework 4.8, WPF, KeePassLib 2.61.1, NUnit 4.2.2, .NET 10 SDK (user-local).
**Spec:** `docs/specs/2026-10-15-lockpad-core-design.md`
**Assumptions:**
- Assumes Windows 10/11 with .NET Framework 4.8+ runtime (present: release 533325) — will NOT run on Linux/macOS.
- Assumes the .NET 10 SDK at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` (installed 10.0.402) — commands will NOT work via a bare `dotnet` (not on PATH).
- Assumes the verified KeePass source is extracted at `C:\Users\TJERKP~1\AppData\Local\Temp\claude\c--Users-Tjerk-Pieksma-Documents-Github-Crypto-Notepad\ea79b3f2-2b91-470a-aef7-c2a5942b2dff\scratchpad\keepass-src\` (zip + `x\` folder) — if missing, Task 1 re-downloads and re-verifies it.
- Assumes `keepassxc-cli.exe` 2.7.12 at `C:\Program Files\KeePassXC\` — interop tests are ignored (not failed) if absent.
- Assumes SDK-style `UseWPF` builds for `net48` — Task 1 verifies; if not, STOP and report.

## Global Constraints

- All commands run in PowerShell from `C:\Users\Tjerk Pieksma\Documents\Github\LockPad` after: `$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"`.
- Target `net48` everywhere. Language version: SDK default for net48 (C# 7.3). No `Nullable`, no records.
- NuGet packages allowed (exact versions, nothing else without user approval): `Microsoft.NETFramework.ReferenceAssemblies 1.0.3`, `NUnit 4.2.2`, `NUnit3TestAdapter 4.6.0`, `Microsoft.NET.Test.Sdk 17.11.1`.
- `nuget.config` at repo root clears all sources and adds only `https://api.nuget.org/v3/index.json`. Never edit the user's global NuGet config.
- `lib/KeePassLib/` is the official KeePass 2.61.1 `KeePassLib` folder, byte-identical; never edit any file in it. Its integrity is enforced by `lib/keepasslib.sha256` + a test.
- No network code in `src/` (no `System.Net`, `HttpClient`, `WebClient`, `WebRequest`, `Socket`); enforced by a test. No auto-update, telemetry, plugins or sync.
- LockPad never writes to a file that lacks the marker `CustomData["LockPad.Notebook"] == "1"`.
- Notebook file: AES-256 (`StandardAesEngine.AesUuid`), Argon2id (`Argon2Type.ID`), `MemoryProtection.ProtectNotes = true`, notes in group `Notes` directly under root, `HistoryMaxItems = 10`, `RecycleBinEnabled = false`.
- Save = temp file in same directory → `Flush(true)` → reopen-verify temp → shift backups → `File.Replace(temp, target, target + ".bak1", true)` (new file: `File.Move`). Never rely on KeePassLib's `UseFileTransactions`.
- Passwords enter KeePassLib only as UTF-8 bytes via `KcpPassword(byte[], false)`; every intermediate `char[]`/`byte[]` copy is zeroed in `finally`.
- Defaults/ranges: idle lock 5 min (1–60), clipboard clear 20 s (5–120), font size 14 pt (8–48), theme `System|Light|Dark` (default `System`), backups kept 3, history 10, KDF target 1000 ms, strength warn < 80 bits, confirm < 60 bits.
- Exact user-facing strings (use verbatim):
  - `Wrong password or key file.`
  - `File not found.`
  - `This file is not a valid notebook or KeePass database.`
  - `This file was not created by LockPad — it may be your password database. It is opened read-only.`
  - `Passwords do not match.`
  - `File already exists.`
  - `Unsaved changes are held in memory. Saving failed: {reason}`
  - `(untitled)` for empty note titles in the list.
- Commit after every task (local only, never push), message ends with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Notebook stored where saving fails (read-only file attribute, OneDrive placeholder, locked file) → save reports `IoError`, real file byte-identical. Test: Task 6 `Save_ReadOnlyFileAttribute_ReturnsIoError_OriginalUntouched`.
2. Very large pasted content (5 MB note, exceeds KeePassLib's 6 MB history size budget after a few versions) → save and reopen still exact. Test: Task 6 `Save_LargeNote5MB_RoundTrips`.
3. Empty / whitespace / duplicate note titles → stored and reopened verbatim; list shows `(untitled)` for empty. Tests: Task 4 `AddNote_EmptyTitle_Allowed`, Task 12 `NoteItem_EmptyTitle_DisplaysUntitled`.
4. Stale `*.tmp` files left by a crash next to the notebook → next save succeeds, Open ignores them. Test: Task 6 `Save_StaleTempFilePresent_StillSaves`.
5. Key file moved/deleted after unlocking → saving still works (key already loaded), verify still passes. Test: Task 6 `Save_KeyFileDeletedAfterOpen_StillSaves`.

---

## File Structure

```
LockPad/
├─ nuget.config                     package source lock-down
├─ Directory.Build.props            shared: ReferenceAssemblies package, Deterministic
├─ .gitignore
├─ LockPad.sln
├─ LICENSE                          GPL-2.0 text (copied from KeePass Docs/License.txt)
├─ README.md
├─ lib/
│  ├─ KeePassLib/                   official, unmodified (incl. its own .csproj/.pfx, unused)
│  ├─ KeePassLib.csproj             our SDK-style build of the above
│  ├─ keepasslib.sha256             per-file SHA-256 manifest
│  └─ KEEPASSLIB.md                 version, source URL, zip SHA-256
├─ src/LockPad.Core/
│  ├─ Credentials.cs  Errors.cs  LocalPath.cs
│  ├─ FileFingerprint.cs  BackupRotator.cs
│  ├─ Note.cs  Notebook.cs
│  ├─ SaveResult.cs  NotebookStore.cs
│  ├─ KdfTuner.cs  PasswordStrength.cs
│  ├─ AppSettings.cs  SettingsStore.cs
│  ├─ IClock.cs  IdleLockPolicy.cs
│  └─ ClipboardFormats.cs
├─ src/LockPad.App/
│  ├─ App.xaml(.cs)  MainWindow.xaml(.cs)
│  ├─ Infrastructure/ObservableObject.cs  RelayCommand.cs
│  ├─ Services/IDialogService.cs  DialogService.cs  IClipboardService.cs  ClipboardService.cs  ThemeService.cs  TextBoxClipboardGuard.cs
│  ├─ Themes/Light.xaml  Dark.xaml
│  ├─ ViewModels/ShellViewModel.cs  UnlockViewModel.cs  CreateNotebookViewModel.cs  EditorViewModel.cs  NoteItemViewModel.cs  SettingsViewModel.cs
│  └─ Views/UnlockView.xaml(.cs)  CreateNotebookView.xaml(.cs)  EditorView.xaml(.cs)  PromptWindow.xaml(.cs)  SettingsWindow.xaml(.cs)
├─ tests/LockPad.Tests/             one test file per unit (named <Unit>Tests.cs) + TestKdf.cs, TempDir.cs, Fakes.cs
└─ docs/  specs/  plans/  manual-test-checklist.md  reviews/
```

---

### Task 1: Scaffold solution and vendor KeePassLib

**Files:**
- Create: `nuget.config`, `Directory.Build.props`, `.gitignore`, `LockPad.sln`, `LICENSE`, `README.md`
- Create: `lib/KeePassLib/**` (copy), `lib/KeePassLib.csproj`, `lib/keepasslib.sha256`, `lib/KEEPASSLIB.md`
- Create: `src/LockPad.Core/LockPad.Core.csproj`, `src/LockPad.App/LockPad.App.csproj`, `src/LockPad.App/App.xaml(.cs)`, `src/LockPad.App/MainWindow.xaml(.cs)` (empty window titled `LockPad`)
- Create: `tests/LockPad.Tests/LockPad.Tests.csproj`, `tests/LockPad.Tests/TestKdf.cs`, `tests/LockPad.Tests/TempDir.cs`
- Test: `tests/LockPad.Tests/RepositoryGuardTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - Test helper `internal static class TestKdf { public static KdfParameters Fast(); }` — `new Argon2Kdf(Argon2Type.ID).GetDefaultParameters()` with `SetUInt64(Argon2Kdf.ParamIterations, 1)` and `SetUInt64(Argon2Kdf.ParamMemory, 1024 * 1024)`.
  - Test helper `internal sealed class TempDir : IDisposable { public string Path { get; } public string File(string name); }` — unique folder under `%TEMP%\LockPadTests\`, deleted on Dispose (clear read-only attributes first).
  - Assemblies: `KeePassLib`, `LockPad.Core` (InternalsVisibleTo `LockPad.Tests`), `LockPad` (WinExe, from `LockPad.App.csproj`), `LockPad.Tests`.

**Security flag:** `security` (supply chain: vendored crypto library, package sources)

- [x] **Step 1: Re-verify the KeePass source zip**

Run: `(Get-FileHash "C:\Users\TJERKP~1\AppData\Local\Temp\claude\c--Users-Tjerk-Pieksma-Documents-Github-Crypto-Notepad\ea79b3f2-2b91-470a-aef7-c2a5942b2dff\scratchpad\keepass-src\KeePass-2.61.1-Source.zip" -Algorithm SHA256).Hash`
Expected: `711BD9EC7076661678469E51BAE876DEBAC07A08C32F1B1C6B3B8FE5E761C13A`. If the file is missing, download `https://sourceforge.net/projects/keepass/files/KeePass%202.x/2.61.1/KeePass-2.61.1-Source.zip/download` into a new empty folder and require the same hash; any mismatch → STOP.

- [x] **Step 2: Copy vendored files**

Copy the whole extracted `x\KeePassLib\` folder to `lib\KeePassLib\` (all files, unchanged). Copy `x\Docs\License.txt` to `LICENSE`. Generate `lib/keepasslib.sha256`: one line per file under `lib/KeePassLib`, sorted by relative path, format `<UPPERCASE-SHA256>  <path relative to lib/KeePassLib, forward slashes>`.

- [x] **Step 3: Write content files**

`nuget.config`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`lib/KeePassLib.csproj` (default compile glob picks up `KeePassLib/**/*.cs`; verified to build with 0 warnings):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>KeePassLib</AssemblyName>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <NoWarn>1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System.Security" />
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System.Drawing" />
  </ItemGroup>
</Project>
```

`src/LockPad.Core/LockPad.Core.csproj`: `net48`, `TreatWarningsAsErrors=true`, `<Reference Include="System.Security" />`, `<Reference Include="System.Runtime.Serialization" />`, ProjectReference `..\..\lib\KeePassLib.csproj`, `<InternalsVisibleTo Include="LockPad.Tests" />`.

`src/LockPad.App/LockPad.App.csproj`: `OutputType=WinExe`, `net48`, `UseWPF=true`, `AssemblyName=LockPad`, `RootNamespace=LockPad.App`, `TreatWarningsAsErrors=true`, ProjectReference Core, `<InternalsVisibleTo Include="LockPad.Tests" />`.

`tests/LockPad.Tests/LockPad.Tests.csproj`: `net48`, `UseWPF=true`, `IsPackable=false`, packages `NUnit 4.2.2`, `NUnit3TestAdapter 4.6.0`, `Microsoft.NET.Test.Sdk 17.11.1`, ProjectReferences Core, App and `lib\KeePassLib.csproj`.

`lib/KEEPASSLIB.md`:
```markdown
# Vendored KeePassLib

- Source: KeePass 2.61.1 source package, https://sourceforge.net/projects/keepass/files/KeePass%202.x/2.61.1/KeePass-2.61.1-Source.zip/download
- Package SHA-256: 711BD9EC7076661678469E51BAE876DEBAC07A08C32F1B1C6B3B8FE5E761C13A (matches https://keepass.info/integrity.html)
- Folder `KeePassLib/` is byte-identical to the package's `KeePassLib/` folder. Do not edit it.
- Built by `KeePassLib.csproj` (ours). The official `.csproj`/`.pfx` files inside the folder are kept unmodified and unused.
- License: GPL-2.0-or-later (see repository `LICENSE`).
- To update: replace the folder with a newer official release, verify its hash against keepass.info, regenerate `keepasslib.sha256`.
```

`README.md`:
```markdown
# LockPad

A Windows notepad for sensitive text. Each notebook is a standard KDBX file
(KeePass format), so KeePassXC can always open it as a fallback.

- Build: `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -c Release`
- Test:  `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test`
- Design: `docs/specs/2026-10-15-lockpad-core-design.md`

License: GPL-2.0-or-later (LockPad includes KeePassLib, which is GPL-2.0-or-later).
```

`.gitignore`: `bin/`, `obj/`, `.vs/`, `*.user`, `TestResults/`.

Create `LockPad.sln` with `& $dotnet new sln -n LockPad` and `& $dotnet sln add` for the four projects.

- [x] **Step 4: Write failing guard tests** in `tests/LockPad.Tests/RepositoryGuardTests.cs`

```csharp
[Test] public void VendoredKeePassLib_MatchesManifest()
// every manifest line: file exists and SHA-256 equals; and every file under lib/KeePassLib appears in the manifest
// Assert.That(mismatches, Is.Empty)
[Test] public void Source_DoesNotReferenceNetworkApis()
// scan src/**/*.cs and src/**/*.xaml for "System.Net", "HttpClient", "WebClient", "WebRequest", "Socket"
// Assert.That(hits, Is.Empty)
[Test] public void KeePassLib_SelfTestAndArgon2idAvailable()
// Assert.That(new Argon2Kdf(Argon2Type.ID).Name, Is.EqualTo("Argon2id"))
```
Repository root is found by walking up from `TestContext.CurrentContext.TestDirectory` until `LockPad.sln` exists.

- [x] **Step 5: Build and run**

Run: `& $dotnet build LockPad.sln -c Debug` → Expected: `Build succeeded`, `0 Error(s)`. If the WPF project fails to build for net48, STOP and report (assumption violated).
Run: `& $dotnet test LockPad.sln --filter "FullyQualifiedName~RepositoryGuardTests"` → Expected: 3 passed.

- [x] **Step 6: Commit**

```bash
git add -A
git commit -m "Scaffold LockPad solution with vendored KeePassLib 2.61.1"
```

---

### Task 2: Credentials, local-path rule, error types

**Files:**
- Create: `src/LockPad.Core/Credentials.cs`, `src/LockPad.Core/LocalPath.cs`, `src/LockPad.Core/Errors.cs`
- Test: `tests/LockPad.Tests/CredentialsTests.cs`, `tests/LockPad.Tests/LocalPathTests.cs`

**Interfaces:**
- Consumes: KeePassLib `CompositeKey`, `KcpPassword(byte[] pbPasswordUtf8, bool bRememberPassword)`, `KcpKeyFile(string)`, `KcpKeyFile.Create(string, byte[])`, `CompositeKey.EqualsValue(CompositeKey)`
- Produces (namespace `LockPad.Core`):
  - `public sealed class Credentials : IDisposable`
    - `public Credentials(byte[] passwordUtf8, string keyFilePath)` — builds the CompositeKey immediately; does not keep `passwordUtf8` (caller zeroes it); `keyFilePath` may be null.
    - `public static Credentials FromSecureString(SecureString password, string keyFilePath)`
    - `public string KeyFilePath { get; }`
    - `internal CompositeKey CompositeKey { get; }`
    - `public bool Matches(Credentials other)`
    - `public static void CreateKeyFile(string path)`
    - `public void Dispose()`
  - `public static class LocalPath { public static string RequireLocalFile(string path); }`
  - `public sealed class WrongCredentialsException : Exception`, `public sealed class NotebookFormatException : Exception` (ctor `(string message, Exception inner)`), `public sealed class NonLocalPathException : ArgumentException`

**Security flag:** `security`

**Does NOT cover:** empty password with key file only (rejected: password is always required); YubiKey/Windows Hello keys.

- [x] **Step 1: Write failing tests**

```csharp
// CredentialsTests
SamePassword_Matches()                    // new Credentials(U("pw1"), null).Matches(new Credentials(U("pw1"), null)) == true
DifferentPassword_DoesNotMatch()          // "pw1" vs "pw2" == false
NonAsciiPassword_IsUsedVerbatim()         // U("Abc№123") matches U("Abc№123"); does NOT match U("Abc?123")
FromSecureString_EqualsUtf8Bytes()        // SecureString "пароль№" matches new Credentials(UTF8("пароль№"), null)
EmptyPassword_Throws()                    // Assert.Throws<ArgumentException>(() => new Credentials(new byte[0], null))
KeyFile_ChangesKey()                      // CreateKeyFile(f); Credentials(U("pw"), f).Matches(Credentials(U("pw"), null)) == false
MissingKeyFile_ThrowsIOException()        // Assert.Throws(Is.InstanceOf<IOException>(), () => new Credentials(U("pw"), dir.File("nope.keyx")))
CreateKeyFile_RefusesToOverwrite()        // File.WriteAllText(f,"x"); Assert.Throws<IOException>(() => Credentials.CreateKeyFile(f)); File.ReadAllText(f) == "x"
// helper U(s) = Encoding.UTF8.GetBytes(s)

// LocalPathTests
DrivePath_ReturnsFullPath()               // RequireLocalFile(@"C:\x\a.kdbx") == @"C:\x\a.kdbx"
RelativePath_ReturnsFullPath()            // == Path.GetFullPath("a.kdbx")
UncPath_Allowed()                         // @"\\server\share\a.kdbx" returned unchanged
[TestCase("https://evil.example/a.kdbx")] [TestCase("ftp://x/a.kdbx")] [TestCase("file:///C:/a.kdbx")] [TestCase("")] [TestCase("   ")] [TestCase(null)]
Rejected(string p)                        // Assert.Throws<NonLocalPathException>
```

- [x] **Step 2: Run tests to verify they fail**

Run: `& $dotnet test tests/LockPad.Tests --filter "FullyQualifiedName~CredentialsTests|FullyQualifiedName~LocalPathTests"`
Expected: FAIL (compile errors: types not defined).

- [x] **Step 3: Implement**

- `Credentials(byte[], string)`: throw `ArgumentException` if null/empty; `new KcpPassword(passwordUtf8, false)`; add `new KcpKeyFile(keyFilePath)` if keyFilePath non-null (let its `FileNotFoundException` propagate).
- `FromSecureString`: `Marshal.SecureStringToGlobalAllocUnicode` → copy to `char[]` → `Encoding.UTF8.GetBytes(char[])` → ctor; in `finally` zero the `char[]` and `byte[]` and call `Marshal.ZeroFreeGlobalAllocUnicode`.
- `Matches`: `CompositeKey.EqualsValue(other.CompositeKey)`.
- `CreateKeyFile`: throw `IOException("File already exists.")` if the path exists, else `KcpKeyFile.Create(path, null)`.
- `RequireLocalFile`: reject null/whitespace; reject if the string contains `"://"` or starts with `"file:"` (case-insensitive); otherwise return `Path.GetFullPath(path)`.

- [x] **Step 4: Run tests to verify they pass**

Run: same command → Expected: PASS (all).

- [x] **Step 5: Commit**

```bash
git add src/LockPad.Core tests/LockPad.Tests
git commit -m "Add credentials, local-path rule and error types"
```

---

### Task 3: File fingerprint and backup rotation

**Files:**
- Create: `src/LockPad.Core/FileFingerprint.cs`, `src/LockPad.Core/BackupRotator.cs`
- Test: `tests/LockPad.Tests/FileFingerprintTests.cs`, `tests/LockPad.Tests/BackupRotatorTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `public sealed class FileFingerprint { public static FileFingerprint Compute(string path); public static readonly FileFingerprint Missing; public bool Exists { get; } public bool SameContentAs(FileFingerprint other); }` — SHA-256 of file bytes; reads with `FileShare.ReadWrite | FileShare.Delete`; `Compute` of a nonexistent path returns `Missing`.
  - `public static class BackupRotator { public const int Keep = 3; public static string BackupPath(string notebookPath, int n); public static void ShiftOlderBackups(string notebookPath); }` — `BackupPath(p, n) = p + ".bak" + n`. `ShiftOlderBackups`: delete `.bak3`, move `.bak2→.bak3`, move `.bak1→.bak2` (each only if it exists). Leaves `.bak1` free.

**Security flag:** `none`

- [x] **Step 1: Write failing tests**

```csharp
SameBytes_SameContent()                   // two files "abc" → SameContentAs == true
DifferentBytes_NotSame()                  // "abc" vs "abd" → false
Missing_NotExists()                       // Compute(nonexistent).Exists == false; SameContentAs(FileFingerprint.Missing) == true
ReadsFileHeldOpenByWriter()               // open FileStream(f, Open, ReadWrite, FileShare.ReadWrite); Compute(f) does not throw
Shift_ThreeBackups_DropsOldest()          // bak1="1", bak2="2", bak3="3" → after: bak2=="1", bak3=="2", !Exists(bak1)
Shift_OnlyBak1()                          // bak1="1" → bak2=="1", !Exists(bak1), !Exists(bak3)
Shift_NoBackups_NoOp()                    // no exception, no files created
```

- [x] **Step 2: Run to verify fail** — `& $dotnet test tests/LockPad.Tests --filter "FullyQualifiedName~FileFingerprintTests|FullyQualifiedName~BackupRotatorTests"` → FAIL (types missing).
- [x] **Step 3: Implement** the two types as specified in Interfaces.
- [x] **Step 4: Run to verify pass** — same command → PASS.
- [x] **Step 5: Commit** — `git add src tests && git commit -m "Add file fingerprint and backup rotation"`

---

### Task 4: Note and Notebook model

**Files:**
- Create: `src/LockPad.Core/Note.cs`, `src/LockPad.Core/Notebook.cs`
- Test: `tests/LockPad.Tests/NotebookTests.cs`

**Interfaces:**
- Consumes: `Credentials` (Task 2), `FileFingerprint` (Task 3), KeePassLib `PwDatabase`, `PwUuid`, `ProtectedString`
- Produces:
  - `public sealed class Note { public string Id { get; } public string Title { get; } public string Text { get; } public DateTime LastModifiedUtc { get; } }` — `Id` = 32-char uppercase hex (`PwUuid.ToHexString()`); text held internally as `ProtectedString(true, …)`, `Text` returns `ReadString()`.
  - `public sealed class Notebook : IDisposable`
    - `internal Notebook(string filePath, bool isReadOnly, IEnumerable<Note> notes)`
    - `public string FilePath { get; internal set; }`, `public bool IsReadOnly { get; }`, `public bool IsDirty { get; }`
    - `public IReadOnlyList<Note> Notes { get; }` (file order, new notes appended)
    - `public Note AddNote(string title)` — new random id, empty text
    - `public void UpdateNote(string id, string title, string text)` — null → `""`
    - `public void RemoveNote(string id)`
    - `public Note FindNote(string id)` (null if absent)
    - `public bool CredentialsMatch(Credentials other)`
    - `public event EventHandler DirtyChanged`
    - `internal PwDatabase Database { get; set; }`, `internal Credentials Credentials { get; set; }`, `internal FileFingerprint LoadedFingerprint { get; set; }`, `internal IReadOnlyCollection<string> RemovedIds { get; }`, `internal void MarkClean()` (clears dirty + RemovedIds)
    - `Dispose()` closes `Database` and disposes `Credentials`.
  - `internal static Note Note.Create(string id, string title, string text, DateTime lastModifiedUtc)`

**Security flag:** `none`

**Does NOT cover:** persistence (Task 5/6); ordering/sorting UI.

- [x] **Step 1: Write failing tests**

```csharp
AddNote_MarksDirty_AssignsUniqueHexIds()  // two notes: ids differ, each matches ^[0-9A-F]{32}$, IsDirty == true
AddNote_EmptyTitle_Allowed()              // AddNote("").Title == ""
UpdateNote_SameValues_NotDirty()          // fresh clean notebook with one note; UpdateNote(id, sameTitle, sameText) → IsDirty == false
UpdateNote_Changed_DirtyAndEventOnce()    // change text twice → DirtyChanged raised exactly 1 time; FindNote(id).Text == new text
UpdateNote_UnknownId_Throws()             // Assert.Throws<KeyNotFoundException>
RemoveNote_RemovesAndRecordsId()          // Notes.Count decreases; RemovedIds contains id; IsDirty
ReadOnly_MutationsThrow()                 // isReadOnly:true → AddNote/UpdateNote/RemoveNote throw InvalidOperationException
MarkClean_ResetsDirtyAndRemovedIds()
```

- [x] **Step 2: Run to verify fail** — `--filter "FullyQualifiedName~NotebookTests"` → FAIL.
- [x] **Step 3: Implement** per Interfaces; `DirtyChanged` fires only on false→true and true→false transitions.
- [x] **Step 4: Run to verify pass** → PASS.
- [x] **Step 5: Commit** — `git commit -m "Add note and notebook model"`

---

### Task 5: NotebookStore — create, open, verified atomic write

**Files:**
- Create: `src/LockPad.Core/SaveResult.cs`, `src/LockPad.Core/NotebookStore.cs`
- Test: `tests/LockPad.Tests/NotebookStoreOpenTests.cs`

**Interfaces:**
- Consumes: `Credentials`, `LocalPath.RequireLocalFile`, `WrongCredentialsException`, `NotebookFormatException`, `FileFingerprint`, `BackupRotator`, `Notebook`, `Note.Create`; KeePassLib `PwDatabase.New/Open/SaveAs/Close`, `IOConnectionInfo.FromPath`, `InvalidCompositeKeyException`, `StandardAesEngine.AesUuid`, `PwGroup`, `PwEntry`, `PwDefs.TitleField`, `PwDefs.NotesField`
- Produces:
  - `public enum SaveStatus { Saved, ExternalChangeDetected, ReadOnly, VerifyFailed, IoError }`
  - `public sealed class SaveResult { public SaveStatus Status { get; } public string Message { get; } public static SaveResult Ok(); public static SaveResult Fail(SaveStatus s, string message); }`
  - `public sealed class NotebookStore`
    - `public const string MarkerKey = "LockPad.Notebook";` `public const string NotesGroupName = "Notes";`
    - `public Notebook Create(string path, Credentials credentials, KdfParameters kdf)` — throws `IOException("File already exists.")` if path exists; returns an open, clean notebook. The notebook takes ownership of `credentials`.
    - `public Notebook Open(string path, Credentials credentials)` — `FileNotFoundException`, `WrongCredentialsException`, `NotebookFormatException`; notebook takes ownership of `credentials`.
    - `internal SaveResult WriteVerifiedAndCommit(Notebook notebook, string targetPath)` — the pipeline below; used by Create and (Task 6) Save/SaveAs.
    - `internal Action<string, string> TestHook { get; set; }` — invoked as `(stage, tempPath)` with stage `"after-temp-write"` (after flush, before verify) and `"before-replace"`.

**Security flag:** `security`

**Does NOT cover:** applying edits / backups / external-change checks (Task 6). Unmarked files are never written (enforced in Task 6 via `ReadOnly`).

- [x] **Step 1: Write failing tests**

```csharp
Create_ThenOpen_EmptyNotebook()           // Open(...).Notes.Count == 0, IsReadOnly == false, IsDirty == false
Create_WritesRequiredFormatSettings()     // raw PwDatabase.Open: CustomData.Get("LockPad.Notebook")=="1"; DataCipherUuid.Equals(StandardAesEngine.AesUuid);
                                          // KdfParameters.KdfUuid.Equals(new Argon2Kdf(Argon2Type.ID).Uuid); MemoryProtection.ProtectNotes; RootGroup has child group "Notes";
                                          // HistoryMaxItems == 10; RecycleBinEnabled == false
Create_ExistingFile_Throws_FileUnchanged()// File.WriteAllText(p,"x") → Throws<IOException>; content still "x"
Create_UrlPath_Throws()                   // Throws<NonLocalPathException>("https://x/a.kdbx")
Create_LeavesNoTempFiles()                // Directory.GetFiles(dir, "*.tmp") is empty
Open_WrongPassword_Throws()               // Throws<WrongCredentialsException>
[TestCase("Abc№123")] [TestCase("пароль")] Open_NonAsciiPassword_RoundTrips(string pw)
Open_KeyFileRequired()                    // created with key file: without key → WrongCredentialsException; with key → opens
Open_RandomBytes_ThrowsFormat()           // 4 KB random file → Throws<NotebookFormatException>
Open_Missing_ThrowsFileNotFound()
Open_UnmarkedKdbx_ReadOnly_NotesOnly()    // build with raw KeePassLib: entries in nested groups, Password field "SECRET-PW", Notes "n1";
                                          // → IsReadOnly; Notes contains title/notes of every entry (recursive); no Note.Text or Title contains "SECRET-PW"
WriteVerified_HookStages_Called()         // TestHook records ["after-temp-write","before-replace"] during Create
```

- [x] **Step 2: Run to verify fail** — `--filter "FullyQualifiedName~NotebookStoreOpenTests"` → FAIL.

- [x] **Step 3: Implement `Create`** — `RequireLocalFile`; existence check; `new PwDatabase()`, `.New(IOConnectionInfo.FromPath(path), credentials.CompositeKey)`; set `DataCipherUuid = StandardAesEngine.AesUuid`, `KdfParameters = kdf`, `MemoryProtection.ProtectNotes = true`, `HistoryMaxItems = 10`, `RecycleBinEnabled = false`, `UseFileTransactions = false`, `CustomData.Set(MarkerKey, "1")`; add group `Notes` under root; build `Notebook`; `WriteVerifiedAndCommit(nb, path)`; on non-`Saved` result dispose and throw `IOException(result.Message)`.

- [x] **Step 4: Implement `Open`** — `RequireLocalFile`; missing → `FileNotFoundException`; fingerprint before open, `PwDatabase.Open(ioc, key, null)`, fingerprint after; if they differ retry once, then throw `IOException("The file changed while it was being opened.")`. Map `InvalidCompositeKeyException` → `WrongCredentialsException`; any other non-IO exception from KeePassLib → `NotebookFormatException("This file is not a valid notebook or KeePass database.", ex)`. Marked → notes = entries of root child group `Notes` (Title, Notes fields). Unmarked → read-only; notes = all entries recursively, Title and Notes fields only.

- [x] **Step 5: Implement `WriteVerifiedAndCommit`**

```
temp = Path.Combine(dir(target), Path.GetFileName(target) + "." + 8 random hex chars + ".tmp")
try:
  db.UseFileTransactions = false; db.SaveAs(IOConnectionInfo.FromPath(temp), false, null)
  using (var fs = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) fs.Flush(true)
  TestHook?("after-temp-write", temp)
  verify: new PwDatabase().Open(FromPath(temp), notebook.Credentials.CompositeKey, null);
          marker == "1" and the set of (id, title, text) in its Notes group equals notebook.Notes; else → Fail(VerifyFailed, "The saved copy did not match. Your file was not changed.")
          any exception while opening temp → Fail(VerifyFailed, same message)
  TestHook?("before-replace", temp)
  if File.Exists(target): BackupRotator.ShiftOlderBackups(target); File.Replace(temp, target, BackupRotator.BackupPath(target, 1), true)
  else File.Move(temp, target)
  return Ok()
catch IOException / UnauthorizedAccessException ex → Fail(IoError, ex.Message)
finally: delete temp if it still exists (ignore errors)
```

- [x] **Step 6: Run to verify pass** → PASS.
- [x] **Step 7: Commit** — `git commit -m "Add notebook store: create, open, verified atomic write"`

---

### Task 6: NotebookStore — save and save-as

**Files:**
- Modify: `src/LockPad.Core/NotebookStore.cs`
- Test: `tests/LockPad.Tests/NotebookStoreSaveTests.cs`

**Interfaces:**
- Consumes: `NotebookStore.WriteVerifiedAndCommit`, `NotebookStore.TestHook`, `SaveResult`, `SaveStatus`, `Notebook` internals (`Database`, `Credentials`, `LoadedFingerprint`, `RemovedIds`, `MarkClean`), `FileFingerprint`, `BackupRotator`; KeePassLib `PwEntry.CreateBackup(PwDatabase)`, `PwEntry.Uuid` (settable), `PwDeletedObject(PwUuid, DateTime)`, `PwDatabase.DeletedObjects`, `MemUtil.HexStringToByteArray`
- Produces:
  - `public SaveResult Save(Notebook notebook, bool overwriteExternalChanges)`
  - `public SaveResult SaveAs(Notebook notebook, string newPath)` — on success `notebook.FilePath = full newPath`, fingerprint refreshed, clean; original file untouched.

**Security flag:** `security`

**Does NOT cover:** merging external changes (the user chooses Overwrite / Save As / Cancel in the UI — Task 12). `SaveAs` never overwrites: any existing target path returns `IoError` "File already exists.", so an unmarked file can never be overwritten through Save As.

- [x] **Step 1: Write failing tests**

```csharp
Save_RoundTripsContent()                  // titles + text "line1\r\nline2\n\tTab 😀 № \u202E RTL" survive reopen exactly
Save_ReadOnlyNotebook_ReturnsReadOnly()   // unmarked file: Status ReadOnly; file hash unchanged
Save_CreatesBackupsKeepingThree()         // 4 saves with different text → bak1..bak3 exist, bak4 not; bak1 opens with text from the 3rd save
Save_UpdatedNote_HistoryCappedAtTen()     // 12 change+save cycles → entry.History.Count == 10 (inspect via raw PwDatabase)
Save_RemovedNote_GoneAfterReopen()
Save_ExternalChange_Detected_NoWrite()    // after open, overwrite file with bytes of another valid notebook → Status ExternalChangeDetected; file hash == the external bytes
Save_ExternalChange_Overwrite_Saves()     // same, Save(nb, true) → Saved; reopen shows our content
Save_FailAfterTempWrite_OriginalIdentical() // TestHook throws IOException at "after-temp-write" → IoError; original bytes identical; no *.tmp left; nb.IsDirty
Save_VerifyMismatch_OriginalIdentical()   // TestHook at "after-temp-write" flips byte 200 of temp → VerifyFailed; original identical; nb.IsDirty
Save_ReadOnlyFileAttribute_ReturnsIoError_OriginalUntouched() // File.SetAttributes(target, ReadOnly) → IoError; bytes identical (clear attribute in teardown)
Save_LargeNote5MB_RoundTrips()            // 5_000_000-char text; save twice with an edit; reopen equals latest
Save_StaleTempFilePresent_StillSaves()    // drop "<name>.kdbx.DEADBEEF.tmp" with junk; Save → Saved; stale file untouched; Open still works
Save_KeyFileDeletedAfterOpen_StillSaves() // key-file notebook; delete key file after Open; edit; Save → Saved
SaveAs_NewPath_MovesNotebook_OriginalUnchanged()
SaveAs_ExistingTarget_ReturnsIoError()    // Message "File already exists."
```

- [x] **Step 2: Run to verify fail** — `--filter "FullyQualifiedName~NotebookStoreSaveTests"` → FAIL.

- [x] **Step 3: Implement `Save`** — order: (1) read-only → `Fail(ReadOnly, "This notebook is read-only.")`; (2) unless `overwriteExternalChanges`, `FileFingerprint.Compute(path)` must `SameContentAs(LoadedFingerprint)` else `Fail(ExternalChangeDetected, "The file was changed by another program since it was opened.")`; (3) apply changes to `Database`: for each note find entry in `Notes` group by uuid — absent → new `PwEntry(true, true)` with `Uuid = new PwUuid(MemUtil.HexStringToByteArray(id))`, add to group; present and title/text differ → `entry.CreateBackup(db)` then set fields and `Touch(true)`; Title as `ProtectedString(false, title)`, Notes as `ProtectedString(true, text)`; removed ids → remove entry, add `PwDeletedObject(uuid, DateTime.UtcNow)`; (4) `WriteVerifiedAndCommit(nb, nb.FilePath)`; (5) on `Saved`: `LoadedFingerprint = Compute(path)`, `MarkClean()`. Re-applying after a failed attempt must not add a second history entry (compare against current entry values).
- [x] **Step 4: Implement `SaveAs`** — `RequireLocalFile(newPath)`; existing → `Fail(IoError, "File already exists.")`; read-only notebook → `Fail(ReadOnly, …)`; apply changes as in Save; `WriteVerifiedAndCommit(nb, newPath)`; on success update `FilePath`, `LoadedFingerprint`, `MarkClean()`, and `Database`'s IOConnectionInfo is irrelevant (we always `SaveAs` to temp).
- [x] **Step 5: Run to verify pass** → PASS. Then full suite: `& $dotnet test LockPad.sln` → all PASS.
- [x] **Step 6: Commit** — `git commit -m "Add notebook save and save-as with backups, history and external-change detection"`

---

### Task 7: KDF tuning and password strength

**Files:**
- Create: `src/LockPad.Core/KdfTuner.cs`, `src/LockPad.Core/PasswordStrength.cs`
- Test: `tests/LockPad.Tests/KdfTunerTests.cs`, `tests/LockPad.Tests/PasswordStrengthTests.cs`

**Interfaces:**
- Consumes: KeePassLib `Argon2Kdf(Argon2Type.ID).GetBestParameters(uint)`, `QualityEstimation.EstimatePasswordBits(char[])`
- Produces:
  - `public static class KdfTuner { public const uint DefaultTargetMilliseconds = 1000; public static KdfParameters Argon2id(uint targetMilliseconds); }`
  - `public enum StrengthRating { Weak, Fair, Strong }`
  - `public static class PasswordStrength { public const uint WarnBelowBits = 80; public const uint ConfirmBelowBits = 60; public static uint EstimateBits(SecureString password); public static uint EstimateBits(char[] password); public static StrengthRating Rate(uint bits); }` — Weak `< 60`, Fair `60–79`, Strong `≥ 80`; SecureString overload copies to `char[]` and zeroes it in `finally`.

**Security flag:** `security`

- [x] **Step 1: Write failing tests**

```csharp
Argon2id_UsesIdVariantAndDefaultsMemoryParallelism() // p = KdfTuner.Argon2id(100): KdfUuid == Argon2id uuid; GetUInt64(ParamMemory,0) == 64*1024*1024; GetUInt32(ParamParallelism,0) == 2; GetUInt64(ParamIterations,0) >= 1
[TestCase(59u, StrengthRating.Weak)] [TestCase(60u, StrengthRating.Fair)] [TestCase(79u, StrengthRating.Fair)] [TestCase(80u, StrengthRating.Strong)] Rate_Boundaries
CommonPassword_IsWeak()                   // Rate(EstimateBits("password".ToCharArray())) == Weak
LongRandom_IsStrong()                     // "q7#Vt9!mZ2@xLp4$Rw8&" → Strong
SecureStringAndCharArray_Agree()
```

- [x] **Step 2: Run to verify fail** → FAIL. **Step 3: Implement.** **Step 4: Run to verify pass** → PASS.
- [x] **Step 5: Commit** — `git commit -m "Add Argon2id tuning and password strength rating"`

---

### Task 8: Settings store, clock and idle-lock policy

**Files:**
- Create: `src/LockPad.Core/AppSettings.cs`, `src/LockPad.Core/SettingsStore.cs`, `src/LockPad.Core/IClock.cs`, `src/LockPad.Core/IdleLockPolicy.cs`
- Test: `tests/LockPad.Tests/SettingsStoreTests.cs`, `tests/LockPad.Tests/IdleLockPolicyTests.cs`, `tests/LockPad.Tests/Fakes.cs` (`FakeClock`)

**Interfaces:**
- Consumes: nothing
- Produces:
  - `[DataContract] public sealed class AppSettings` with `[DataMember]` properties: `string LastNotebookPath` (null), `double FontSize` (14), `bool WordWrap` (true), `string Theme` ("System"), `int IdleLockMinutes` (5), `int ClipboardClearSeconds` (20); `public AppSettings Normalized()` clamps FontSize 8–48, IdleLockMinutes 1–60, ClipboardClearSeconds 5–120, Theme ∉ {System, Light, Dark} → "System".
  - `public sealed class SettingsStore { public SettingsStore(string directory); public static string DefaultDirectory { get; } public AppSettings Load(); public void Save(AppSettings settings); }` — file `settings.json`, `DataContractJsonSerializer`; Load returns `new AppSettings()` on missing/corrupt, always `.Normalized()`; Save writes temp then `File.Replace`/`File.Move`. `DefaultDirectory` = `%APPDATA%\LockPad`.
  - `public interface IClock { DateTime UtcNow { get; } }`, `public sealed class SystemClock : IClock`
  - `public sealed class IdleLockPolicy { public IdleLockPolicy(IClock clock, TimeSpan timeout); public TimeSpan Timeout { get; set; } public void RecordActivity(); public bool IsExpired { get; } public TimeSpan Remaining { get; } }` — `Remaining` never negative.
  - Test fake: `internal sealed class FakeClock : IClock { public DateTime UtcNow { get; set; } public void Advance(TimeSpan t); }`

**Security flag:** `none`

- [x] **Step 1: Write failing tests**

```csharp
Load_Missing_ReturnsDefaults()            // FontSize 14, WordWrap true, Theme "System", IdleLockMinutes 5, ClipboardClearSeconds 20, LastNotebookPath null
Load_Corrupt_ReturnsDefaults()            // settings.json = "{not json"
SaveThenLoad_RoundTrips()
[TestCase(0,1)] [TestCase(999,60)] Normalized_ClampsIdle(int input, int expected)
[TestCase(1,5)] [TestCase(1000,120)] Normalized_ClampsClipboard
[TestCase(2.0,8.0)] [TestCase(100.0,48.0)] Normalized_ClampsFont
Normalized_UnknownTheme_IsSystem()        // "Purple" → "System"
Idle_NotExpiredJustBeforeTimeout()        // timeout 5 min; Advance(4:59) → !IsExpired; Remaining == 1 s
Idle_ExpiredAtTimeout()                   // Advance(5:00) → IsExpired
Idle_RecordActivityResets()
Idle_RemainingNeverNegative()             // Advance(1 h) → Remaining == TimeSpan.Zero
```

- [x] **Step 2–4:** run (FAIL) → implement → run (PASS) with `--filter "FullyQualifiedName~SettingsStoreTests|FullyQualifiedName~IdleLockPolicyTests"`.
- [x] **Step 5: Commit** — `git commit -m "Add settings store and idle-lock policy"`

---

### Task 9: Protected clipboard

**Files:**
- Create: `src/LockPad.Core/ClipboardFormats.cs`, `src/LockPad.App/Services/IClipboardService.cs`, `src/LockPad.App/Services/ClipboardService.cs`
- Test: `tests/LockPad.Tests/ClipboardServiceTests.cs`

**Interfaces:**
- Consumes: `IClock` (Task 8)
- Produces:
  - `public static class ClipboardFormats { public const string ExcludeFromMonitor = "ExcludeClipboardContentFromMonitorProcessing"; public const string CanIncludeInHistory = "CanIncludeInClipboardHistory"; public const string CanUploadToCloud = "CanUploadToCloudClipboard"; public static byte[] DwordZero(); }` (returns a new `byte[4]`)
  - `public interface IClipboardService { void CopyProtected(string text); void ClearIfOwned(); }`
  - `public sealed class ClipboardService : IClipboardService { public ClipboardService(Func<int> clearSecondsProvider); internal void OnClearTimerElapsed(); }` — `CopyProtected` builds a `DataObject` with `DataFormats.UnicodeText` + the three formats (each a `MemoryStream` over `DwordZero()`), `Clipboard.SetDataObject(obj, true)` with up to 5 retries 50 ms apart on `COMException`; stores only a SHA-256 of the copied text (never the text); starts a `DispatcherTimer` for `clearSecondsProvider()` seconds. `ClearIfOwned` clears only if current clipboard text hashes to the stored hash. Empty/null text → no-op.

**Security flag:** `security`

**Does NOT cover:** clipboard managers that ignore these formats (third-party tools); paste-in (unrestricted by design).

- [x] **Step 1: Write failing tests** (class attributes `[Apartment(ApartmentState.STA)]`, `[NonParallelizable]`; note: these tests overwrite the real clipboard)

```csharp
CopyProtected_SetsTextAndExclusionFormats() // Clipboard.GetText()=="secret-1"; ContainsData for all three formats; history & cloud data bytes == {0,0,0,0}
ClearIfOwned_ClearsOwnText()              // CopyProtected("secret-2"); ClearIfOwned(); Clipboard.ContainsText() == false
ClearIfOwned_KeepsForeignText()           // CopyProtected("secret-3"); Clipboard.SetText("other"); ClearIfOwned(); GetText()=="other"
TimerElapsed_ClearsOwnText()              // CopyProtected("secret-4"); OnClearTimerElapsed(); !ContainsText()
CopyProtected_Empty_NoOp()                // Clipboard.SetText("keep"); CopyProtected(""); GetText()=="keep"
```

- [x] **Step 2–4:** FAIL → implement → PASS (`--filter "FullyQualifiedName~ClipboardServiceTests"`).
- [x] **Step 5: Commit** — `git commit -m "Add protected clipboard service"`

---

### Task 10: App shell, MVVM infrastructure, dialogs, theme

**Files:**
- Create: `src/LockPad.App/Infrastructure/ObservableObject.cs`, `Infrastructure/RelayCommand.cs`, `Services/IDialogService.cs`, `Services/DialogService.cs`, `Services/ThemeService.cs`, `Themes/Light.xaml`, `Themes/Dark.xaml`, `ViewModels/ShellViewModel.cs`, `Views/PromptWindow.xaml(.cs)`
- Modify: `src/LockPad.App/App.xaml(.cs)`, `src/LockPad.App/MainWindow.xaml(.cs)`
- Test: `tests/LockPad.Tests/InfrastructureTests.cs`, `tests/LockPad.Tests/ThemeServiceTests.cs`, add `FakeDialogService` to `Fakes.cs`

**Interfaces:**
- Consumes: `AppSettings`, `SettingsStore`
- Produces:
  - `public abstract class ObservableObject : INotifyPropertyChanged { protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string name = null); protected void OnPropertyChanged(string name); }`
  - `public sealed class RelayCommand : ICommand { public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null); public void RaiseCanExecuteChanged(); }`
  - `public enum ExternalChangeChoice { Overwrite, SaveAs, Cancel }`
  - `public interface IDialogService { void ShowError(string title, string message); void ShowWarning(string title, string message); bool Confirm(string title, string message); ExternalChangeChoice AskExternalChange(string path); string PickOpenNotebookPath(); string PickNewNotebookPath(); string PickSaveAsPath(string currentPath); string PickKeyFile(bool forCreate); string Prompt(string title, string label, string initialValue); }` — pickers/prompt return null on cancel. File filters: `LockPad notebook (*.kdbx)|*.kdbx|All files (*.*)|*.*`; key files `Key files (*.keyx;*.key)|*.keyx;*.key|All files (*.*)|*.*`. `PickNewNotebookPath`/`PickSaveAsPath` use `SaveFileDialog` with `OverwritePrompt = false` and `CheckFileExists = false` (Core refuses existing targets).
  - `public enum AppTheme { System, Light, Dark }`; `public static class ThemeService { public static bool SystemPrefersDark(object appsUseLightThemeValue); public static void Apply(AppTheme theme); }` — registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`: `0` → dark; `1`, missing or non-int → light. `Apply` swaps the merged `Themes/Light.xaml`/`Dark.xaml` dictionary (brush keys: `WindowBackground`, `PanelBackground`, `EditorBackground`, `EditorForeground`, `AccentBrush`, `BorderBrush`, `MutedForeground`).
  - `public sealed class ShellViewModel : ObservableObject` — ctor `(NotebookStore store, SettingsStore settingsStore, AppSettings settings, IDialogService dialogs, IClipboardService clipboard, IClock clock, Func<KdfParameters> kdfFactory)` (stores dependencies only); `public object CurrentView { get; }`. Page methods are added in Tasks 11–14. `App.OnStartup` constructs it with `new NotebookStore()`, `new SettingsStore(SettingsStore.DefaultDirectory)`, `new DialogService()`, `new ClipboardService(() => settings.ClipboardClearSeconds)`, `new SystemClock()`, `() => KdfTuner.Argon2id(KdfTuner.DefaultTargetMilliseconds)`.
  - Test fake: `internal sealed class FakeDialogService : IDialogService` — queues of scripted answers per method, records calls (`List<string> Calls`).

**Security flag:** `none`

- [x] **Step 1: Write failing tests**

```csharp
SetProperty_RaisesOnlyOnChange()          // same value → no event, returns false; new value → one event with property name
RelayCommand_CanExecuteRespectsPredicate()
[TestCase(0, true)] [TestCase(1, false)] [TestCase(null, false)] [TestCase("x", false)] SystemPrefersDark(object v, bool expected)
```

- [x] **Step 2–4:** FAIL → implement → PASS.
- [x] **Step 5: Build check** — `& $dotnet build LockPad.sln` → `0 Error(s)`. `MainWindow` hosts `ContentControl Content="{Binding CurrentView}"` with `DataTemplate`s (added per view in later tasks); `App.OnStartup` loads settings, applies theme, creates `ShellViewModel`.
- [x] **Step 6: Commit** — `git commit -m "Add MVVM infrastructure, dialogs, theme and shell"`

---

### Task 11: Unlock and create-notebook flows

**Files:**
- Create: `src/LockPad.App/ViewModels/UnlockViewModel.cs`, `ViewModels/CreateNotebookViewModel.cs`, `Views/UnlockView.xaml(.cs)`, `Views/CreateNotebookView.xaml(.cs)`
- Modify: `src/LockPad.App/ViewModels/ShellViewModel.cs`, `MainWindow.xaml` (DataTemplates)
- Test: `tests/LockPad.Tests/UnlockViewModelTests.cs`, `tests/LockPad.Tests/CreateNotebookViewModelTests.cs`

**Interfaces:**
- Consumes: `NotebookStore.Create/Open`, `Credentials.FromSecureString`, `Credentials.CreateKeyFile`, `Notebook.CredentialsMatch`, `Notebook.IsReadOnly`, `WrongCredentialsException`, `NotebookFormatException`, `PasswordStrength`, `StrengthRating`, `SettingsStore`, `AppSettings`, `IDialogService`, `ObservableObject`, `RelayCommand`
- Produces:
  - `public sealed class UnlockViewModel : ObservableObject`
    - ctor `(NotebookStore store, SettingsStore settingsStore, AppSettings settings, IDialogService dialogs, Action<Notebook> onOpened, Action showCreate, Notebook heldNotebook = null, string heldWarning = null)`
    - `string NotebookPath`, `string KeyFilePath`, `string ErrorMessage`, `bool IsBusy`, `string HeldWarning`
    - `ICommand BrowseNotebookCommand`, `ICommand BrowseKeyFileCommand`, `ICommand NewNotebookCommand`
    - `public Task UnlockAsync(SecureString password)` — held notebook: `CredentialsMatch` → `onOpened(held)` else `ErrorMessage = "Wrong password or key file."`; otherwise `store.Open` on `Task.Run`, success → save `LastNotebookPath`, if read-only `dialogs.ShowWarning("Read-only", <read-only string>)`, `onOpened(nb)`. Errors: WrongCredentials → `Wrong password or key file.`; FileNotFound → `File not found.`; NotebookFormat → `This file is not a valid notebook or KeePass database.`; IOException → its message. Credentials are disposed on failure.
  - `public sealed class CreateNotebookViewModel : ObservableObject`
    - ctor `(NotebookStore store, IDialogService dialogs, Func<KdfParameters> kdfFactory, Action<Notebook> onCreated, Action cancel)`
    - `string NotebookPath`, `string KeyFilePath`, `bool CreateNewKeyFile`, `uint StrengthBits`, `StrengthRating Strength`, `string StrengthWarning`, `string ErrorMessage`, `bool IsBusy`
    - `public void UpdateStrength(SecureString password)` — sets bits/rating; `StrengthWarning` = `"Consider a longer password (aim for 80+ bits)."` when Fair, `"Weak password."` when Weak, else null.
    - `public Task CreateAsync(SecureString password, SecureString confirm)` — empty → `ErrorMessage = "Enter a password."`; mismatch (compare via zeroed `char[]` copies) → `Passwords do not match.`; path exists → `File already exists.`; Weak → `dialogs.Confirm("Weak password", "This password is weak ({bits} bits). Anyone who gets this file could guess it. Use it anyway?")` must be true; `CreateNewKeyFile` → `Credentials.CreateKeyFile(KeyFilePath)` first; `store.Create(path, creds, kdfFactory())` on `Task.Run`; success → `onCreated(nb)`.
  - `ShellViewModel` additions: `public void ShowUnlock(Notebook held = null, string heldWarning = null)`; `public void ShowCreate()`. The `onOpened`/`onCreated` callbacks are wired to `ShowEditor`, which Task 12 adds; Task 11 tests assert only the callbacks.
  - Unlock pre-fill: `NotebookPath` starts as `settings.LastNotebookPath` when that file exists, else empty.
  - Views: `UnlockView` (path text + Browse, `PasswordBox` — Enter key triggers unlock, key file + Browse, "New notebook…", error text, held warning banner); `CreateNotebookView` (path + Browse, two `PasswordBox`es, strength bar bound to `StrengthBits` 0–128, warning text, optional key file / "create new key file" checkbox). Code-behind passes `PasswordBox.SecurePassword` to the VM methods and calls `PasswordBox.Clear()` afterwards.
  - Production `kdfFactory` = `() => KdfTuner.Argon2id(KdfTuner.DefaultTargetMilliseconds)`; tests pass `TestKdf.Fast`.

**Security flag:** `security`

- [x] **Step 1: Write failing tests** (temp dir, fast KDF, `FakeDialogService`; `SecureString` helper `Secure(string)`)

```csharp
Unlock_PrefillsExistingLastPath_IgnoresMissing() // settings.LastNotebookPath existing → NotebookPath equals it; nonexistent → ""
Unlock_Correct_CallsOnOpened_SavesLastPath()
Unlock_Wrong_SetsError_NoCallback()       // ErrorMessage == "Wrong password or key file."
Unlock_Missing_SetsFileNotFound()         // "File not found."
Unlock_Unmarked_OpensReadOnly_Warns()     // dialogs.Calls contains "ShowWarning"; callback notebook IsReadOnly
Unlock_Held_RightPassword_ReturnsSameInstance()
Unlock_Held_WrongPassword_SetsError()
Create_Mismatch_SetsError_NoFile()        // "Passwords do not match."; !File.Exists
Create_Weak_Declined_NoFile()             // Confirm scripted false → no file
Create_Weak_Accepted_Creates()
Create_Strong_CreatesAndCallsBack()       // StrengthBits >= 80 for "q7#Vt9!mZ2@xLp4$Rw8&"; file exists; callback called
Create_ExistingPath_SetsError()           // "File already exists."
Create_NewKeyFile_RequiredToReopen()      // CreateNewKeyFile=true → reopening without key throws WrongCredentialsException
```

- [x] **Step 2–4:** FAIL → implement → PASS (`--filter "FullyQualifiedName~UnlockViewModelTests|FullyQualifiedName~CreateNotebookViewModelTests"`).
- [x] **Step 5: Commit** — `git commit -m "Add unlock and create-notebook flows"`

---

### Task 12: Editor — notes list, editing, save, clipboard routing

**Files:**
- Create: `src/LockPad.App/ViewModels/EditorViewModel.cs`, `ViewModels/NoteItemViewModel.cs`, `Views/EditorView.xaml(.cs)`, `Services/TextBoxClipboardGuard.cs`
- Modify: `ShellViewModel.cs` (`ShowEditor`), `MainWindow.xaml` (DataTemplate, status bar), `App.xaml.cs` (register guard)
- Test: `tests/LockPad.Tests/EditorViewModelTests.cs`, add `FakeClipboardService` to `Fakes.cs` (`internal sealed class FakeClipboardService : IClipboardService { public string Copied; public int ClearCount; }`)

**Interfaces:**
- Consumes: `Notebook` (all public members), `NotebookStore.Save/SaveAs`, `SaveResult`, `SaveStatus`, `IDialogService`, `ExternalChangeChoice`, `IClipboardService`, `SettingsStore`, `AppSettings`, `ObservableObject`, `RelayCommand`
- Produces:
  - `public sealed class NoteItemViewModel : ObservableObject { public string Id { get; } public string DisplayTitle { get; } }` — `DisplayTitle` = title, or `(untitled)` when title is empty/whitespace.
  - `public sealed class EditorViewModel : ObservableObject`
    - ctor `(Notebook notebook, NotebookStore store, IDialogService dialogs, IClipboardService clipboard, SettingsStore settingsStore, AppSettings settings)`
    - `public Notebook Notebook { get; }`, `ObservableCollection<NoteItemViewModel> VisibleNotes`, `NoteItemViewModel SelectedNote`, `string EditorText`, `string FilterText`, `double FontSize`, `bool WordWrap`, `bool IsReadOnly`, `string StatusText` (`Saved` / `Unsaved changes` / `Read-only`)
    - Commands: `NewNoteCommand` (title `New note`, selects it), `RenameNoteCommand` (`dialogs.Prompt("Rename note", "Title:", current)`), `DeleteNoteCommand` (`dialogs.Confirm("Delete note", $"Delete \"{DisplayTitle}\"? LockPad cannot undo this. The previous saved version stays in the .bak backup files.")`), `SaveCommand`, `ZoomInCommand`, `ZoomOutCommand`, `ZoomResetCommand`, `ToggleWordWrapCommand` — New/Rename/Delete/Save disabled when `IsReadOnly`.
    - `public SaveResult SaveNow()` — `store.Save(nb, false)`; on `ExternalChangeDetected` ask `dialogs.AskExternalChange(path)`: Overwrite → `store.Save(nb, true)`; SaveAs → `PickSaveAsPath` then `store.SaveAs` (null path → treat as Cancel); Cancel → return the ExternalChangeDetected result. Any final non-Saved status (other than user Cancel) → `dialogs.ShowError("Save failed", result.Message)`.
    - `public void CopySelection(string selectedText)` → `clipboard.CopyProtected(selectedText)`
  - Behaviour: `EditorText` setter → `notebook.UpdateNote(selectedId, title, value)`; filter = case-insensitive ordinal substring of title or text; zoom step 1 pt within 8–48, reset 14, persisted via `settingsStore.Save`; word wrap persisted.
  - `public static class TextBoxClipboardGuard { public static void Register(IClipboardService clipboard); }` — `CommandManager.RegisterClassCommandBinding(typeof(TextBox), …)` for `ApplicationCommands.Copy` and `ApplicationCommands.Cut` → `CopyProtected(SelectedText)` (Cut also clears the selection when not read-only); `EventManager.RegisterClassHandler(typeof(TextBox), DataObject.CopyingEvent, …)` cancelling drag-drop copies (`e.IsDragDrop` → `e.CancelCommand()`). Covers context-menu Copy/Cut too. `PasswordBox` is untouched (WPF blocks copying from it).
  - `EditorView`: two-column `Grid` with `GridSplitter`; sidebar = filter `TextBox`, `ListBox` of `VisibleNotes`, New/Rename/Delete buttons; editor `TextBox` with `FontFamily="Cascadia Mono, Consolas"`, `AcceptsReturn`, `AcceptsTab`, `SpellCheck.IsEnabled="False"`, `TextWrapping` bound to `WordWrap`, both scrollbars Auto, `IsReadOnly` bound; Ctrl+wheel → zoom; Ctrl+F opens a find bar (case-insensitive, Enter = next match, wraps around); key bindings Ctrl+N, Ctrl+S, F2, Del (list focused), Ctrl+Plus/Minus/0.
  - `ShellViewModel.ShowEditor(Notebook nb)` creates the `EditorViewModel`; exposes `public EditorViewModel Editor { get; }` (null when locked).

**Security flag:** `security` (clipboard routing, drag-out)

**Does NOT cover:** find-and-replace; multiple selection; rich text.

- [x] **Step 1: Write failing tests**

```csharp
NewNote_AddsSelectsAndMarksDirty()        // VisibleNotes.Count +1; SelectedNote.DisplayTitle == "New note"; StatusText == "Unsaved changes"
EditingText_UpdatesNotebook()             // EditorText = "abc" → notebook.FindNote(id).Text == "abc"
Filter_MatchesTitleOrTextIgnoringCase()   // notes ("Ledger","x") ("Bank","seed WORDS") → FilterText "words" → only Bank; "LEDGER" → only Ledger
NoteItem_EmptyTitle_DisplaysUntitled()
Delete_Declined_KeepsNote() / Delete_Confirmed_RemovesNote()
Save_Success_StatusSaved()
Save_ExternalChange_Overwrite_Saves()     // AskExternalChange scripted Overwrite → Saved; reopen shows our edit
Save_ExternalChange_Cancel_StaysDirty()   // no ShowError call; StatusText "Unsaved changes"
Save_Failure_ShowsError()                 // read-only file attribute → dialogs.Calls contains "ShowError"
ReadOnly_CommandsDisabled()               // New/Rename/Delete/Save CanExecute == false; IsReadOnly == true; StatusText "Read-only"
Zoom_ClampsAndPersists()                  // 50 × ZoomIn → FontSize 48; settingsStore.Load().FontSize == 48; ZoomReset → 14
CopySelection_UsesProtectedClipboard()    // FakeClipboard.Copied == "secret"
```

- [x] **Step 2–4:** FAIL → implement → PASS (`--filter "FullyQualifiedName~EditorViewModelTests"`).
- [x] **Step 5: Build + launch smoke** — `& $dotnet build LockPad.sln` → `0 Error(s)`; run `src\LockPad.App\bin\Debug\net48\LockPad.exe`, create a notebook in `%TEMP%`, add a note, Ctrl+S, close; then close the process.
- [x] **Step 6: Commit** — `git commit -m "Add editor with notes list, save flow and protected copy"`

---

### Task 13: Settings dialog and theme menu

**Files:**
- Create: `src/LockPad.App/ViewModels/SettingsViewModel.cs`, `src/LockPad.App/Views/SettingsWindow.xaml(.cs)`
- Modify: `src/LockPad.App/MainWindow.xaml(.cs)` (menu bar), `ShellViewModel.cs`, `Services/IDialogService.cs` + `DialogService.cs` (add `bool ShowSettings(SettingsViewModel vm)`), `tests/LockPad.Tests/Fakes.cs` (FakeDialogService implements it: returns a scripted bool)
- Test: `tests/LockPad.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `AppSettings`, `AppSettings.Normalized`, `SettingsStore`, `AppTheme`, `ThemeService.Apply`, `IDialogService`, `ObservableObject`, `RelayCommand`
- Produces:
  - `public sealed class SettingsViewModel : ObservableObject { public SettingsViewModel(AppSettings current); public int IdleLockMinutes { get; set; } public int ClipboardClearSeconds { get; set; } public AppTheme Theme { get; set; } public AppSettings ApplyTo(AppSettings target); }` — `ApplyTo` copies the three values into a copy of `target` and returns `.Normalized()`.
  - `IDialogService.ShowSettings(SettingsViewModel vm)` → true when the user pressed OK.
  - `ShellViewModel`: `public AppSettings Settings { get; }`, `public event EventHandler SettingsChanged`, `public void OpenSettings()` (OK → save via `settingsStore`, `ThemeService.Apply`, raise `SettingsChanged`), `public void SetTheme(AppTheme theme)` (persist + apply).
  - Menu bar in `MainWindow`: `View → Theme → System / Light / Dark` (checked item reflects current theme) and `Tools → Settings…`. `SettingsWindow`: two numeric `TextBox`es (digits only) with labels `Lock after idle (minutes, 1–60)` and `Clear clipboard after (seconds, 5–120)`, theme `ComboBox`, OK / Cancel.

**Security flag:** `none`

- [x] **Step 1: Write failing tests**

```csharp
ApplyTo_CopiesValuesAndClamps()           // IdleLockMinutes 0 → 1; ClipboardClearSeconds 500 → 120; Theme Dark → "Dark"; target instance itself unchanged
OpenSettings_Ok_PersistsAndRaisesEvent()  // FakeDialogService.ShowSettings scripted true after vm.IdleLockMinutes = 2 → settingsStore.Load().IdleLockMinutes == 2; SettingsChanged raised once
OpenSettings_Cancel_ChangesNothing()      // scripted false → settingsStore.Load() equals the previous values; no event
SetTheme_Persists()                       // SetTheme(AppTheme.Light) → settingsStore.Load().Theme == "Light"
```

- [x] **Step 2–4:** FAIL → implement → PASS (`--filter "FullyQualifiedName~SettingsViewModelTests"`). `ThemeService.Apply` must be a no-op when `Application.Current` is null (tests).
- [x] **Step 5: Commit** — `git commit -m "Add settings dialog and theme menu"`

---

### Task 14: Auto-lock, manual lock and close behaviour

**Files:**
- Modify: `src/LockPad.App/ViewModels/ShellViewModel.cs`, `MainWindow.xaml(.cs)`
- Test: `tests/LockPad.Tests/ShellLockTests.cs` (uses `FakeClipboardService` from Task 12)

**Interfaces:**
- Consumes: `IdleLockPolicy`, `IClock`, `NotebookStore.Save`, `SaveResult`, `EditorViewModel.SaveNow`, `EditorViewModel.Notebook`, `ShellViewModel.ShowUnlock(Notebook, string)`, `UnlockViewModel.HeldWarning`, `IClipboardService.ClearIfOwned`, `IDialogService`
- Produces (on `ShellViewModel`):
  - `public void RecordActivity()`
  - `public void Tick()` — called every second by a `DispatcherTimer` in `MainWindow`; locks when an editor is open and `IdleLockPolicy.IsExpired`; updates `LockCountdownText` (`Locks in m:ss`, empty when locked).
  - `public void Lock()` — no editor → no-op. Clean or read-only → `ClearIfOwned()`, dispose notebook, `ShowUnlock()`. Dirty → `store.Save(nb, false)` (no dialogs while the user may be away): `Saved` → as clean; otherwise keep the notebook, drop the editor VM, `ShowUnlock(nb, $"Unsaved changes are held in memory. Saving failed: {result.Message}")`, `ClearIfOwned()`.
  - `public bool TryClose()` — no editor and no held notebook → true. Held notebook on unlock screen → `dialogs.Confirm("Unsaved changes", "Unsaved changes are held in memory because saving failed. Close anyway and lose them?")`. Editor clean/read-only → dispose, `ClearIfOwned()`, true. Editor dirty → `editor.SaveNow()` (user present, so the external-change dialog is allowed); `Saved` → dispose, true; else `dialogs.ShowError("Could not save", result.Message + " The window stays open so nothing is lost.")`, false.
  - `public string LockCountdownText { get; }`
  - `MainWindow`: `PreviewKeyDown`, `PreviewMouseDown`, `PreviewMouseMove`, `PreviewMouseWheel` → `RecordActivity()`; Ctrl+L → `Lock()`; `Closing` → `e.Cancel = !vm.TryClose()`; idle timeout from `AppSettings.IdleLockMinutes`, updated on `SettingsChanged` (Task 13).

**Security flag:** `security`

**Does NOT cover:** locking on Windows lock/sleep or minimize (user chose idle-only — spec non-goal); input in other applications does not count as activity.

- [x] **Step 1: Write failing tests** (FakeClock, FakeDialogService, FakeClipboardService, temp files, fast KDF)

```csharp
Tick_BeforeTimeout_DoesNotLock()          // Advance(4:59); Tick() → Editor != null
Tick_AfterTimeout_Clean_LocksAndDisposes() // Advance(5:00); Tick() → Editor == null; CurrentView is UnlockViewModel with HeldWarning == null; ClearCount == 1
Lock_Dirty_SavesThenLocks()               // edit, Lock() → reopen file contains edit; no held notebook
Lock_SaveFails_HoldsNotebookWithWarning() // file ReadOnly attribute; edit; Lock() → UnlockViewModel.HeldWarning starts with "Unsaved changes are held in memory. Saving failed:"
Lock_SaveFails_UnlockRestoresEdit()       // then UnlockAsync(correct) → Editor.Notebook.FindNote(id).Text == edited text
Lock_ExternalChange_HoldsWithoutDialog()  // external overwrite; edit; Lock() → dialogs.Calls has no "AskExternalChange"; held warning present
RecordActivity_ResetsTimer()              // Advance(4:00); RecordActivity(); Advance(4:00); Tick() → still unlocked
SettingsChanged_UpdatesIdleTimeout()      // settings → IdleLockMinutes 1 via OpenSettings (scripted OK); Advance(1:00); Tick() → locked
TryClose_Dirty_Saves_ReturnsTrue()
TryClose_SaveFails_ReturnsFalse_ShowsError()
TryClose_ReadOnlyNotebook_ReturnsTrue_NoSave() // file bytes unchanged
TryClose_HeldNotebook_AsksConfirm()       // Confirm scripted false → TryClose() == false
```

- [x] **Step 2–4:** FAIL → implement → PASS (`--filter "FullyQualifiedName~ShellLockTests"`).
- [x] **Step 5: Full suite** — `& $dotnet test LockPad.sln` → all PASS.
- [x] **Step 6: Commit** — `git commit -m "Add auto-lock, manual lock and safe close"`

---

### Task 15: KeePassXC interoperability and manual checklist

**Files:**
- Test: `tests/LockPad.Tests/KeePassXcInteropTests.cs`
- Create: `docs/manual-test-checklist.md`

**Interfaces:**
- Consumes: `NotebookStore.Create/Open/Save`, `Credentials`, `Notebook`, `TestKdf.Fast`, `TempDir`
- Produces: nothing

**Security flag:** `none`

**Does NOT cover:** KeePassXC versions other than the installed 2.7.12; KeePassDX/Strongbox (manual only).

- [x] **Step 1: Write tests** (each starts with `if (!File.Exists(Cli)) Assert.Ignore("keepassxc-cli not installed");`, `Cli = @"C:\Program Files\KeePassXC\keepassxc-cli.exe"`; password supplied on stdin; process timeout 30 s)

```csharp
KeePassXc_ReadsLockPadNote()              // notebook password "Interop-Test-Pw-1!", note "Ledger seed" text "alpha beta\ngamma №";
                                          // run: show -q -s -a Notes <db> "Notes/Ledger seed" → stdout.TrimEnd('\r','\n') == text (normalize \r\n → \n)
KeePassXc_Edit_ThenLockPadOpens_StillMarked() // run: edit -q --notes "changed by kpxc" <db> "Notes/Ledger seed" → LockPad Open: IsReadOnly == false; note text == "changed by kpxc"
```

- [x] **Step 2: Run** — `& $dotnet test tests/LockPad.Tests --filter "FullyQualifiedName~KeePassXcInteropTests"` → Expected: 2 passed. If `KeePassXc_Edit_ThenLockPadOpens_StillMarked` fails because the marker was dropped, STOP and report (the read-only fallback is safe, but the design assumption needs a user decision).

- [x] **Step 3: Write `docs/manual-test-checklist.md`** with this exact content:

```markdown
# LockPad manual test checklist

Run with a throwaway notebook in %TEMP%. Tick each item.

## Create / unlock
- [ ] New notebook: mismatched passwords show "Passwords do not match."
- [ ] New notebook: weak password ("password") asks for confirmation; declining creates no file.
- [ ] New notebook with a strong password opens straight into the editor.
- [ ] Restart app: the last notebook path is prefilled; only the password is needed.
- [ ] Wrong password shows "Wrong password or key file."
- [ ] Opening a KeePassXC password database shows the read-only warning; editing and saving are disabled.

## Editor
- [ ] Paste 200 lines; text is monospace, readable, word wrap toggles.
- [ ] Ctrl+Plus / Ctrl+Minus / Ctrl+0 and Ctrl+wheel zoom; size survives restart.
- [ ] Ctrl+F finds text and wraps around.
- [ ] Filter box narrows the notes list by title and content.
- [ ] Rename (F2) and delete (Del, with confirmation) work.
- [ ] Dragging selected text out of the editor into another app does nothing.

## Saving and safety
- [ ] Ctrl+S → status shows "Saved"; .bak1 appears after the second save.
- [ ] Edit the file in KeePassXC while open in LockPad, then Ctrl+S → Overwrite / Save As / Cancel prompt.
- [ ] Set the .kdbx file read-only, edit, Ctrl+S → error shown, file unchanged.
- [ ] Close the window with unsaved edits → saved automatically.
- [ ] With the file read-only and unsaved edits, closing shows "Could not save" and the window stays open.

## Lock and clipboard
- [ ] Set idle lock to 1 minute; leave the app; it locks and the editor is cleared.
- [ ] Ctrl+L locks immediately.
- [ ] Copy text from a note; press Win+V: the copied text does NOT appear in clipboard history.
- [ ] After 20 seconds the clipboard is empty (paste gives nothing).
- [ ] Copy something in another app after copying from LockPad: LockPad does not clear it.

## Interop
- [ ] Open the notebook in KeePassXC with the same password: the "Notes" group shows every note.
```

- [x] **Step 4: Commit** — `git commit -m "Add KeePassXC interop tests and manual checklist"`

---

### Task 16: Security review and red-team attack (user-mandated)

**Files:**
- Create: `docs/reviews/2026-10-15-security-and-red-team.md`
- Modify: any `src/` or `tests/` file a confirmed finding requires

**Interfaces:**
- Consumes: the complete codebase from Tasks 1–15
- Produces: the review record; fixes with regression tests

**Security flag:** `security`

- [x] **Step 1: Security review** — dispatch the `superpowers-optimized:code-reviewer` agent on all of `src/`, `lib/KeePassLib.csproj`, `nuget.config`, `Directory.Build.props`, with the spec path, focused on: credential handling and zeroing, KDF/cipher settings, file-write safety, path handling, clipboard, dependency/supply-chain.
- [x] **Step 2: Red-team attack** — dispatch the `superpowers-optimized:red-team` agent on all of `src/` and `tests/` with the spec path and this brief: "Content is PINs, seed phrases and private keys. Data loss is as severe as disclosure. Try to break: the save pipeline (crash points, external changes, OneDrive/reparse/read-only/locked files, stale temp files, huge notes), lock/unlock state transitions (held notebook, save failures, timer races, close during save), credential handling (Unicode, key files moved), clipboard ownership/clearing, read-only enforcement for unmarked files, settings corruption."
- [x] **Step 3: Fix loop** — for every Critical or High finding from either review: (a) write a failing test in the owning test file reproducing the exact trigger, (b) run it → FAIL, (c) fix, (d) run full suite `& $dotnet test LockPad.sln` → all PASS, (e) commit `Fix: <finding title>`. Medium findings: fix if the change is contained to one unit; otherwise list for the user. False positives: record why.
- [x] **Step 4: Re-attack** — re-dispatch `superpowers-optimized:red-team` on the files changed in Step 3. Repeat Steps 3–4 until no Critical/High remain, maximum 3 rounds; if any remain after round 3, STOP and report to the user.
- [x] **Step 5: Write the review record** `docs/reviews/2026-10-15-security-and-red-team.md`: one row per finding — source (security/red-team), severity, title, disposition (fixed + test name / accepted + reason / false positive + reason).
- [x] **Step 6: Commit** — `git commit -m "Record security review and red-team results"`

---

### Task 17: Final verification and handover

**Files:**
- Modify: `project-map.md`, `session-log.md`

**Interfaces:**
- Consumes: everything
- Produces: Release build at `src\LockPad.App\bin\Release\net48\` (`LockPad.exe`, `LockPad.Core.dll`, `KeePassLib.dll`)

**Security flag:** `none`

- [x] **Step 1: Clean build + tests** — `& $dotnet build LockPad.sln -c Release` → `0 Warning(s)`, `0 Error(s)`; `& $dotnet test LockPad.sln -c Release` → all PASS (interop tests passed, not ignored).
- [x] **Step 2: Guard re-check** — `RepositoryGuardTests` pass (KeePassLib unmodified, no network APIs).
- [x] **Step 3: Manual checklist** — walk through `docs/manual-test-checklist.md` with the user; record any failure as a bug and fix via `systematic-debugging` before handover.
- [x] **Step 4: Update memory files** — `project-map.md`: directory structure, key files (NotebookStore, Notebook, Credentials, ShellViewModel, EditorViewModel, ClipboardService, TextBoxClipboardGuard), hot files, constraints; `session-log.md`: `[saved]` entry for the build.
- [x] **Step 5: Commit** — `git commit -m "LockPad core: final verification and project map"`
