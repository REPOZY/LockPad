# LockPad marketing video - design

_Date: 2026-10-22 | Status: approved_

## Goal

A ~60 second product video that shows what LockPad is, how it works and its key
features, for the GitHub README, a website or YouTube. Built with Remotion
(React-based video) so it can be edited and re-rendered from source.

## Scope

- One composition, `LockPadPromo`: 1920x1080, 30 fps, 1830 frames (61 s): a 1 s still title card,
  then the 60 s storyboard below. GitHub's README player shows the first frame as its preview, so the
  video opens on the settled logo instead of an empty background (added 2026-10-22).
- LockPad's UI is recreated as React components (no screen recordings): exact
  colors from `src/LockPad.App/Themes/Dark.xaml`, layouts from `EditorView.xaml`,
  `UnlockView.xaml` and `MainWindow.xaml`, icon from `docs/icon/lockpad.svg`.
- No audio.

## Non-goals

- Vertical (9:16) cut, music or sound effects, real screen recordings, a
  light-theme variant.
- Offline first render: the first render downloads Chrome Headless Shell and the
  fonts, so it needs internet once.

## Location and repository impact

- Project in `marketing/video/` inside the LockPad repo, using npm and pinned
  Remotion 4.0.x versions.
- `.gitignore` gets `marketing/video/node_modules/` and `marketing/video/out/`.
  The source is committed; dependencies and rendered videos are not.
- No effect on the C# build or tests: `LockPad.sln` only builds its own
  projects, and `RepositoryGuardTests` only scans `src/`, `tests/`, `lib/` and
  `README.md`. The old product name must not appear in `marketing/` either, to
  keep the rename complete.

## Visual style

- Background: near-black navy `#0B1120` with a soft radial glow in the icon's
  `#1E40AF` and the app accent `#4DA3FF`.
- Fonts: Inter for headlines and captions, JetBrains Mono for editor text (the
  closest Google Font to LockPad's Cascadia Mono). Both are loaded with
  `@remotion/google-fonts`.
- The app window uses Windows 11 style chrome (title bar `notes.kdbx - LockPad`,
  menu File | Settings | Help, status bar) and the Dark theme colors.
- Motion: springs for entrances, short fades and slides between scenes
  (`@remotion/transitions`), no 3D.

## Storyboard

| # | Time | Scene | Content |
|---|------|-------|---------|
| 1 | 0-5 s | Hook | "Private keys. Seed phrases. PIN codes." types in, then "Where do you keep them?" |
| 2 | 5-9 s | Logo | The icon draws in and the padlock snaps shut. "LockPad" plus the tagline "The encrypted notepad for text you can't afford to lose or leak." |
| 3 | 9-15 s | Unlock | Recreated unlock screen (Notebook file, Master password, Key file (optional), New notebook... / Unlock). Password dots type in, Unlock is pressed, the editor slides in. |
| 4 | 15-22 s | Editor | Notes list (Seed phrase, Recovery codes, Bank PIN, SSH key), filter box, New/Rename/Delete/Undo delete, text typing in the editor, status bar. Captions: "AES-256 + Argon2id" and "Standard KDBX 4.1 - KeePassXC opens it too". |
| 5 | 22-30 s | Safe save | Ctrl+S key chip, then a four-step pipeline: write a temp file, re-open and verify, swap in, keep the old version as `.bak`. Caption: "Saving can't destroy your notebook." |
| 6 | 30-36 s | Auto-lock | The status bar countdown runs out and the window locks to the unlock screen. Chips: Ctrl+L, Win+L, Sleep. Caption: "Locks itself." |
| 7 | 36-42 s | Clipboard | Copy, then a clipboard chip with a 20 s ring that clears. Win+V history stays empty. Caption: "Kept out of clipboard history. Cleared automatically." |
| 8 | 42-48 s | Screen capture | Split view: the desktop shows LockPad, while the "You're sharing your screen" view shows an empty desktop where the window would be. Caption: "Hidden from screenshots and screen sharing." (matches the app's own setting name; the README lists exceptions) |
| 9 | 48-54 s | Feature grid | Tiles: No network code; No cloud, no telemetry; Portable (4 files, runs from USB); Read-only for files it didn't create; Light & dark; Open source (GPL). |
| 10 | 54-60 s | Call to action | Logo, "Free & open source - Windows 10/11", the GitHub URL. |

Scene times include transition overlaps. Small timing shifts are allowed as
long as the total stays 60 s.

## Copy rules

- Every claim must be stated in `README.md`. In particular, never use
  "audited" (the README says it is not a third-party audit), "unhackable" or
  "download now" (there are no ready-made downloads yet).
- Captions have at most 8 words and each stays on screen at least 2 s. If a
  scene does not fit, drop a feature instead of speeding up text.
- Note titles and text in the demo are obviously fake (for example
  `word1 word2 ...`, `1234`). They are never realistic secrets.
- The GitHub URL (`github.com/REPOZY/LockPad`) is planned; the repository does
  not exist yet. It lives in one constant in `src/theme.ts`.

## Architecture

```
marketing/video/
  package.json, tsconfig.json, remotion.config.ts, README.md
  src/index.ts                registerRoot
  src/Root.tsx                registers the LockPadPromo composition
  src/LockPadPromo.tsx        TransitionSeries of the ten scenes
  src/theme.ts                colors, fonts, URL, timing constants
  src/components/             AppWindow, UnlockScreen, EditorScreen, KeyChip,
                              Caption, LockPadIcon, Background
  src/scenes/                 one file per scene (Hook, Logo, Unlock, Editor,
                              SafeSave, AutoLock, Clipboard, ScreenCapture,
                              Features, CallToAction)
```

- Scenes are pure functions of `useCurrentFrame()`. They have no props beyond
  what is reused, so each scene can be previewed on its own in Remotion Studio.
- `LockPadIcon` inlines the paths of `docs/icon/lockpad.svg` as JSX, so its parts
  (page, lines, shackle, body) can be animated separately. There is no
  `public/` folder.
- The UI components take simple, nullable props (for example `typedPassword`,
  `selectedNote`, `statusText`) so scenes can drive them frame by frame.

## Verification

1. `npx tsc --noEmit` passes.
2. `npx remotion still LockPadPromo --frame=<n>` for one frame per scene. Each
   still is inspected for layout, legibility and match with the real app.
3. `npx remotion render LockPadPromo out/lockpad-promo.mp4` succeeds. The
   output is 1920x1080 and 60 s long, checked with `npx remotion ffprobe`.
4. `dotnet test` is not needed (no C# changes). A grep confirms the old product
   name does not appear in `marketing/`.

## Risks

1. The recreated UI looks fake. Mitigation: exact XAML colors and layouts, and
   per-scene stills reviewed before the full render.
2. Too much text for 60 s. Mitigation: the caption rules above.
3. A security claim overreaches. Mitigation: the copy rules above.
4. A recreated UI differs from the real app (for example the light theme), which
   can feel like bait-and-switch. Mitigation: stay close to the real Dark theme;
   the video shows nothing the app does not do.

## License note

Remotion is free for individuals and companies with up to 3 employees; larger
companies need a company license. The video project's own code is part of this
repository (GPL-2.0-or-later).
