# LockPad

**An encrypted notepad for Windows, for the text you cannot afford to lose or
leak:** seed phrases, private keys, PIN codes, passwords and recovery codes.

Every notebook is a standard, encrypted **KDBX 4.1** file, the format of
KeePass and KeePassXC. If LockPad ever stops working, KeePassXC still opens
your notebooks.

<!-- PROMO VIDEO: on github.com, edit this file and drag marketing/video/out/lockpad-promo.mp4 onto this spot.
     GitHub uploads it and inserts a https://github.com/user-attachments/assets/... link; keep that link on its own
     line (blank line above and below) so it plays inline. A video file committed to the repo does not play. -->

> **Status: pre-release.** LockPad passes its full automated test suite and has
> been through AI-assisted security reviews and red-team attacks, but not a
> third-party audit. See [Security reviews](#security-reviews).

## Highlights

- **Strong, standard encryption.** AES-256 with Argon2id key derivation, through
  the official, unmodified KeePass library.
- **Saving can't destroy your notebook.** Each save writes a new file, reads it
  back to verify every note, and only then replaces the old one. The previous
  version is kept as a backup.
- **Locks itself.** After 5 minutes without input (adjustable), when Windows
  locks, and before sleep. Unsaved changes are saved first.
- **A careful clipboard.** Copied text stays out of Windows clipboard history
  and the cloud clipboard, and is cleared after 20 seconds.
- **Hidden from screen captures.** Left out of screenshots, recordings and
  screen sharing (best effort; see [SECURITY.md](SECURITY.md)).
- **Offline by design.** Never connects to the network: no cloud sync, no
  updater, no telemetry, no plugins.
- **Portable.** Four files in one folder. Runs from a USB stick; nothing to
  install.
- **Open source.** GPL-2.0-or-later.

## Download

There is no release yet. When there is, download the zip from the
[Releases](https://github.com/REPOZY/LockPad/releases) page, unzip it, and
check it before you run it: in PowerShell, in the unzipped folder, run
`Get-FileHash * -Algorithm SHA256` and compare every hash with `SHA256SUMS.txt`.

Until then, [build LockPad from source](CONTRIBUTING.md#building-lockpad).

**Requirements:** Windows 10 (version 1903 or later) or Windows 11. .NET
Framework 4.8 is already part of Windows.

LockPad is not code-signed, so Windows SmartScreen may warn about it. After
checking the hashes, choose **More info > Run anyway**.

## Quick start

1. Start `LockPad.exe` and click **New notebook...**.
2. Choose where to save it, for example `Documents\private.kdbx`.
3. Choose a strong master password and type it twice. **Write it down and keep
   it somewhere safe: nobody can recover a forgotten password.**
4. Click **Create**. Add notes with **New** (Ctrl+N) and type or paste into the
   editor. Ctrl+S saves.
5. **Keep copies** of your notebook on a second USB stick or drive. LockPad's
   own backup only goes one save back.

Next time, LockPad remembers your notebook: type the password and press Enter.
The [user guide](docs/user-guide.md) covers everything else.

## How it protects you

| Area | What LockPad does |
| --- | --- |
| Encryption | KDBX 4.1, AES-256, Argon2id (at least 64 MiB, tuned to about 1 s on your PC); enforced again on every save |
| Saving | Verify-before-replace, one backup, asks before overwriting changes made by another program |
| Locking | Idle timeout, Ctrl+L, Windows lock, sleep; saves first, and holds unsaved changes safely if that fails |
| Clipboard | Excluded from history and cloud sync, cleared after a delay, on lock and on exit |
| Screen | Hidden from screenshots, recordings and screen sharing (on by default) |
| Files | Never writes to a file it did not create; KeePass databases, backups and linked files open read-only |
| Crashes | Kept out of Windows crash reports, so notes do not end up in crash files |

The details are in [SECURITY.md](SECURITY.md).

## What it cannot protect against

No notepad can. In short:

- **Malware already on your PC** can read what you type and see.
- **A forgotten master password or a lost key file.** There is no recovery.
- **Note titles are not secret.** Never put a secret in a title.
- **Windows can write memory to disk** (page file, hibernation, blue-screen
  dumps). Turn on **BitLocker** to keep those encrypted.
- **Losing your only copy.** Keep your own copies on a separate drive.

The full list is in [SECURITY.md](SECURITY.md#what-lockpad-does-not-protect-against).

## Your way out: KeePassXC

Open your notebook in [KeePassXC](https://keepassxc.org/) with the same password
(and key file). Your notes are the entries in the **Notes** group. The
automated tests check that LockPad and KeePassXC can each read and save the
other's changes.

## Security reviews

LockPad's code was reviewed and attacked by AI agents (Claude). Every change was
attacked again after its fixes until no Critical, High or Medium finding
remained. This is not a third-party audit. Every finding and what was done
about it is listed in [SECURITY.md](SECURITY.md#security-reviews) and
[docs/reviews](docs/reviews).

## Documentation

- [User guide](docs/user-guide.md): using LockPad, shortcuts, settings, files,
  recovery.
- [SECURITY.md](SECURITY.md): how your data is protected, the limits, the
  reviews.
- [CONTRIBUTING.md](CONTRIBUTING.md): building from source, tests, project
  rules.

## License

GNU General Public License, version 2 or later (GPL-2.0-or-later); see
[LICENSE](LICENSE) and [NOTICE](NOTICE). LockPad includes KeePassLib, part of
[KeePass Password Safe](https://keepass.info/), Copyright (C) 2003-2026 Dominik
Reichl, under the same license. LockPad is not affiliated with or endorsed by
the KeePass or KeePassXC projects.
