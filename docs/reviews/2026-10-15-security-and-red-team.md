# LockPad core: security review and red-team record

Plan Task 16, on the core feature branch. Reviewed baseline: `ca97fb4`. Fixes: `7400bea`..`d46f395`.

| Pass | Agent | Scope | Result |
|---|---|---|---|
| Security review | code-reviewer, read-only | `src/`, `lib/KeePassLib.csproj`, `nuget.config`, `Directory.Build.props`, integrity manifests and guards | 0 Critical, 2 High, 2 Medium, 10 Low |
| Red-team attack | red-team, PoC tests | `src/`, `tests/` | 0 Critical, 0 High, 4 Medium, 7 Low |
| Fix round 1 | implementer | 7 items | `7400bea`..`be1688c`, 570 tests passed |
| Re-attack 1 | red-team | fix round 1 | 0 Critical, 0 High, 0 Medium, 6 Low |
| Fix round 2 | implementer | 3 items (re-attack Lows L1-L4) | `a8ca995`..`d46f395`, 601 tests passed |
| Re-attack 2 | red-team | fix round 2 | 0 Critical, 0 High, 0 Medium, 2 Low |

> **Status on 2026-10-22** (whole-codebase check, [record](2026-10-22-full-codebase-check.md)): this record is kept as it was on 2026-10-15. Since then RA2-L1 (`\??\` paths), SEC-L1 (network paths: local drives only) and SEC-L3 (crash reports: LockPad excludes itself) were fixed; SEC-L7 no longer applies (backup rotation was removed, one `.bak` is kept); RA1-L6 (heavy key settings on Overwrite) and SEC-L10 (no lock file) were fixed in the whole-codebase check.

End state:
- No Critical, High or Medium finding is open.
- Full suite: 601 passed, 0 failed, 0 skipped. The 3 KeePassXC interop tests ran and passed against keepassxc-cli 2.7.12.

Every fix was test-first: a failing test reproducing the trigger, then the fix, then the full suite.

## Findings

Sources:
- SEC: security review.
- RT: red-team attack.
- RA1 / RA2: re-attack rounds 1 and 2.

| Source | Sev | Title | Disposition |
|---|---|---|---|
| SEC-H1 / RT-M4 | High | The idle lock never fires while a dialog is open, so note text stays on screen indefinitely | **Fixed** `a1e04f1`. On expiry under a dialog or a save, the editor is obscured and the clipboard cleared at once, and the lock completes when the dialog closes. Tests: `ShellLockTests.IdleExpiry_WhileDialogOpen_ObscuresAndClearsClipboard_KeepsEditorAndDialog`, `Obscured_DialogCloses_NextTickSavesAndLocks`, `Obscured_DialogCloses_SaveFails_Holds`, `Obscured_ActivityDoesNotUnobscure_AlwaysProceedsToLock`, `Obscured_SettingsOkWithLongerTimeout_ThenActivity_Locks`, `Resume_AfterLongSleep_WhileDialogOpen_Obscures`, `IdleExpiry_WhileSaveInProgress_ObscuresAndClearsClipboard_ThenLocksAfterSave`, `EditorChromeTests.EditorView_Obscured_HidesTextListAndFindBar_ShowsLockedPanel` |
| SEC-H2 | High | KeePassLib's native Argon2 DLL (KeePassLibN) can be planted and would receive the master key | **Fixed** `7400bea`. `NativeLib.AllowNative = false` before every KDF run, `SetDllDirectory("")` at startup, and `RestoreDirectory = true` on file dialogs. Tests: `NativeLibPolicyTests.*` (8), `RepositoryGuardTests.Source_NeverCallsNativeLibIsLibraryInstalled` |
| SEC-M1 | Medium | The KDF, cipher and history settings are enforced only at Create, so a weakened file stays weak | **Fixed** `182395e`. `SaveFormatPolicy` runs before every write (AES-256; Argon2id with at least 64 MiB, 2 iterations and 2 lanes; history 10 / 6 MiB; recycle bin off), and verify-before-swap checks it. Tests: `NotebookStoreSaveTests.Save_WeakenedByAnotherClient_RestoresAesArgon2idAndHistoryLimit` (8 cases), `Save_CompliantFile_KeepsExactKdfParameters_ExceptTheSaltKeePassLibRandomizes`, `Create_WithWeakKdf_WritesFreshCompliantParameters` |
| SEC-M2 | Medium | The vendored-library integrity check can be bypassed: the csproj compiled any `.cs` under `lib/`, and the code and manifest can be changed together | **Partly fixed** `4d30a9f`: only `lib/KeePassLib/**` is compiled. Tests: `RepositoryGuardTests.Lib_HasNoCSharpSourceOutsideVendoredFolder`, `KeePassLibCsproj_CompilesOnlyTheVendoredFolder`. **Accepted:** a manifest kept in the same repo cannot defend against someone with commit access. Verify `lib/keepasslib.sha256` against the official KeePass release out of band. |
| RT-M1 | Medium | A backup (`.bakN`) opened as a notebook loses its edits to later rotation | **Fixed** `ad79353`. Backup copies open read-only. Tests: `NotebookStoreOpenTests.Open_BackupCopy_IsReadOnly_SaveRefused_LaterRotationCannotLoseEdits`, `Open_BackupNamePattern_IsReadOnly`, `UnlockViewModelTests.Unlock_BackupCopy_OpensReadOnly_WarnsWithBackupMessage` |
| RT-M2 | Medium | A notebook opened through a symbolic link can never be saved | **Fixed** `86d4dbc`. A symlink (tag `0xA000000C` only) opens read-only, and cloud placeholders stay writable. Tests: `NotebookStoreOpenTests.Open_ThroughSymbolicLink_IsReadOnly_RealFileWritable`, `SymbolicLinkTagCheck_OnlySymlinkTag` (9 cases), `UnlockViewModelTests.Unlock_ThroughSymbolicLink_OpensReadOnly_WarnsWithLinkMessage` |
| RT-M3 | Medium | A lasting save failure makes the window impossible to close; Task Manager is the only exit and loses the edits | **Fixed** `be1688c`. Closing offers Save As / Close without saving (double confirmation) / Cancel, and File > Save As was added. Tests: `ShellLockTests.TryClose_SaveFails_*` (7), `PrepareForSessionEnd_SaveFails_NeverShowsTheCloseChoice`, `EditorChromeTests.CloseChoiceWindow_ButtonsAndDefaults`, `MainWindow_FileMenu_SaveAs_BoundToEditorSaveAsCommand` |
| RA1-L1 / L2 | Low | A `\\?\` prefix bypasses the symlink and backup checks | **Fixed** `a8ca995`. `\\?\` and `\\.\` are rejected in every slash form, and wildcard paths are never queried. Tests: `LocalPathTests.DeviceNamespacePrefix_Rejected` (9), `NotebookStoreOpenTests.Open_DevicePrefixedShortNameOfBackup_CannotOpenWritable`, `Open/Create/SaveAs_DeviceNamespacePath_*` |
| RA1-L3 | Low | Save As to a backup name is later deleted by rotation | **Fixed** `d05d267`. Save As and Create refuse backup names. Tests: `NotebookStoreSaveTests.SaveAs_BackupCopyName_Refused_NothingWritten_StaysDirty`, `SaveAs_FreeBackupGenerationOfMain_Refused_SoRotationCannotDeleteTheCopy`, `Create_BackupCopyName_Refused_NothingWritten`, plus the UI routes |
| RA1-L4 | Low | The held-notebook close discards after one Yes/No that defaults to Yes | **Fixed** `d46f395`. Two default-No confirmations, Esc = No. Tests: `ShellLockTests.TryClose_HeldNotebook_*` (3), `TryClose_DeclinedDiscard_ThenLockHolds_HeldCloseStillNeedsBothConfirmations`, `EditorChromeTests.ConfirmDefaultNoWindow_NoIsDefaultAndCancel` |
| RA1-L5 | Low | Behind the "Locked" panel the open dialog still works, and the pending lock saves its result | **Accepted.** Leaving open dialogs untouched is deliberate; the note text is hidden. |
| RA1-L6 | Low | A planted file with an absurd Argon2 iteration count hangs the window on Overwrite | **Accepted.** It needs write access to the notebook folder, which already allows deleting or replacing the file. It predates the fixes. |
| RA2-L1 | Low | The NT prefix `\??\` is not in the rejected list and Core Save As accepts it | **Accepted.** The UI pages refuse it, and opening through it falls back to read-only. A candidate for the final whole-branch review. |
| RA2-L2 | Low | `Y` alone (mnemonic) probably answers Yes in the confirm window | **Accepted.** Same as the Win32 message box it replaces, and two confirmations are still needed. Not shown at runtime. |
| SEC-L1 / RT-L6 | Low | UNC (network) paths are accepted, and the saved path is probed at startup (possible NTLM hash leak) | **Open: user decision.** Restricting to local drives would break notebooks on network shares. |
| SEC-L2 | Low | Plaintext copies of note text stay in managed memory (strings, undo stack), never zeroed | **Accepted.** An inherent limit of a WPF TextBox; the spec puts in-memory attackers out of scope. |
| SEC-L3 | Low | No opt-out from Windows Error Reporting crash dumps | **Open: user decision.** It needs a persistent per-user registry change. |
| SEC-L4 | Low | Copied text stays on the clipboard if the process is killed | **Accepted.** A clear can't run once the process is killed. The clipboard-history and cloud exclusion formats still apply. |
| SEC-L5 / RT-L5 | Low | Ctrl+C in a MessageBox copies its text, including note titles, unprotected | **Accepted.** Titles are not protected by design; the manual checklist warns never to put secrets in titles. |
| SEC-L6 / RT-L4 | Low | Saving through a hard link breaks the link and the other name keeps the old content | **Accepted.** The symlink case is fixed (RT-M2). Hard links and parent-directory junctions are not detected. |
| SEC-L7 | Low | Repeated rotation failures can delete older backups | **Accepted.** The main file is never touched by a failed save; a candidate for the final review. |
| SEC-L8 | Low | `*.tmp` files left after a crash are never cleaned up | **Accepted.** They are encrypted KDBX; the next save succeeds (Review Focus 4). |
| SEC-L9 | Low | The no-network guard misses some routes (XAML URLs, `Process.Start`, UNC) | **Accepted.** No such use exists in `src/`; a candidate for a guard extension. |
| SEC-L10 | Low | No NuGet lock file | **Accepted.** There are 4 exact-pinned packages and a single-source `nuget.config`. |
| RT-L1 | Low | Logoff is refused during the held screen's wrong-password backoff | **Accepted.** Logoff is refused, never data loss. |
| RT-L2 | Low | Two entries with the same UUID make the notebook unsavable | **Accepted.** Only a hand-crafted file can contain this; saves fail safely. |
| RT-L3 | Low | A close arriving during the logoff save leaves a held screen that cannot be unlocked | **Accepted.** No data loss was observed (the save had succeeded); Task 14's close-in-progress guard narrows it further. |
| RT-L7 | Low | Creating a notebook does not update the remembered last-notebook path | **Accepted.** Convenience only. |

## Not covered by automated tests

These go to the manual checklist (`docs/manual-test-checklist.md`):
- real logoff, restart and sleep/resume;
- file dialogs not changing the current directory, verified only through option mapping;
- the clipboard history service holding the clipboard;
- symlink tests ignored on machines without the symlink privilege;
- keepassxc-cli 2.7.12 with notes-only edits (KeePassXC GUI edits are manual).
