# Whole-codebase check of 2026-10-22: review record

Purpose: the last review of the whole codebase was on 2026-10-15; every later review covered one change. This check
covered all of LockPad's own code, the build setup and the documentation at `309fbed`, then attacked its own fixes.
Branch `fix/full-check-2026-10-22`.

| Pass | Agent | Scope | Result |
|---|---|---|---|
| Security review | code-reviewer, read-only, ran the suite | all of `src/`, the build files, the docs | 0 Critical, 0 High, 0 Medium, 9 Low |
| Red-team attack | red-team, read-only | whole product | cut off by an API error after its first finding: 1 Medium |
| Fix round 1 | (this session) | 1 Medium, 9 Low | `7a7ac78`, `bebdb58`, `da79095`, `95e051d`; 1172 tests passed |
| Red-team attack (re-run) | red-team, read-only, ran the suite | round 1 fixes and the rest of the product | 0 Critical, 0 High, 1 Medium, 6 Low; all round 1 fixes hold |
| Fix round 2 | (this session) | 1 Medium, 4 Low, 1 gap | `94d4b2d`; 1179 tests passed |
| Re-attack 1 | red-team, read-only, ran the suite | round 2 fixes | 0 Critical, 1 High, 1 Medium, 3 Low; the clipboard, root-group and path-limit fixes hold |
| Fix round 3 | (this session) | 1 High, 1 Medium, 1 Low | `f2f1553`; 1183 tests passed |
| Re-attack 2 | red-team, read-only, ran the suite | round 3 fixes | 0 Critical, 0 High, 0 Medium, 2 Low; all four fixes hold |
| Fix round 4 | (this session) | 2 Low | 1186 tests passed |

## Findings and dispositions

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| FC-M1 | Medium | LockPad's key-settings check read the **first** KDF field of a file's header; KeePassLib derives with the **last** one and also honours the obsolete KDBX 3 transform fields in KDBX 4 files. A planted `.bak` could pass the cap and run a key derivation for hours on the UI thread at every save, notes on screen | **Fixed** `7a7ac78`: a second KDF field or a transform field makes the header unreadable (never test-opened); the file is read once, and the fingerprint, the cap and the key derivation all use those bytes (tests `Save_BakWithAnExtraKdfHeaderField_*`, `KdfHeaderReader_ExtraKdfHeaderField_Null`) |
| FC-L1 | Low | The lock's save (seconds of key derivation on the UI thread) ran with the notes still on screen | **Fixed** `7a7ac78`, `94d4b2d`: the idle, resume and late-input locks hide the notes and clear the clipboard first; the next Tick saves and locks (tests `*_Dirty_HidesTheNotesFirst_*`) |
| FC-L2 | Low | Overwrite test-opened a changed file with whatever key settings its header named (re-check of the accepted RA1-L6) | **Fixed** `7a7ac78`: heavier settings than the notebook's are refused without deriving a key (test `SaveOverwrite_FileReplacedWithAHeavierKdf_*`). Opening a hostile file can still hang the unlock page: **accepted and documented** (nothing is written, no notebook is open; SECURITY.md) |
| FC-L3 | Low | Package versions were minimums, no lock files, and the documented release build also ran test-package build logic (re-check of SEC-L10) | **Fixed** `bebdb58`: exact versions, `packages.lock.json` in locked mode (NuGet checks content hashes, NU1403), release build from the app project only (CONTRIBUTING.md); guard test |
| FC-L4 | Low | The vendored-library guard did not cover MSBuild import files (`Directory.Build.*`, `.rsp`) | **Fixed** `bebdb58`: only the reviewed root files may exist and none may add code, imports or targets; empty root `.targets` and `.rsp` stop MSBuild searching above the repository |
| FC-L5 | Low | Docs implied that locking erases notes from memory | **Fixed** `da79095` (SECURITY.md: close LockPad when away for long) |
| FC-L6 | Low | "Delete the folder to remove LockPad" ignored the settings file, the registry value and Windows' dialog history | **Fixed** `da79095` (user guide, USB use on a borrowed PC) |
| FC-L7 | Low | "No network code" is not literally true (KeePassLib contains some; LockPad never calls it) | **Fixed** `da79095` (wording) |
| FC-L8 | Low | The 2026-10-15 record still listed items as open or accepted that were fixed later | **Fixed** `da79095` (status note on that record) |
| FC-L9 | Low | SECURITY.md dropped the "when both files carry one" qualifier of the `.bak` id rule | **Fixed** `da79095` |
| FC-RT-M1 | Medium | Overwrite checked only the marker, not the notebook id: a different same-password notebook moved over this one was replaced, became the `.bak`, was remembered as this notebook's own `.bak` and was destroyed by the next save | **Fixed** `94d4b2d`: when both carry an id it must match (test `SaveOverwrite_FileReplacedWithAnotherSamePasswordNotebook_RefusedAndNeverLost`); two older tests that used a different notebook as an "external change" now use a copy of the same notebook |
| FC-RT-L1 | Low | The external-change question and the close "Could not save" question answered after the editor was obscured are still acted on | **Accepted**, as recorded before (re-attacks 2026-10-22: "Could not save" matches the two-click discard on the held page; Overwrite and Save As still write an encrypted file under the user's key, and Overwrite now also needs the same notebook) |
| FC-RT-L2 | Low | Copied text containing U+0000 was never cleared: Windows reads clipboard text only up to the first NUL | **Fixed** `94d4b2d`: NULs are left out of the copy (real-clipboard test `ClearIfOwned_TextWithNul_StillClears`, which failed before the fix) |
| FC-RT-L3 | Low | Overwrite is refused in some legitimate cases: a notebook below the key-settings floor whose file another LockPad re-tuned, or a file re-saved as KDBX 3.1 | **Accepted**: rare, safe (nothing is written, the message names Save As) |
| FC-RT-L4 | Low | The in-memory ownership load had no root group, so a wrong key raised KeePassLib assert dialogs in Debug builds | **Fixed** `94d4b2d` (test `Save_BakWithAnotherPassword_OwnershipCheck_RaisesNoKeePassLibAssert`, which failed before the fix); Release builds were not affected |
| FC-RT-L5 | Low | With two LockPad windows open, a settings save in one overwrites settings changed in the other | **Accepted and documented** (user guide: run one LockPad at a time); the reverted values are the user's own earlier choices, still clamped to the safe ranges |
| FC-RT-L6 | Low | A notebook path near 260 characters opens, but the temporary and kept-aside names do not fit, so it can never be saved in place | **Fixed** `94d4b2d` for new paths: Create and Save As refuse paths over 231 characters (tests `*_PathLongerThanTheLimit_*`, `Create_PathAtTheLimit_SavesTwice`). An existing longer notebook still fails to save with an error and stays in memory; Save As to a shorter path works |
| FC-RT-L7 | Low | Input after the idle deadline locked directly, so its save ran with the notes visible | **Fixed** `94d4b2d` (test `RecordActivity_AfterDeadline_Dirty_HidesTheNotesFirst_*`) |
| FC-RT-N1 | Note | A notebook whose Notes group holds two entries with the same id (a merge or crafted file, needing the user's key) cannot be saved | **Accepted**: low plausibility; the file on disk is never touched |
| RA1-H1 | High | Introduced by FC-RT-L7: a late key that only hid the editor was not swallowed, so Enter, Backspace or Ctrl+V could reach the hidden text box and the lock saved the change unseen | **Fixed** `f2f1553`: the main window swallows input that hid or locked the editor, and the editor ignores text changes while hidden (tests `LateInput_HidingOrLocking_IsSwallowed_OnTimeInputIsNot`, `EditorText_WhileObscured_IsIgnored_NeverSaved`) |
| RA1-M1 | Medium | Save As kept the notebook id, so a Save As copy moved over the original passed the Overwrite id check (the FC-RT-M1 outcome through another route) | **Fixed** `f2f1553`: the copy gets its own id, and the original id is restored if the copy is not written (tests `SaveAs_GivesTheCopyANewNotebookId_*`, `SaveAs_Fails_TheNotebookKeepsItsId`) |
| RA1-L1 | Low | When one side has no notebook id (only files from hand-made or id-dropping clients), Overwrite falls back to password and marker | **Accepted**: every LockPad build writes an id and KeePassXC keeps it; copies made outside LockPad share the id and count as the same notebook, as documented |
| RA1-L2 | Low | A Tick already queued could start the save before the window redrew after late input or a resume (the timer outranks rendering) | **Fixed** `f2f1553`: the first Tick after such a hide never saves (tests `*_SecondTickSavesAndLocks`) |
| RA1-L3 | Low | A notebook already longer than the path limit fails to save with a raw Windows message instead of LockPad's | **Accepted**: nothing is replaced before the failure, the notebook is held, Save As works; refusing such saves up front could block saves that work on PCs with long-path support |

| RA2-L1 | Low | A resume queued together with late input (or right after a Tick hid the notes) locked at once, so the save could start before the window redrew | **Fixed** (round 4): a resume leaves an editor already hidden for the save to the Tick (tests `LateInputHide_ThenQueuedResume_*`, `TickHide_ThenQueuedResume_*`) |
| RA2-L2 | Low | The wait-for-redraw mark was set after the hide step, which can rethrow a listener's failure | **Fixed** (round 4): marked first (test `LateInputHide_ListenerThrows_FirstTickStillDoesNotSave`) |

Verdict: no Critical, High or Medium finding remains. The round-4 Low fixes were not attacked again; they only make
the lock wait for the next Tick in two more cases.

Red-team agents reasoned from the source and ran the existing suite; their test skeletons became the automated tests
named above. Input routing through WPF (key handling, IME, context menus) was reasoned about, not observed; the editor
ignoring text changes while hidden is the backstop for every input path.
