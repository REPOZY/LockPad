# LockPad Core — Design

_Status: approved 2026-10-15 · Sub-project 1 of 2 (sub-project 2 = legacy Crypto Notepad `.cnp` importer, separate spec)_

## Purpose

A Windows notepad for highly sensitive text (private keys, seed phrases, PINs). Notebooks are standard KDBX4 files, so official KeePassXC can always open them as a fallback. Replaces Crypto Notepad, whose flaws (non-atomic saves, weak KDF, no integrity check, mangled passwords, clipboard leaks) this design explicitly addresses.

## Scope

In scope (sub-project 1):
- Create a notebook (file path + master password, optional key file).
- Open/unlock a notebook; reopens last notebook directly at the unlock screen.
- Notes list: add, rename, delete (with confirmation), filter by title and text.
- Full-height plain-text editor: monospace font, zoom, word wrap, find-in-note.
- Save pipeline: change-on-disk check, rotating backups, atomic write, reopen-verify, entry history.
- Auto-lock after idle; auto-save on lock and on close.
- Protected clipboard.
- Light/dark theme following Windows, with manual override.

Non-goals:
- Sync, cloud, mobile, network access of any kind, auto-updater, plugins.
- Markdown or rich text.
- Multiple windows / multiple notebooks open at once.
- Recycle bin / undelete UI (recovery = backups + entry history).
- Locking on Windows lock/sleep or on minimize (user chose idle-only; not built). (superseded, see Amendments; locking on minimize is still not built)
- Protection against malware already running on the PC (keyloggers, memory readers). No notepad can provide this.
- Importing `.cnp` files (sub-project 2).

## Platform and toolchain

- C# targeting .NET Framework 4.8, WPF.
- Built with the .NET 10 SDK (SDK-style projects; `Microsoft.NETFramework.ReferenceAssemblies` NuGet package supplies net48 reference assemblies). SDK installed user-local at `%LOCALAPPDATA%\Microsoft\dotnet`, not on PATH.
- KeePassLib: official source from the KeePass 2.61.x source package (keepass.info), SHA-256 verified against the published hash, vendored **unmodified** in `lib/KeePassLib/`, pinned to one release. Updating = replacing the folder with a newer official release.
- **Gate:** if KeePassLib cannot be built standalone from the official source without modification, stop and return to the user. Do not patch it.

## Architecture

```
LockPad.sln
├─ lib/KeePassLib/          official, unmodified, pinned
├─ src/LockPad.Core/        no UI; storage, crypto params, save pipeline, clipboard formats
├─ src/LockPad.App/         WPF; views + view-models + platform services
└─ tests/LockPad.Tests/     unit + integration tests for Core (and testable App services)
```

Dependency direction: `App → Core → KeePassLib`. Core has no WPF reference.

### Core units

| Unit | Responsibility |
|---|---|
| `Credentials` | Holds password as UTF-8 bytes (zeroed on dispose) + optional key-file path; builds a KeePassLib `CompositeKey`. |
| `Note` | `Id` (entry UUID), `Title`, `Text`, `LastModified`. |
| `Notebook` | In-memory notebook: notes collection, `IsReadOnly`, `IsDirty`, file path, file fingerprint at load time. |
| `NotebookStore` | `Create(path, credentials)`, `Open(path, credentials)`, `Save(notebook)`. Wraps KeePassLib `PwDatabase`. Local paths only. |
| `KdfTuner` | Picks Argon2id iterations so key derivation takes about 1 s on this machine (memory 64 MiB, parallelism 2). |
| `BackupRotator` | Copies current file to `name.kdbx.bak1`, shifting `bak1→bak2→bak3`, dropping the oldest. (superseded, see Amendments: one `name.kdbx.bak`, no rotator) |
| `FileFingerprint` | Size + last-write time + SHA-256 of file bytes; detects external changes. |
| `PasswordStrength` | Wraps KeePassLib `QualityEstimation` → bits + rating. |
| `ClipboardFormats` | Builds the data object with history/cloud exclusion formats (platform-neutral description; WPF service applies it). |

### File format mapping

- Standard KDBX, cipher AES-256, KDF Argon2id (tuned as above). Written as **KDBX 4.1**: KeePassLib picks 4.1 automatically because the database custom-data marker carries a modification time (`KdbxFile.GetMinKdbxVersion`). KeePassXC 2.7.12 supports 4.1 (`src/format/KeePass2.h:33`); the `keepassxc-cli` interop test confirms it. No other 4.1-only features (tags etc.) are used. _(Amended 2026-10-15 after source review.)_
- Group `Notes` under the root group holds one `PwEntry` per note.
  - `Title` field ← note title.
  - `Notes` field ← note text. Database `MemoryProtection.ProtectNotes = true` so notes are protected in memory and in the inner XML.
- Marker: database `CustomData["LockPad.Notebook"] = "1"` (KeePassLib/KeePassXC preserve database custom data).
- Opening a file **without** the marker: opens **read-only** with a warning ("This file was not created by LockPad — it may be your password database. It is opened read-only."). Read-only notebooks never save, never auto-save.
- Entry history: before modifying a changed note's entry on save, call `CreateBackup`; history limited to 10 items per entry.

### Save pipeline (single code path for Ctrl+S, auto-save on lock, auto-save on close)

1. If notebook is read-only → refuse.
2. Compute current `FileFingerprint`; if it differs from the load-time fingerprint → return `ExternalChangeDetected`; UI asks: Overwrite / Save As / Cancel.
3. Apply notes to the `PwDatabase` (create/update/delete entries; history for updated ones).
4. Write the database to a new temp file in the **same directory** (`<name>.kdbx.<random>.tmp`) via `PwDatabase.SaveAs(temp, false, null)`, then flush it to disk (`FileStream.Flush(true)`).
5. Reopen the **temp file** with the same credentials; compare every note (id, title, text) with the in-memory notebook. On mismatch → delete temp, return `VerifyFailed`. The real file has not been touched.
6. Shift backups (`bak2→bak3`, `bak1→bak2`, oldest dropped), then atomically swap: `File.Replace(temp, target, target + ".bak1")` (first save of a new file: `File.Move(temp, target)`). (superseded, see Amendments: no shift; `File.Replace(temp, target, target + ".bak")`)
7. Update the stored fingerprint; clear `IsDirty`.

_(Amended 2026-10-15: KeePassLib's own transaction is off by default and silently falls back to a direct, non-atomic write when the target is a reparse point, e.g. OneDrive Files-On-Demand (`FileTransactionEx.cs:95-97`). LockPad therefore always uses its own temp-file pipeline. Verifying before the swap means a bad save never touches the real file.)_

Save results: `Saved`, `ExternalChangeDetected`, `ReadOnly`, `VerifyFailed`, `IoError(message)`.

### Locking

- Idle = no keyboard/mouse input in LockPad's windows for N minutes (default 5, configurable 1–60).
- On idle or manual lock (Ctrl+L):
  - Not dirty → close notebook, dispose credentials and database, clear UI, show unlock screen.
  - Dirty → run save pipeline.
    - `Saved` → as above.
    - Anything else → keep the notebook in memory (notes as KeePassLib protected strings), clear all UI controls, show the unlock screen with a warning: "Unsaved changes are held in memory. Saving failed: <reason>." Unlocking verifies the password against the held credentials and restores the editor.
- Close window: dirty → save pipeline; failure → cancel close and show error; read-only → close without saving.

### UI (WPF, MVVM)

- **Unlock screen:** opens on the last notebook path if it still exists; password box, optional key file, "Open other…", "New notebook…". Wrong password → "Wrong password or key file" (KDBX integrity check makes this reliable).
- **Create dialog:** path, password + confirmation, live strength meter (bits). Below 80 bits → warning; below 60 bits → explicit "Use this weak password anyway" confirmation required. Optional key file.
- **Main window:** left sidebar (filter box, notes list, New/Rename/Delete); right editor (`TextBox`, AcceptsReturn/AcceptsTab, Cascadia Mono → Consolas fallback, spell check off, Ctrl +/−/0 and Ctrl+wheel zoom, word-wrap toggle, Ctrl+F find bar); status bar (Saved/Unsaved • Read-only badge • lock countdown).
- **Commands:** Ctrl+N new note, Ctrl+S save, Ctrl+L lock, Ctrl+F find, F2 rename, Del (in list) delete with confirm.
- **Theme:** follows Windows app theme (registry `AppsUseLightTheme`), manual Light/Dark/System toggle.

### Clipboard

- Copy and Cut (keyboard, menu, context menu) are routed through one `ClipboardService` via WPF command bindings.
- Sets text plus formats: `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory` = DWORD 0, `CanUploadToCloudClipboard` = DWORD 0.
- Clears after N seconds (default 20) only if the clipboard still contains the text LockPad placed there.
- Drag-out of text from the editor is disabled. Paste-in is unrestricted.

### Password handling

- `PasswordBox.SecurePassword` → UTF-8 byte array → KeePassLib `KcpPassword`; intermediate arrays zeroed.
- Regression requirement: passwords with non-ASCII characters (e.g. `Abc№123`, Cyrillic) must round-trip (create → reopen).

### Settings

`%APPDATA%\LockPad\settings.json`: `lastNotebookPath`, `fontSize`, `wordWrap`, `theme`, `idleLockMinutes`, `clipboardClearSeconds`. Never contains secrets or note content. Corrupt/missing file → defaults.

## Error handling

| Situation | Behaviour |
|---|---|
| Wrong password / key file | Unlock screen error; nothing written. |
| File missing / unreadable / not KDBX | Clear error; stay on unlock screen. |
| File changed on disk since load | Overwrite / Save As / Cancel prompt. |
| Disk full / access denied during save | Original untouched (atomic write); `IoError` shown; notebook stays dirty. |
| Reopen-verify mismatch | Temp file discarded, real file untouched; `VerifyFailed` shown; notebook stays dirty. |
| Save fails at auto-lock | Data held in memory behind lock screen with warning. |
| Save fails on close | Close cancelled; error shown. |
| Non-local path (URL) | Rejected by `NotebookStore`. |
| Unmarked KDBX file | Opened read-only with warning. |

## Testing strategy

Core (automated, NUnit on net48; NUnit's `[Apartment(ApartmentState.STA)]` covers clipboard tests):
- Create → reopen round-trip (titles, texts, multiline, Unicode content).
- Wrong password and wrong key file rejected.
- Non-ASCII password round-trip (`Abc№123`, Cyrillic).
- Key file create/open.
- Simulated failure mid-save leaves the original file byte-identical.
- Backup rotation (bak1..bak3, oldest dropped).
- Reopen-verify detects an injected mismatch in the temp file and leaves the real file byte-identical.
- External change detection.
- Unmarked file opens read-only; save refused.
- Entry history entries are created on update and capped at 10.
- Local-path enforcement.
- KeePassXC interop: if `keepassxc-cli` is installed, a test lists/shows a LockPad-created notebook with it (skipped otherwise).

App services (automated where feasible):
- `ClipboardService` sets the exclusion formats and clears only its own text (STA test).
- Idle-lock timer logic (fake clock).

Manual checklist: unlock/create flows, editor features, theme switching, lock with/without unsaved changes, close with save failure (read-only file), copy then check Win+V history excludes the text.

## Migration notes

Old `.cnp` files are handled by sub-project 2 (importer). Until then the user keeps Crypto Notepad files as-is (backed up) and follows the manual migration precautions already given.

## Amendments (2026-10-18, from build, security review and red team)

- Titles are single-line: CR/LF/TAB become spaces, whitespace-only becomes empty. Text keeps newlines, and lone surrogates become U+FFFD.
- Closing after a failed save offers Save As / Close without saving (one further default-No confirmation; Cancel is the choice window's default) / Cancel, instead of only cancelling the close.
- An idle expiry while a dialog is open hides the editor and clears the clipboard at once, and the lock completes when the dialog closes. Input inside LockPad's own dialogs resets the idle timer before it expires, but never after expiry; OS file pickers and message boxes are not observed (user decision 2026-10-18).
- Backup copies (`*.bak<digits>`) and notebooks reached through a symbolic link open read-only. Save As and Create refuse backup-copy names.
- Only paths on local drives are allowed: UNC and mapped network drives are refused (user decision 2026-10-18). Device-namespace prefixes (`\\?\`, `\\.\`, `\??\`) are refused.
- File > Save As (Ctrl+Shift+S) exists.
- The held-notebook unlock applies a wrong-password back-off (1/2/4/8 s), and closing a held notebook needs two default-No confirmations.
- Held notes are NOT protected in memory: KeePassLib's `ProtectedString` keeps a plain string, and in-memory attackers are out of scope.
- The KeePassLib history size limit of 6 MiB per entry applies: notes over about 3 million characters keep no entry history (the .bak files still apply).
- Every save re-applies AES-256, Argon2id (at least 64 MiB, 2 iterations, 2 lanes), history 10 / 6 MiB, ProtectNotes and recycle bin off. KeePassLib's native library is never loaded.
- LockPad excludes itself from Windows Error Reporting per user, and sets no-heap (user decision 2026-10-18).
- The exit clipboard clear retries for up to 20 s (user decision 2026-10-18).
- A failed backup rotation loses no generation: the oldest backup is parked under a temporary `*.lockpad-rotation-*.old` name and deleted only after every shift succeeded; otherwise the shifts are undone and the save reports the failure.
- LockPad always locks when Windows locks (session lock, user switch, remote disconnect) and before sleep/hibernate, exactly like Ctrl+L; no setting (user decision 2026-10-19). Supersedes the non-goal 'Locking on Windows lock/sleep'.
- Theme is set only in the Settings item in the menu bar (View menu removed); New note asks for the name first (Cancel creates nothing); deleted notes can be restored with Undo delete until lock or close (user decisions 2026-10-19).
- One backup file (<notebook>.bak, the version before the last save), replacing the three rotating .bak1-.bak3 (user decision 2026-10-19). Older .bak1-.bak3 files are left untouched. An existing .bak is replaced only when this notebook wrote it in this session or it opens with the notebook's credentials, carries the marker and, when both files have one, the same random notebook id (CustomData `LockPad.NotebookId`, 32 hex characters, written by Create and added on the next save of an older notebook; not secret, never shown) (checked once per session, and never test-opened when its header KDF is heavier than the notebook's); any other .bak, and any .bak next to a Save As or Create target, is renamed aside to `<notebook>.bak.kept-<yyyyMMdd-HHmmss>` (never overwritten) and the user is told in the status bar. A folder named like the .bak makes the save fail with a message naming it, and so does a .bak another program holds open (sharing or lock violation): it is never classified as foreign or moved on that basis; the user retries.
- 2026-10-19: The program is renamed LockPad (user decision). Assemblies, namespaces, settings folder (%APPDATA%\LockPad) and the notebook labels (LockPad.Notebook, LockPad.NotebookId) use the new name; no migration: files with the labels of the earlier development builds open read-only.
- 2026-10-19: Change master password (File menu). The current password is required; password only (the key file is kept); the notebook is written twice so the .bak is re-locked with the new password (user decisions).
- 2026-10-21: A notebook path passed on the command line (double-click with "Open with") pre-fills the first unlock page; it is validated like the remembered path and never opens anything by itself.
- 2026-10-21: File menu with New/Open notebook (switching closes the current notebook like closing the window: save, or ask), Save, Lock and Exit; title shows the notebook file name; themed controls with high-contrast fallback.
