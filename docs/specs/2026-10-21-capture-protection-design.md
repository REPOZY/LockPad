# Capture protection (design, approved 2026-10-21)

## Goal
Keep LockPad's windows out of screenshots, screen recordings and screen sharing, so notes are never shown by accident
(a forgotten screen share) or grabbed by ordinary screen-capturing programs.

## Decisions (user)
- On by default, also for new users. Setting: Settings > "Hide LockPad from screenshots and screen sharing".
- Every LockPad window is covered: the main window (all pages) and every LockPad dialog.

## Design
- `CaptureProtection` (App/Services), modelled on `DarkTitleBar`: every window calls `CaptureProtection.Attach(this)`
  in its constructor; on SourceInitialized (HWND created, before first shown) it calls `SetWindowDisplayAffinity`:
  - on: `WDA_EXCLUDEFROMCAPTURE` (0x11); if refused or throwing, `WDA_MONITOR` (0x1);
  - off: `WDA_NONE` (0x0).
- Windows before 10 version 2004 treat 0x11 as `WDA_MONITOR` (Microsoft docs), so no version check.
- App startup sets the real API and the saved value after loading settings and before any window exists. Until then
  the value is on.
- OK in Settings saves the value and re-applies it to every open window (no restart).
- Best effort: any failure is ignored and never keeps a window from opening.
- `AppSettings.HideFromScreenCapture` (bool, default true; old settings files load as true). Not a secret; the
  settings member allow-list test is updated.

## Limits (documented in README)
- Microsoft: "unlike a security feature or an implementation of DRM, there is no guarantee". It covers the public
  capture APIs only: not a phone camera, not malware that injects into LockPad.
- WPF popups (menus, drop-down lists, tooltips), Win32 message boxes (errors and warnings) and the Open/Save file
  dialogs are separate windows and are not covered. None shows note text (the error boxes can show file paths).
  Review round (2026-10-21): the Delete confirmation did show the note title, so Yes/No confirmations now use a
  LockPad window (ConfirmWindow, Yes default as before), which is covered.
- IME candidate lists and Windows text suggestions show typed text and are not covered (often another process).
- Remote Desktop / VM / Citrix sessions captured on the connecting computer, and virtual displays or Miracast, are
  outside Windows' protection. The taskbar and Alt+Tab labels show the window title (the notebook file name).
- Accepted residuals: no indication if Windows refuses both calls (not expected with DWM always on in Windows 10/11);
  CaptureProtection duplicates DarkTitleBar's plumbing (a shared WindowSetup.Attach is a possible follow-up); a
  thread-wide window hook that would also cover message boxes and popups was declined (more native code, uncertain
  timing, and none of those windows shows note text).
- Windows Magnifier may show LockPad blank while it is on; users who need it turn the setting off.
- The user cannot screenshot LockPad while it is on.

## Tests
Fake API and fake handles (as in DarkTitleBarTests): the calls and fallbacks; every window type applies the value on
SourceInitialized (and a new window type fails the coverage test); a live change reaches open and later windows;
settings default, old files, round trip, view model, OK/Cancel; the Settings check box and note; the startup order
(source check).
