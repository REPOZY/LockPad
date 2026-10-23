# LockPad user guide

- [Create a notebook](#create-a-notebook)
- [Open a notebook](#open-a-notebook)
- [Work with notes](#work-with-notes)
- [Save](#save)
- [Change the master password](#change-the-master-password)
- [Read-only notebooks](#read-only-notebooks)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Settings](#settings)
- [Files LockPad creates](#files-lockpad-creates)
- [Recover a notebook](#recover-a-notebook)
- [Open a notebook in KeePassXC](#open-a-notebook-in-keepassxc)

How LockPad protects your data is described in [SECURITY.md](../SECURITY.md).

## Create a notebook

1. On the start screen, click **New notebook...** (or **File > New notebook...**).
2. Choose where to save the file: **Browse...**, or type a full path such as
   `C:\Users\you\Documents\private.kdbx`. Paths longer than 231 characters are
   refused: LockPad needs room for its backup and temporary file names.
3. Enter your master password twice. The strength meter shows how strong it is:
   below 80 bits you get a warning; below 60 bits you must confirm that you want
   a weak password (No is the default).
4. Optional: tick **Create a new key file at this location**. A key file is a
   second factor: without it the notebook cannot be opened, even with the right
   password. **Back up the key file. If you lose it, the notebook is gone.**
5. Click **Create**. The notebook opens straight into the editor.

## Open a notebook

- LockPad remembers your last notebook, so normally you only type your password
  and press Enter. **Browse...** opens a different file.
- **Double-click to open:** right-click a `.kdbx` file > Open with > Choose
  another app > browse to `LockPad.exe` and tick "Always". LockPad then starts
  with that file filled in; you still enter the password.
- **Open another notebook** with **File > Open notebook...** (Ctrl+O). The
  current one is saved and closed first. If saving fails, LockPad asks: Save
  As..., Close without saving, or Cancel to stay in the current notebook.
  **File > New notebook...** works the same way, then shows the create screen.
- While a notebook is open, the window title shows its file name (for example
  `notes.kdbx - LockPad`, with a `*` while there are unsaved changes). On every
  other screen it is just `LockPad`.

## Work with notes

- **New** (Ctrl+N) asks for the note's name ("New note" is preselected, so you
  can type over it), adds the note and puts the cursor in the editor. Cancel or
  Esc adds nothing.
- **Rename** (F2) changes the title. **Delete** (Del) removes the note after
  confirmation.
- **Undo delete** (or Ctrl+Z while the notes list has focus) brings back the last
  deleted note at its old place; press it again for the one before. This works
  until you lock or close LockPad, also after a save. Ctrl+Z in the editor still
  undoes typing.
- Type or paste into the editor on the right. The filter box above the list
  narrows it by title and text. Ctrl+F opens the find bar: Enter jumps to the
  next match (and wraps around), Esc closes it.
- The status bar shows "Saved" or "Unsaved changes", a read-only badge when it
  applies, and the time left until the auto-lock.

## Save

- Ctrl+S saves. LockPad also saves when it locks and when you close it.
- **File > Save As...** (Ctrl+Shift+S) saves a copy under a new name and then
  continues in that copy. From then on the copy is a separate notebook: LockPad
  never overwrites one with the other.
- If another program changed the file since you opened it, LockPad asks whether
  to overwrite it, save a copy elsewhere, or cancel. Overwrite works only if the
  changed file is still this notebook: it opens with your password, is a LockPad
  notebook whose hidden notebook id (if it has one) matches, and does not use
  heavier key settings than your notebook. Otherwise LockPad says so, changes nothing, and you can use Save
  As. A different notebook that happens to have the same password is never
  overwritten.

## Change the master password

- **File > Change master password...** changes the password of the open
  notebook. It is disabled for read-only notebooks and while LockPad is locked.
- Enter the current password, then the new one twice. A weak new password asks
  for confirmation, as when you create a notebook.
- After a wrong current password you wait before the next try: 1, 2, 4, then
  8 seconds. Closing and reopening the dialog does not skip the wait.
- Only the password changes. A notebook with a key file keeps the same key file.
  If a different file now sits at that key file's path, the dialog says "The
  current password is wrong."
- LockPad saves twice, so both the notebook and its backup (`.bak`) open only
  with the new password. The `.bak` then holds your current notes, so notes you
  deleted earlier can no longer be restored from it.
- If the backup could not be re-locked (for example because another program held
  it), LockPad says so and re-locks it the next time it saves, also when it
  locks or closes.
- Copies you made yourself (another USB stick, or `.bak.kept` files) still open
  with the old password.

## Read-only notebooks

These open read-only, with a message explaining why:

- a KDBX file LockPad did not create (for example your KeePassXC password
  database);
- a backup copy;
- a notebook reached through a symbolic link.

You can read and copy, but New, Rename, Delete and Save are disabled, and
nothing is ever written to that file.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+O | Open another notebook |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+N | New note |
| F2 | Rename note |
| Del (in the notes list) | Delete note |
| Ctrl+Z (in the notes list) | Undo delete |
| Ctrl+F | Find in note |
| Ctrl+L | Lock now |
| Alt+Z | Word wrap on or off |
| Ctrl+Plus / Ctrl+Minus / Ctrl+0 | Zoom in / out / reset |
| Ctrl+mouse wheel | Zoom |
| Enter (on the unlock screen) | Unlock |
| F1 | Show all shortcuts (also **Help > Keyboard shortcuts**) |

## Settings

Open **Settings** in the menu bar. Word wrap is the checkbox above the editor
(Alt+Z), and the font size follows zoom.

| Setting | Default | Range |
| --- | --- | --- |
| Lock after idle | 5 minutes | 1-60 minutes |
| Clear clipboard after | 20 seconds | 5-120 seconds |
| Theme | System | System / Light / Dark |
| Hide LockPad from screenshots and screen sharing | On | On / off |
| Font size | 14 | 8-48 (zoom) |
| Word wrap | On | On / off |

"System" follows the Windows light/dark setting when LockPad starts and when you
choose it. Settings are stored in `%APPDATA%\LockPad\settings.json`: only the
path of your last notebook and the values above, never passwords or note text.
If the file is damaged, LockPad uses the defaults. Run one LockPad at a time:
with two open, a setting changed in one can be overwritten by the other when it
saves its own settings.

**Help > About LockPad** shows the project's GitHub address with a **Copy link**
button; LockPad itself never opens a browser.

## Files LockPad creates

| File | Where | What it is |
| --- | --- | --- |
| Your notebook | Where you saved it, e.g. `private.kdbx` | Encrypted KDBX 4.1 |
| Backup | `private.kdbx.bak`, next to the notebook | The version before your last save, encrypted; each save replaces it |
| Kept file | `private.kdbx.bak.kept-<date>-<time>` | A file named like the backup that LockPad did not create. Your data; LockPad never deletes it |
| Temporary file | `private.kdbx.<random>.tmp` | Exists only during a save. A leftover after a crash or failed swap is encrypted but may be the only copy of your newest edits: copy it, rename the copy to `.kdbx` and open it before deleting anything |
| Key file | Where you chose | Only if you asked LockPad to create one. Back it up |
| Settings | `%APPDATA%\LockPad\settings.json` | No secrets |
| Crash-report exclusion | Registry value `LockPad.exe` under `HKCU\Software\Microsoft\Windows\Windows Error Reporting\ExcludedApplications` | Keeps notes out of crash reports; delete the value to undo |

LockPad writes nothing else. It never connects to the network and runs no
background service. To remove LockPad, delete its folder (and, if you like, the
settings folder and the registry value above).

**On a PC that is not yours** (LockPad on a USB stick): the settings file
remembers where your last notebook is, and Windows' own Open and Save dialogs
can remember recently used files and folders. Delete the settings folder
afterwards; the notebook itself stays encrypted wherever it is.

## Recover a notebook

- **A note you just deleted:** **Undo delete** (or Ctrl+Z in the notes list)
  before you lock or close LockPad. A note that was never saved is not in any
  backup, so after locking or closing it is gone.
- **From the backup:** close LockPad. In File Explorer, copy `private.kdbx.bak`
  and rename the copy, for example to `private-restored.kdbx`. Open it in LockPad
  with the same password and key file. Do this before you save the notebook
  again: the next save replaces the `.bak`. (A backup opened under its `.bak`
  name opens read-only on purpose, so your recovery cannot be overwritten.)
- **From KeePass version history:** each note keeps up to 10 earlier versions
  inside the file. LockPad has no screen for them; KeePassXC shows them under the
  entry's History.
- **If LockPad will not start:** open the notebook in KeePassXC (below).

## Open a notebook in KeePassXC

1. In [KeePassXC](https://keepassxc.org/), choose **Open Database**.
2. Select your `.kdbx` notebook and enter the same password (and key file).
3. Your notes are the entries in the **Notes** group: the entry title is the note
   title, and its Notes field holds the text.

You can edit notes in KeePassXC and keep using the file in LockPad afterwards.
The automated tests check that LockPad and KeePassXC 2.7.12 can each read and
save the other's changes.
