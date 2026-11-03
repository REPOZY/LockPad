# LockPad manual test checklist

Run with a throwaway notebook in %TEMP% and a throwaway password. Never use real secrets for testing. Tick each item. Windows clipboard history (Win+V) must be ON.

## First launch
- [CHECK] After the first start, `regedit` shows a value named `LockPad.exe` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\Windows Error Reporting\ExcludedApplications` (crash-dump exclusion).

## Create / unlock
- [CHECK] New notebook: mismatched passwords show "Passwords do not match."
- [CHECK] New notebook: an empty password shows "Enter a password."
- [CHECK] New notebook: weak password ("password") asks for confirmation; declining creates no file.
- [Check] The weak-password confirmation defaults to No (Enter does not accept a weak password).
- [CHECK] New notebook: typing a bare file name (no folder) shows "Enter a full path or use Browse."
- [CHECK] New notebook with a strong password opens straight into the editor; the window does not freeze during the ~1 s key derivation.
- [CHECK] New notebook with "create new key file": a backup warning names the key file's full path. Reopening without the key file fails; with it, it opens.
- [CHECK] Restart app: the last notebook path is prefilled; only the password is needed.
- [CHECK] After creating a new notebook and restarting, the unlock page shows the new notebook's path.
- [CHECK] A notebook path on a network share (`\\server\share\...`) or a mapped network drive is refused.
- [CHECK] Opening a backup copy (name ending .bak, or .bak1/.bak2/.bak3 from older versions) opens it read-only with the backup message.
- [CHECK] Wrong password shows "Wrong password or key file."
- [CHECK] Missing key file shows "Key file not found."
- [CHECK] Opening a KeePassXC password database shows the read-only warning; New/Rename/Delete/Save are disabled; nothing is ever written to that file.
- [CHECK] Enter in the password box unlocks.

## Editor
- [CHECK] Paste 200 lines; text is monospace and readable.
- [CHECK] The "Word wrap" checkbox and Alt+Z toggle wrapping; a long single-line key can be viewed unwrapped; the setting survives restart.
- [CHECK] Ctrl+Plus / Ctrl+Minus / Ctrl+0 and Ctrl+wheel zoom; size survives restart.
- [CHECK] On a non-US keyboard layout, AltGr characters (e.g. AltGr+0, AltGr+Z) still type normally.
- [CHECK] F2 and Ctrl+N while typing in the editor work and type nothing into the note.
- [CHECK] Ctrl+F opens the find bar from the editor and from the notes list; Enter finds the next match, which is clearly highlighted (in Light and Dark theme) and scrolled into view, and wraps around; Esc closes it and leaves the match selected.
- [CHECK] Filter box narrows the notes list by title and content.
- [CHECK] Rename (F2) and delete (Del, with confirmation) work. After a delete, the next note is selected and the arrow keys and Ctrl+S work without clicking.
- [CHECK] Shortcuts (Ctrl+S, Ctrl+N) work while no note is selected.

## Clipboard (with Win+V history ON)
- [CHECK] Copy text from a note with Ctrl+C: it pastes into Notepad, a browser and a terminal, and it does NOT appear in Win+V history.
- [CHECK] Same check for Ctrl+Insert, Ctrl+X, Shift+Delete, and right-click Copy / Cut.
- [CHECK] Same check for text copied from the find box, the filter box and the rename prompt.
- [CHECK] After the clear delay (default 20 s) the clipboard is empty (paste gives nothing).
- [CHECK] Copy something in another app after copying from LockPad: LockPad does not clear it.
- [CHECK] Dragging selected text out of the editor into another app does nothing. (Dragging text to move it inside the editor is also blocked; this is deliberate.)
- [CHECK] After copying a note's text and closing the app, the clipboard is empty, even with Win+V history on (the app may stay blank up to 20 s while it waits).
- [CHECK] Copy, then press Ctrl+L: the clipboard is cleared.
- [CHECK] Note titles are NOT protected: never put a secret in a title. (Ctrl+C on the delete confirmation box copies its text, including the title.)

## Saving and safety
- [CHECK] Ctrl+S: status shows "Saved"; .bak1 appears after the second save; the window shows a wait cursor for 2-3 s during a save.
- [CHECK] Ctrl+S with nothing changed does not touch the notebook or its .bak (check the files' modified time). Typing a character and deleting it again, then Ctrl+S, also writes nothing.
- [CHECK] Ctrl+Shift+S (Save As) to a new path works; to an existing file it says "File already exists."
- [CHECK] File > Save As exists and works (Ctrl+Shift+S).
- [CHECK] Save As or New notebook to a name ending in .bak<number> is refused: 'This name is reserved for backup copies. Choose another name.'
- [CHECK] Edit the file in KeePassXC while it is open in LockPad, then Ctrl+S: Overwrite / Save As / Cancel prompt. Cancel keeps "Unsaved changes".
- [CHECK] Set the .kdbx file read-only, edit, Ctrl+S: error shown, file unchanged.
- [CHECK] Close the window with unsaved edits: saved automatically.
- [CHECK] With the file read-only and unsaved edits, closing offers Save As... / Close without saving / Cancel; Cancel and Esc keep the window open.
- [CHECK] Close without saving asks 'Discard all unsaved changes? This cannot be undone.'; No (the default, also Esc) keeps the window open.
- [CHECK] Save As... from that choice to a new name saves and closes.
- [CHECK] After two saves with different content, exactly one backup exists: `<notebook>.bak` (the version before the last save). No new .bak1/.bak2/.bak3 files appear. Older .bak1-.bak3 files from earlier test runs are left untouched.
- [CHECK] Save As or New notebook to a name ending in .bak is refused with the reserved-name message.

## Lock
- [CHECK] Set idle lock to 1 minute in Tools > Settings; leave the app; it locks and the editor is cleared from the screen.
- [CHECK] Ctrl+L locks immediately.
- [CHECK] Idle lock while a dialog (Save As, Settings, a message box) is open: the notes are hidden at once behind 'Locked - close the open dialog to finish locking.' and the clipboard is cleared; closing the dialog completes the lock.
- [CHECK] Typing in the Settings or Rename window for longer than the lock time does not lock; leaving it untouched past the lock time hides the notes.
- [CHECK] With idle lock at 1 minute, put the PC to sleep for 2+ minutes with a notebook open; on wake the notebook is locked (or locks within a second).
- [CHECK] Make the file read-only, edit a note, wait for the idle lock: the unlock screen says "Unsaved changes are held in memory. Saving failed: ...". Unlocking restores the edit; "New notebook" and "Browse" are disabled on that screen.
- [CHECK] On that held-notebook screen, a wrong password shows "Wrong password or key file. Try again in N s." and the wait grows (1, 2, 4, 8 s).
- [CHECK] Closing the held-notebook screen asks twice before discarding; both default to No.
- [CHECK] Sign out of Windows with unsaved edits on a read-only file: Windows reports LockPad is preventing sign-out; the notebook is locked with the held warning.
- [CHECK] Sign out of Windows with a clean (saved) notebook open: sign-out proceeds and the clipboard is cleared.
- [CHECK] Rest the mouse cursor over the LockPad window without moving it: the idle lock still fires on time.
- [CHECK] After the idle time has passed (e.g. after waking the PC), the first mouse move or key press locks the notebook rather than keeping it open.
- [CHECK] Close the window from the taskbar ("Close window") while a dialog is open: nothing is lost; unsaved edits are saved or the window stays open.
- [CHECK] On a successful close with copied text, the window goes blank while the clipboard is cleared (up to 20 s), then closes.

## Settings and theme
- [CHECK] Tools > Settings: the cursor starts in the first box; letters cannot be typed or pasted; an empty box refuses OK with a message.
- [CHECK] Out-of-range values (0, 999) are clamped to 1-60 minutes / 5-120 seconds.
- [CHECK] View > Theme > Dark / Light / System switch immediately and survive restart.
- [CHECK] In Dark theme, every menu item (View, Theme, System, Light, Dark, Tools, Settings) is readable.
- [CHECK] (New menu) In Light and Dark theme, File, Save As, Settings and Shortcuts in the menu bar are readable.
- [CHECK] In Dark theme the unlock, create, editor and settings screens are readable (error text, warning banner, word-wrap checkbox).
- [CHECK] With Windows high contrast on, the menus are readable.

## Interop
- [CHECK] Open the notebook in KeePassXC with the same password: the "Notes" group shows every note with its text.
- [CHECK] Edit a note in KeePassXC, save, then open in LockPad: the edit is there and the notebook is NOT read-only.

## Changes since the first test round
Note: older lines above that mention "Tools > Settings" or "View > Theme" now mean the "Settings" item in the menu bar.

### Icon and menu
- [CHECK] LockPad.exe in Explorer, the window title bar and the taskbar show the notepad-with-padlock icon.
- [CHECK] The menu bar shows File, Settings and Shortcuts (no Tools, no View). Clicking Settings opens the Settings window directly; clicking Shortcuts opens the shortcuts list directly.
- [CHECK] Settings > Theme: Dark / Light / System apply after OK and survive restart.

### Shortcuts
- [CHECK] F1 opens the Shortcuts window from the editor and from the unlock screen; Esc closes it.
- [CHECK] Hovering over New, Rename, Delete and Undo delete shows their shortcut (e.g. "New note (Ctrl+N)").

### Create / unlock screens
- [CHECK] The Browse buttons read "Browse notebook..." and "Browse key file..." on both the create and the unlock screen.
- [CHECK] Filling only the key-file box and clicking Create shows the error naming "Notebook file", puts the cursor in that box and outlines it in red; typing in the box removes the outline.
- [CHECK] A bare file name in "Notebook file" shows 'Enter a full path in "Notebook file" or use Browse notebook...'.
- [CHECK] A notebook path in a folder that does not exist shows 'The folder in "Notebook file" does not exist...' with that box outlined.
- [CHECK] Answering No to the weak-password question puts the cursor back in the (emptied) password box.

### Notes
- [CHECK] New (or Ctrl+N) first asks for the note's name ("New note" preselected); OK creates it and the cursor is in the editor; Cancel or Esc creates no note.
- [CHECK] The name box (New note and Rename) shows the warning not to put passwords or other secrets in a title.
- [CHECK] Delete a note that was never saved: the message says it can be brought back with Undo delete until you lock or close, and cannot be recovered after that.
- [CHECK] Delete a note that was saved before: the message mentions Undo delete and the .bak backup file.
- [CHECK] After a delete, Undo delete (button, or Ctrl+Z in the notes list) brings the note back at its place with its text; deleting two notes and undoing twice restores both.
- [CHECK] Ctrl+Z inside the editor still undoes typing (it does not restore deleted notes).
- [CHECK] After deleting a note and pressing Ctrl+L, then unlocking again: Undo delete is greyed out.

### Locking with Windows
- [CHECK] Win+L with a notebook open (and unsaved edits): after unlocking Windows, LockPad shows its unlock screen, the edits were saved, and the clipboard is empty.
- [CHECK] Switch user, then come back: LockPad is locked.
- [CHECK] Put the PC to sleep with a notebook open: on wake, LockPad is locked.
- [CHECK] Win+L while the Settings or Rename window is open: after unlocking Windows the notes are hidden behind 'Locked - close the open dialog to finish locking.'; closing the dialog completes the lock.
- [CHECK] Press Win+L right after clicking Unlock (during the short wait): after unlocking Windows, LockPad shows its unlock screen, not the notes.

### Dialog border and title bars
- [CHECK] In Dark theme, the weak-password question, Settings, Shortcuts, the name box and the close choice each have a clearly visible grey edge against the window behind them.
- [CHECK] In Dark theme, the main window and each of those dialogs open with a dark title bar; in Light theme the title bars are light.
- [CHECK] Switching Light <-> Dark in Settings changes the main window's title bar immediately (without clicking another window and back).

## LockPad (renamed) and Change master password
Use a throwaway notebook and throwaway passwords.

### Rename
- [CHECK] The exe is LockPad.exe; the window title, taskbar and every dialog say LockPad (no earlier development name anywhere).
- [CHECK] A notebook made by an early development build (before the LockPad name) opens read-only with "This file was not created by LockPad"; Save, New, Rename and Delete are disabled.
- [CHECK] Settings (theme, idle time) are stored in %APPDATA%\LockPad.

### Change master password
- [CHECK] File > Change master password... opens a dialog with three password boxes; the cursor starts in "Current password". Esc closes it and nothing changes.
- [CHECK] The menu item is greyed out for a read-only notebook.
- [CHECK] A wrong current password shows "The current password is wrong. Try again in N s." and the wait grows (1, 2, 4, 8 s), also after closing and reopening the dialog.
- [CHECK] Different new passwords show "The new passwords do not match."; the dialog stays open.
- [CHECK] A weak new password asks for confirmation; No is the default and returns to the dialog with the new-password boxes empty.
- [CHECK] In Dark theme the dialog has a dark title bar and a visible grey edge; the weak-password question appears above it.
- [CHECK] After a successful change: the message "Master password changed" appears; the wait cursor showed during the save.
- [CHECK] Close LockPad. The notebook opens with the NEW password, not the old one.
- [CHECK] Open the .bak file (it opens read-only): it opens with the NEW password, not the old one.
- [CHECK] With a key file: after the change, the new password plus the same key file opens the notebook.
- [CHECK] With Caps Lock on, the Change master password dialog shows "Caps Lock is on." while the cursor is in any of its boxes; it disappears when Caps Lock is turned off.
- [CHECK] The dialog shows the note that the backup file will then hold your current notes.
- [CHECK] Open the dialog, type the current password, then press Win+L. After unlocking Windows the boxes are empty and the change cannot be confirmed; LockPad locks once the dialog is closed.

### Opening by double-click
- [CHECK] Set LockPad as "Open with" for .kdbx (right-click > Open with > Choose another app > LockPad.exe, tick "Always"). Double-clicking a notebook (also one on an external drive) starts LockPad with that path in "Notebook file" and the cursor in "Master password"; nothing opens until you enter the password.
- [CHECK] Double-clicking a .kdbx on a network share shows 'The file "..." cannot be opened. Notebooks must be existing files on a local drive.' with an empty "Notebook file" box.

## Switching notebooks and the new look
Use throwaway notebooks.

### Switching
- [CHECK] File menu shows New notebook..., Open notebook... (Ctrl+O), Save, Save As..., Change master password..., Lock (Ctrl+L), Exit. On the unlock screen only New notebook, Open notebook and Exit are active.
- [CHECK] In a notebook, Ctrl+O, pick another notebook: the current one is saved (if changed) and closed; the unlock screen shows the chosen file with the cursor in Master password. Cancel in the file picker changes nothing.
- [CHECK] With unsaved changes on a read-only notebook, Ctrl+O and pick a file: "Could not save" appears and ends with "Cancel keeps this notebook open."; Cancel keeps the notebook with your edits.
- [CHECK] Ctrl+O and pick a file on a network share: "Cannot open notebook" appears and the current notebook stays open.
- [CHECK] File > New notebook... from a notebook: it is closed (saved first) and the create screen appears.
- [CHECK] Ctrl+L, then Ctrl+Z in the next notebook's list: Undo delete is greyed out (nothing carried over).

### Title and empty states
- [CHECK] The title shows "notes.kdbx - LockPad", with a * after typing, without it after Ctrl+S; it shows only "LockPad" when locked.
- [CHECK] A new empty notebook shows "No notes yet. Click New (Ctrl+N) to add one."; a filter that matches nothing shows "No notes match the filter."

### Look (check in Dark and in Light)
- [CHECK] Buttons, the Theme dropdown, the File menu, check boxes, scroll bars and tooltips match the theme (no light-grey Windows controls in Dark).
- [CHECK] Unlock, Create, OK and Change password are the highlighted (accent) buttons; Yes/No and "Could not save" have no highlighted button.
- [CHECK] The create screen shows Create before Cancel.
- [CHECK] The strength bar is empty with "Type a password to see its strength." until you type, then fills red / amber / green.
- [CHECK] Press Tab through every screen and dialog: you can always see which control has focus, including on Unlock/Create in Light theme.
- [CHECK] Press Alt, then use the arrow keys through the File menu, and open the Theme dropdown with Alt+Down: the current item has a visible blue border.
- [CHECK] Right-click in the editor: the Cut/Copy/Paste menu is readable.
- [CHECK] With Windows high contrast on (Left Alt+Left Shift+Print Screen), LockPad uses the Windows high-contrast colours and everything stays readable; switching to another high-contrast theme updates LockPad.


## Help menu, About and window size
The menu bar is now File | Settings | Help (this replaces the "File, Settings and Shortcuts" item above).
- [CHECK] A fresh start opens LockPad at about 1000 x 700, centred on the screen, fully inside the screen (also on a laptop with display scaling at 150 %).
- [CHECK] Help > Keyboard shortcuts (and F1) opens the shortcuts list completely inside the LockPad window, with no scroll bar at the starting size.
- [CHECK] Make the LockPad window small (about 640 x 400) and press F1: the list is no taller than the LockPad window, scrolls with the mouse wheel and with Up/Down/Page Up/Page Down/Home/End, and Close stays visible; Tab only reaches Close.
- [CHECK] Help > About LockPad shows https://github.com/REPOZY/LockPad as text and nothing in it opens a browser. Copy link shows "Link copied."; pasting into a browser's address bar gives the address. Enter and Esc close it.
- [CHECK] Copy a secret from a note, then Help > About > Copy link, then Ctrl+L: the clipboard still holds the address (LockPad only clears its own copies).
- [CHECK] In Dark and Light theme, the About window is readable, with a dark title bar in Dark.

## Hidden from screen captures
Setting: Settings > "Hide LockPad from screenshots and screen sharing" (on by default).
- [CHECK] Settings shows the check box ticked, with the note about screenshots and phone cameras below it.
- [CHECK] With a notebook open, Win+Shift+S over LockPad: the snip shows what is behind LockPad (or nothing), never your notes. Same for the Print Screen key.
- [CHECK] Open Settings, Shortcuts or About, then Win+Shift+S: the dialog does not appear in the snip either.
- [CHECK] Share your screen in Teams, Zoom or Discord (or record with OBS / Xbox Game Bar Win+Alt+R): LockPad does not appear for the viewers or in the recording.
- [CHECK] Untick the setting and press OK: Win+Shift+S now captures LockPad normally, without restarting. Tick it again: hidden again.
- [CHECK] Close and reopen LockPad: the setting is remembered.
- [CHECK] While sharing your screen, select a note and press Del: the "Delete note" question (now a LockPad-style window; Enter = Yes, Esc = No) does not appear for the viewers.
- [CHECK] Alt+Tab and hover over the LockPad taskbar button while the setting is on: note whether the previews show your notes (expected: blank or black).

## Whole-codebase check (2026-10-22)
- [CHECK] Settings > Lock after idle = 1 minute. Type in a note (do not save) and wait: the notes disappear and "Locking. If a LockPad dialog is open, close it to finish." shows; about a second later the unlock page appears. Unlock: your text was saved.
- [CHECK] Same with no unsaved changes: the unlock page appears directly after the minute.
- [CHECK] With unsaved changes, put the PC to sleep for longer than the idle time and wake it: the notes are hidden before the unlock page appears, and your text was saved.
- [CHECK] Windows text suggestions: turn on Settings > Time & language > Typing > "Show text suggestions when typing on the physical keyboard", type a few test words (not real secrets) into a note, and note whether suggestions appear. Afterwards check whether the folder %LOCALAPPDATA%\Microsoft\InputPersonalization changed. Report what you see; leave the Windows setting off when using LockPad if suggestions appear.
- [CHECK] Building from source: `dotnet build src\LockPad.App\LockPad.App.csproj -c Release` succeeds on a fresh clone (the lock files are used; no package versions change).
