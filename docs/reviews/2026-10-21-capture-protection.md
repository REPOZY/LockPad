# Review record: capture protection (2026-10-21)

Range reviewed: 47ee7b4..e82bec1 (branch feature/capture-protection). Spec: docs/specs/2026-10-21-capture-protection-design.md.

## Code review
- Spec FAIL (narrow: README missed the message-box limit), Quality PASS. No Critical.
- I1 (Important): Win32 message boxes are not covered and the Delete confirmation shows the note title. Fixed:
  Yes/No confirmations use ConfirmWindow (a LockPad window, covered; Yes default and Esc = No as before); README and
  spec list the uncovered windows.
- M1: an exception in ApplyTheme skipped ApplyCaptureProtection. Fixed: capture protection is applied first (test).
- M2: README wording could read as protection against malware. Fixed: points to "malware on your PC" limit.
- M3: CaptureProtection duplicates DarkTitleBar's plumbing. Accepted (spec asked for the same pattern); possible
  follow-up: one WindowSetup.Attach.
- M4: unneeded SetLastError on the import. Fixed (removed).
- M5: the startup-order test checks text order only. Accepted; the test says it is a heuristic.

## Red team (reasoning only; it ran no code)
- 0 Critical, 0 High, 3 Medium, 4 Low.
- M: the Delete confirmation (same as I1). Fixed.
- M: IME candidate lists, Windows text suggestions and the touch keyboard show typed text. Documented (often another
  process; cannot be covered).
- M: Remote Desktop / VM / Citrix sessions captured on the connecting computer. Documented.
- L: theme exception skips capture protection (same as M1). Fixed.
- L: no indication when Windows refuses both calls. Accepted (not expected with DWM always on in Windows 10/11).
- L: virtual displays and Miracast count as monitors. Documented.
- L/info: the notebook file name shows on the taskbar and in Alt+Tab labels. Documented.
- Suggested a thread-wide window hook to cover message boxes, popups and file dialogs. Declined: more native code,
  uncertain timing before the first frame, and none of those windows shows note text after the ConfirmWindow change.

## Verification
Full suite after the fixes: 1151 passed, 0 failed, 0 skipped (Release). Runtime behaviour (no unprotected first frame,
taskbar/Alt+Tab previews, Magnifier, RDP) is on the manual checklist.
