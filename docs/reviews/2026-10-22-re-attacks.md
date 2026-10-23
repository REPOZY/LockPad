# Re-attacks of 2026-10-22: review record

Purpose: three changes had final fix rounds that were never attacked again. Each was re-attacked on master
(`8857d10` plus later commits); the fixes were then attacked once more. Red-team agents reasoned from the source (no
code run); their test skeletons were turned into automated tests.

## Round 1: three re-attacks

| Re-attack | Scope | Result |
|---|---|---|
| Change master password | fix round 3 (`472497f`) and the flow at HEAD | 0 Critical, 0 High, 1 Medium, 5 Low; `472497f` holds |
| Switching notebooks and UI | fix round 2 (`e3c4869`) and switching at HEAD | 0 Critical, 0 High, 1 Medium, 2 Low; Y1, Y3, Y4 hold, Y2 only partly |
| Capture protection | review fixes (`e82bec1..a9a3779`) | 0 Critical, 0 High, 0 Medium, 5 Low; every fix holds |

## Fixes (`7052ff8`), 1158 tests passed

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| CP-M | Medium | After a failed password change, the outcome box counted as "saving", so a session end refused without saving unsaved notes | **Fixed**: the outcome is shown after the save scope ends (test `SessionEndWhileTheFailureBoxIsOpen_DirtyNotesAreSaved_CredentialsAlreadyReleased`) |
| CP-L1 | Low | The dialog's credentials (incl. the unapplied new password) stayed in memory while the outcome box was open | **Fixed**: released before the box (same test) |
| CP-L2 | Low | After FailedWhileReplacing the notebook file was missing but the notebook clean, so lock and close dropped it silently | **Fixed**: a write stays pending (test `FirstReplaceFailsHalfway_NotebookKeepsAWritePending`) |
| CP-L3 | Low | ChangedButReplaceIncomplete told the user to wait for a save that cannot succeed | **Fixed**: the message says Save cannot finish this and names Save As |
| CP-L4 | Low | The credential hand-over is not exception-safe for programmer-error exceptions | **Accepted**: no trigger from valid data found |
| CP-L5 | Low | A hard-linked notebook keeps an old-key copy at the other link name | **Documented** in README (Hard links) |
| SW-M1 | Medium | A throwing Tick escaped the DispatcherTimer handler; the timer is then never rescheduled, so the idle lock stopped for the rest of the process | **Fixed**: `MainWindow.RunLockTick` never throws; reported once (tests `LockTick_*`) |
| SW-L1 / CA-L1 | Low | A Delete or Rename answered after the editor was obscured still changed the notebook, and the lock saved it | **Fixed**: ignored (tests `Delete_ConfirmedAfterObscure_DeletesNothing`, `Rename_AnsweredAfterObscure_ChangesNothing`) |
| SW-L1b | Low | The switch/close "Could not save" dialog answered after obscure can discard (two clicks) | **Accepted**: matches the accepted two-click discard on the held page |
| SW-L2 / CA-L2 | Low | Stale comments (MainWindow lists Confirm as a Win32 box; Notebook.Dispose summary misplaced) | **Fixed** |
| CA-L2..L5 | Low | README: Delete confirmation listed as a Windows box; Ctrl+C on a message box; Alt+Tab missing; capture hiding stated as fact | **Fixed** |

## Round 2: final attack on `7052ff8`

0 Critical, 0 High, 0 Medium, 4 Low; all five fixes hold.

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| F-L1 | Low | The New note prompt answered after obscure still added a note | **Fixed** (test `NewNote_AnsweredAfterObscure_AddsNothing`) |
| F-L2 | Low | A throwing countdown listener during the lock left a blank window | **Fixed**: the countdown reset is its own teardown step (test `LockTick_CountdownListenerThrows_UnlockPageStillShown`) |
| F-L3 | Low | README: a failed session-end save with a LockPad dialog open keeps the notes in the editor, not behind the lock screen; New note missing from the activity list | **Fixed** |
| F-L4 | Low | A throwing status listener could hide the password-change outcome | **Fixed**: the backup notice is shown inside try/catch |
| (noted) | - | The external-change question and Save As picker inside a save can be answered after obscure | **Accepted**: the result is still an encrypted file under the user's key |

Verdict: no Critical, High or Medium finding remains. The round-2 Low fixes were not attacked again.
