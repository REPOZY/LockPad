# Security

How LockPad protects your notes, what it cannot protect against, and how it was
reviewed. For everyday use, see the [user guide](docs/user-guide.md).

- [Encryption](#encryption)
- [Your master password](#your-master-password)
- [Saving](#saving)
- [Locking](#locking)
- [Closing and signing out](#closing-and-signing-out)
- [Clipboard](#clipboard)
- [Screenshots and screen sharing](#screenshots-and-screen-sharing)
- [Files and paths](#files-and-paths)
- [Crash reports](#crash-reports)
- [What LockPad does not protect against](#what-lockpad-does-not-protect-against)
- [Security reviews](#security-reviews)
- [Why LockPad exists](#why-lockpad-exists)

## Encryption

| Setting | Value |
| --- | --- |
| File format | KDBX 4.1 (KeePass / KeePassXC compatible) |
| Cipher | AES-256 |
| Key derivation | Argon2id, at least 64 MiB memory, 2 lanes, 2 iterations; new notebooks are tuned to take about 1 second on your PC |
| Library | Official KeePassLib 2.61.1, vendored unmodified; an automated test checks its integrity |

These settings are applied again on **every save**: if another program weakens
a notebook's encryption or key-derivation settings, LockPad restores them the
next time it saves. Every save also gets a fresh random salt and encryption IV.

KeePass's optional native helper library (`KeePassLibN`) is never loaded, so a
copy of that DLL planted next to a notebook can never see your master key.

## Your master password

- The password goes to the KeePass library as raw UTF-8 bytes. Every temporary
  copy LockPad itself makes is wiped from memory as soon as it is no longer
  needed. The strength meter (part of the KeePass library) and Windows' own text
  handling can leave fragments in memory that cannot be wiped.
- Passwords with any characters work, including accented letters, Cyrillic and
  symbols.
- A wrong password shows "Wrong password or key file." Nothing is ever written
  after a failed unlock.

## Saving

Every save, whether from Ctrl+S, on lock or on close, follows the same steps:

1. Refuse if the notebook is read-only.
2. Check whether another program changed the file. If it did, Ctrl+S and closing
   ask: Overwrite, Save As or Cancel. Overwrite replaces the file only if it is
   still this notebook: it opens with your password and key file, is a LockPad
   notebook, and, when both carry one, has the same notebook id. A different
   notebook with the same password is never overwritten. A save on lock asks
   nothing: it keeps your changes in memory behind the unlock screen, with a
   warning.
3. Write the notebook to a new temporary file in the same folder and flush it to
   disk.
4. Re-open that file with your password and compare every note with what is in
   memory. If anything differs, discard it. Your real file has not been touched.
5. Swap in the new file in one step. The old file becomes the backup
   `private.kdbx.bak`, replacing the previous backup.

If saving fails (a read-only file, a full disk, a locked file), your notebook and
its `.bak` stay byte-for-byte unchanged and your edits stay in memory. If the
final swap fails halfway, nothing is lost: the previous version is kept as the
`.bak` and the verified new copy as a `.tmp` file, and the error message names
both. Saving with no changes writes nothing, so it never replaces your backup.

An existing `.bak` that is not this notebook's own (for example your own copy
with that name, or another notebook's) is never overwritten: LockPad keeps it as
`private.kdbx.bak.kept-<date>` and says so in the status bar. A `.bak` counts as
this notebook's own only if it opens with the same password and key file, is a
LockPad notebook, and, when both files carry one, has the same random notebook
id. If another program has the `.bak` open, the save fails with a message naming
it and nothing is changed; save again.

To check a file it did not just write (the `.bak`, or a changed notebook you
chose to overwrite), LockPad must derive its key. It reads the file once and
first checks its key settings: a file with heavier settings than your notebook's
own is never test-opened. It is not overwritten either: a `.bak` is kept aside,
and Overwrite is refused with a message pointing to Save As. A crafted file
therefore cannot freeze LockPad with your notes on screen.

## Locking

- LockPad locks after a period with **no keyboard or mouse input in its
  windows**: 5 minutes by default, adjustable from 1 to 60. Input in LockPad's
  own dialogs counts too; input in the standard Windows boxes (file picker,
  "file changed" question, error messages) does not, because LockPad cannot
  observe it.
- Ctrl+L locks at once. LockPad also locks at once when Windows locks (Win+L,
  switching user, a remote disconnect) and before sleep or hibernation. Just
  before sleep your notes are hidden and the clipboard cleared at once; the save
  and lock follow, and finish after waking if the sleep cut them off.
- Before locking, unsaved changes are saved. That save takes a few seconds, so
  the idle lock and the lock after sleep first hide your notes and clear the
  clipboard, then save and lock a second or two later. A key press or click at
  that moment is ignored; it never changes your notes. If the save fails, your changes
  are held in memory behind the unlock screen, with a warning; your password
  restores them. Wrong attempts there wait 1, 2, 4, then 8 seconds.
- If the lock becomes due while one of LockPad's dialogs is open, your notes are
  hidden at once ("Locked - close the open dialog to finish locking.") and the
  clipboard is cleared; the lock finishes when the dialog closes. A New note,
  Rename or Delete answered after that point is ignored, because it may not have
  been you.
- Changing the Windows clock cannot postpone the lock.

## Closing and signing out

- Closing the window saves your changes first. If that fails, you choose:
  **Save As...**, **Close without saving** (with a confirmation that defaults to
  No) or **Cancel** (the default).
- Signing out of or restarting Windows saves unsaved changes quietly first. If
  that fails, LockPad refuses the sign-out and keeps your changes in memory:
  behind the lock screen, or in the editor while one of LockPad's dialogs is
  open. If Windows forces the restart anyway ("Restart anyway", an update
  deadline), those unsaved changes are lost.

## Clipboard

- Text you copy or cut in LockPad is marked so that Windows clipboard history
  (Win+V) and the cloud clipboard skip it.
- It is cleared after 20 seconds (adjustable from 5 to 120), when you lock, and
  when you close LockPad. On exit LockPad keeps trying for up to 20 seconds,
  because Windows' clipboard history service sometimes holds the clipboard
  briefly.
- LockPad only clears text it put there itself; something you copy in another
  program afterwards is left alone.
- Dragging selected text out of the editor is blocked (and so, deliberately, is
  drag-to-move inside it). Paste works as normal.

## Screenshots and screen sharing

- While **Hide LockPad from screenshots and screen sharing** is on (the
  default), every LockPad window asks Windows to leave it out of captures. In a
  screenshot (Win+Shift+S, Print Screen), a recording or a shared screen (Teams,
  Zoom, Discord, OBS) the window does not appear; on Windows 10 before version
  2004 it appears as a black box.
- This protects against showing your notes by accident, for example in a
  forgotten screen share, and against ordinary capture tools. It is not a
  guarantee: Microsoft describes it as unlike a security feature. It does not
  stop malware on your PC, a phone camera, or someone looking over your
  shoulder.
- Not covered, because Windows draws them as separate windows: menus, drop-down
  lists, tooltips, Windows' own error and warning boxes, and the Open/Save
  dialogs. None of these shows note text; error boxes can show file paths. The
  candidate list of an input method (for example Japanese or Chinese) and
  Windows typing suggestions show what you type and are not covered either. The
  window title (your notebook's file name) can show on the taskbar and in
  Alt+Tab.
- When LockPad runs inside Remote Desktop, a virtual machine or Citrix, the
  computer you connect from can still capture it. Virtual or wireless displays
  (Miracast, screen-extension apps) count as monitors and are not protected.
- To take screenshots of LockPad yourself, turn it off in **Settings**. Windows
  Magnifier still shows LockPad normally: it only enlarges your own screen and
  records or sends nothing.

## Files and paths

- Only notebooks on this PC's local drives are accepted. Network shares and
  mapped network drives are refused for opening and saving. A local folder that
  is itself a link to a network share is not detected.
- Special Windows path forms (`\\?\`, `\\.\`, `\??\`) are refused.
- A notebook reached through a symbolic link opens read-only, because saving
  through the link would break it.
- KDBX files LockPad did not create (for example your KeePassXC password
  database) and backup copies (`*.bak`, `*.bak1`...) open read-only, and you
  cannot save a notebook under a backup-style name. LockPad never writes to a
  file without its own notebook marker.

## Crash reports

On startup, LockPad excludes itself from Windows Error Reporting for your user
account and tells Windows to leave its memory out of any crash report that is
still made, so a crash does not put your notes in a crash file. To undo it,
delete the value `LockPad.exe` under
`HKCU\Software\Microsoft\Windows\Windows Error Reporting\ExcludedApplications`.

Windows itself can still write memory to disk: a blue-screen crash dump, the
page file and the hibernation file. No program can switch those off. Turn on
**BitLocker** for your system drive (Windows 10/11 Pro) to keep them encrypted.

## What LockPad does not protect against

These limits are deliberate or unavoidable:

- **Malware already on your PC.** A keylogger or a program that reads other
  programs' memory can capture what you type and see. This is out of scope for
  any notepad.
- **A forgotten master password or a lost key file.** The encryption has no back
  door; nobody can recover them.
- **Screen capture by a determined attacker.** Capture hiding stops ordinary
  capture tools, not a camera or malware aimed at LockPad.
- **Note text in memory.** While a notebook is unlocked, your notes exist as
  ordinary text in LockPad's memory, which cannot be reliably wiped. This
  includes changes held behind the lock screen after a failed save. Locking
  hides and releases your notes but does not erase them from memory: copies can
  remain until LockPad exits. Close LockPad when you leave the PC for long.
- **Memory that Windows writes to disk.** Crash dumps, the page file and the
  hibernation file can contain open notes. Use BitLocker.
- **Note titles are not secret.** Titles appear in the notes list and in the
  delete confirmation. **Never put a secret in a title.**
- **The clipboard after a forced kill.** If LockPad is killed (for example from
  Task Manager) or the PC loses power, copied text can stay on the clipboard.
  Third-party clipboard managers may ignore Windows' history exclusion.
- **Mistakes older than your last save.** The `.bak` holds only the version
  before your last save. Keep your own copies on a separate USB stick or drive.
- **Very large notes.** A note over about 3 million characters is still saved and
  backed up, but KeePass's per-note version history (6 MiB) cannot hold it.
- **Opening a hostile file.** A `.kdbx` file crafted with extreme key settings
  can keep the unlock screen on "Unlocking..." indefinitely or run out of
  memory. Nothing is written and no notebook is open at that point (switching
  notebooks saves and closes the current one first); close LockPad from Task
  Manager. Only open notebooks you made yourself.
- **Hard links.** Saving through a hard link breaks the link: the other name
  keeps the old version (after a password change, with the old password). Open
  the original file instead.
- **Someone who can change this source code.** The integrity check on the
  vendored KeePass library catches accidental changes, not someone who changes
  both the library and its checksum file. To check it yourself: download the
  official KeePass 2.61.1 source package, verify its SHA-256 against
  [keepass.info](https://keepass.info/integrity.html), and compare its
  `KeePassLib` folder with `lib/KeePassLib` here.

## Security reviews

LockPad's code was reviewed and attacked by AI agents (Claude): a reviewer
checks each change against its design, and a red team tries to break it with
concrete inputs and event sequences. This is not a third-party audit.

| Change | What was done | Record |
| --- | --- | --- |
| Core: encryption, saving, locking, clipboard, read-only files, passwords | Security review of all of LockPad's own code and build setup; red-team attack with purpose-written test code; two re-attacks on the fixes | [2026-10-15](docs/reviews/2026-10-15-security-and-red-team.md) |
| Rename and Change master password | Review; red-team attack; three re-attacks on the fixes | [2026-10-20](docs/reviews/2026-10-20-lockpad-rename-and-change-password.md) |
| Switching notebooks and the new look | Review; red-team attack; three re-attacks on the fixes | [2026-10-21](docs/reviews/2026-10-21-switch-notebooks-and-ui.md) |
| Hidden from screen captures | Review; red-team attack; two re-attacks on the fixes | [2026-10-21](docs/reviews/2026-10-21-capture-protection.md) |
| Help menu, About window, window sizes | Automated tests only (no security-relevant code) | none |
| Whole codebase: all of LockPad's code, the build setup and the docs | Security review; red-team attack; fixes attacked again | [2026-10-22](docs/reviews/2026-10-22-full-codebase-check.md) |

Each change was attacked again after its fixes until no Critical, High or Medium
finding remained. The re-attacks of 2026-10-22, including a final attack on
their own fixes, are in [2026-10-22](docs/reviews/2026-10-22-re-attacks.md). Low
findings from the last attack of each change were fixed with tests or accepted
with a reason, as listed in the records. Some red-team passes reasoned from the
source instead of running code; their test ideas became automated tests.

## Why LockPad exists

LockPad started as a replacement for
[Crypto Notepad](https://crypto-notepad.github.io/), a small encrypted notepad
for Windows. Its source code (version 1.7.3) has weaknesses that matter for
private keys and PIN codes: it overwrites the file in place (an interrupted save
can leave it empty), derives its key with SHA-1 and only 1,000 iterations, has
no integrity check (a damaged file can decrypt into garbage), and lets copied
text reach Windows clipboard history.

The usual advice is a password manager such as KeePassXC. Its protection is
excellent, but it is built for password entries, not for writing and reading
notes. LockPad gives you a notepad with the same file format and protection.
