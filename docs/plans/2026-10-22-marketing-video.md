# LockPad Marketing Video Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers-optimized:subagent-driven-development (recommended) or superpowers-optimized:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Remotion project in `marketing/video/` that renders a 60 s, 1920x1080, 30 fps LockPad product video.
**Architecture:** One composition `LockPadPromo` built from ten scene components in a `TransitionSeries` (15-frame fades/slides between scenes). Scenes drive shared UI components (a recreated LockPad window, unlock screen, editor screen, key chips, captions) frame by frame with `interpolate` and `spring`. All colors, fonts, timings and the URL live in `src/theme.ts`.
**Tech Stack:** Remotion 4.0.537 (`remotion`, `@remotion/cli`, `@remotion/transitions`, `@remotion/google-fonts`), React 19.2.3, TypeScript 5.9.3, npm, Node 24.
**Spec:** `docs/specs/2026-10-22-marketing-video-design.md`
**Assumptions:**
- Assumes internet access for `npm install` and the first render (Chrome Headless Shell and Google Fonts download). Will NOT render offline on a fresh machine.
- Assumes Remotion 4.0.537's blank template config (`Config.setRspack(true)` etc.) works as published. If rspack fails on this machine, remove that one line (webpack fallback).

## Global Constraints

- All commands run from `C:\Users\Tjerk Pieksma\Documents\Github\LockPad\marketing\video` unless stated. Use `npx`, not `bunx` (this is not the Remotion monorepo).
- Exact versions, no `^`/`~`: remotion, @remotion/cli, @remotion/transitions, @remotion/google-fonts = `4.0.537`; react, react-dom = `19.2.3`; @types/react = `19.2.7`; @types/web = `0.0.166`; typescript = `5.9.3`.
- Composition: id `LockPadPromo`, width 1920, height 1080, fps 30, durationInFrames 1800.
- Colors (Dark.xaml): WindowBackground `#1E1E1E`, PanelBackground `#2B2B2B`, EditorBackground `#171717`, EditorForeground `#E6E6E6`, Accent `#4DA3FF`, Border `#4A4A4A`, Muted `#9A9A9A`, Error `#FF6B6B`, PrimaryBackground `#0E639C`, StrengthStrong `#6CCB5F`, StrengthFair `#FFB900`. Stage: background `#0B1120`, glow `#1E40AF` and `#4DA3FF`. Icon: navy `#1E40AF`, shackle `#172554`, outline `#475569`, lines `#94A3B8`, fold `#E2E8F0`.
- Fonts: Inter (headlines/captions/UI), JetBrains Mono (editor text), via `@remotion/google-fonts/Inter` and `@remotion/google-fonts/JetBrainsMono`, weights 400/600/700 (Inter) and 400 (Mono), subset `latin`.
- Copy: every claim must be in LockPad's README. Never "audited", "unhackable", "download now". Captions: max 8 words, on screen >= 60 frames. Demo note text is obviously fake (`word1 word2 ...`, `1234`).
- URL constant: `github.com/REPOZY/LockPad` (repo planned, not yet created).
- The old product name (`Kdbx` + `Pad`) must not appear anywhere under `marketing/`.
- New internal props use `T | null`, not `?:`.
- No Math.random / Date in render code (Remotion renders frames in parallel; use `random(seed)` from `remotion` if randomness is needed).

## Review Focus

1. Text overflow: long captions or note text clipping out of the window or frame at 1920x1080. Expected: every text fits within its container in every still.
2. Fonts not loaded at render time (fallback font in output). Expected: `loadFont()` is called at module level in `theme.ts`, so render waits for fonts.
3. Animation values outside [0,1] (springs overshooting into negative opacity or widths). Expected: every `interpolate` used for opacity/width uses `extrapolateLeft: 'clamp', extrapolateRight: 'clamp'`.
4. Scene timing drift: sum of scene durations minus transitions not equal to 1800. Expected: `npx remotion compositions` reports 1800 frames.
5. Scene looks wrong only mid-transition (blank frames between scenes). Expected: stills at transition midpoints show both scenes blending, never an empty frame.

---

## File Structure

```
marketing/video/
  package.json, tsconfig.json, remotion.config.ts, .prettierrc, README.md
  src/index.ts                registerRoot(RemotionRoot)
  src/Root.tsx                <Composition id="LockPadPromo" .../> plus a <Folder name="Scenes"> with one
                              composition per scene (Scene-Hook, Scene-Logo, ...) for previewing and stills
  src/theme.ts                COLORS, FONTS, URL, SCENE_FRAMES, TRANSITION_FRAMES, TOTAL_FRAMES
  src/LockPadPromo.tsx        TransitionSeries of all scenes over <Background/>
  src/components/Background.tsx   navy stage + slow radial glows
  src/components/LockPadIcon.tsx  inline icon with animatable parts
  src/components/AppWindow.tsx    Win11 window chrome, menu, status bar
  src/components/UnlockScreen.tsx recreated UnlockView
  src/components/EditorScreen.tsx recreated EditorView
  src/components/KeyChip.tsx      keyboard key badge
  src/components/Caption.tsx      headline + optional subline, animated in
  src/scenes/Hook.tsx, Logo.tsx, Unlock.tsx, Editor.tsx, SafeSave.tsx,
             AutoLock.tsx, Clipboard.tsx, ScreenCapture.tsx, Features.tsx,
             CallToAction.tsx
```

Testing note: this is a video, not a library. "Tests" are `tsc --noEmit`, `remotion compositions` (duration) and rendered stills inspected visually (Read the PNG). Stills go to `out/stills/` (gitignored).

---

### Task 1: Scaffold project and composition skeleton

**Files:**
- Create: `marketing/video/package.json`, `tsconfig.json`, `remotion.config.ts`, `.prettierrc`, `src/index.ts`, `src/Root.tsx`, `src/theme.ts`, `src/components/Background.tsx`, `src/LockPadPromo.tsx`
- Modify: `.gitignore` (repo root)

**Interfaces:**
- Consumes: nothing
- Produces:
  - `theme.ts`: `COLORS` (object with keys `stage, glowNavy, glowBlue, window, panel, editor, text, accent, border, muted, error, primary, strong, fair, iconNavy, iconShackle, iconOutline, iconLines, iconFold`), `FONTS = {sans: string, mono: string}`, `GITHUB_URL = 'github.com/REPOZY/LockPad'`, `TRANSITION_FRAMES = 15`, `SCENE_FRAMES` (object below), `TOTAL_FRAMES` (computed = sum(SCENE_FRAMES) - 9 * TRANSITION_FRAMES).
  - `Background: React.FC` (absolute fill).
  - `LockPadPromo: React.FC`.

**Security flag:** none

- [x] **Step 1: Write package.json**

```json
{
  "name": "lockpad-video",
  "version": "1.0.0",
  "description": "LockPad marketing video (Remotion)",
  "private": true,
  "license": "GPL-2.0-or-later",
  "scripts": {
    "dev": "remotion studio",
    "render": "remotion render LockPadPromo out/lockpad-promo.mp4",
    "typecheck": "tsc --noEmit"
  },
  "dependencies": {
    "@remotion/cli": "4.0.537",
    "@remotion/google-fonts": "4.0.537",
    "@remotion/transitions": "4.0.537",
    "react": "19.2.3",
    "react-dom": "19.2.3",
    "remotion": "4.0.537"
  },
  "devDependencies": {
    "@types/react": "19.2.7",
    "@types/web": "0.0.166",
    "typescript": "5.9.3"
  },
  "engines": { "node": "^20.19.0 || ^22.13.0 || >=24" }
}
```

- [x] **Step 2: Write tsconfig.json, remotion.config.ts, index.ts** — copied from Remotion's `template-blank` (tsconfig: target ES2018, module Preserve, moduleResolution Bundler, jsx react-jsx, strict, noEmit, lib es2015, esModuleInterop, skipLibCheck, noUnusedLocals, exclude remotion.config.ts; config: setRspack(true), setVideoImageFormat("jpeg"), setOverwriteOutput(true), setDefaultPremountInSeconds(2)).

- [x] **Step 3: Write theme.ts** with the Global Constraints colors, fonts loaded at module level (`loadFont('normal', {weights, subsets: ['latin']})`, `FONTS.sans = Inter fontFamily`, `FONTS.mono = JetBrains Mono fontFamily`), and:

```ts
export const SCENE_FRAMES = {
  hook: 165, logo: 135, unlock: 195, editor: 225, safeSave: 255,
  autoLock: 195, clipboard: 195, screenCapture: 195, features: 195, cta: 180,
} as const; // sum 1935 - 9*15 = 1800
```

- [x] **Step 4: Background, LockPadPromo (renders `<Background/>` only for now), Root** — Root registers `LockPadPromo` with `durationInFrames={TOTAL_FRAMES}`, 1920x1080, fps 30. Background: `#0B1120` fill plus two large blurred radial gradients (navy top-left, blue bottom-right) that drift a few % over the composition using `useCurrentFrame()`.

- [x] **Step 5: Append to repo `.gitignore`**

```
# Marketing video (Remotion): dependencies and rendered output
marketing/video/node_modules/
marketing/video/out/
```

- [x] **Step 6: Install and verify**

Run: `npm install` then `npx tsc --noEmit` then `npx remotion compositions`
Expected: install OK; tsc exits 0; output lists `LockPadPromo  30  1920x1080  1800 (60.00 sec)`.

- [x] **Step 7: Commit** — `git add .gitignore marketing/video` (node_modules excluded by .gitignore), message `Add Remotion marketing video skeleton`.

---

### Task 2: Shared visual components

**Files:**
- Create: `src/components/LockPadIcon.tsx`, `AppWindow.tsx`, `KeyChip.tsx`, `Caption.tsx`

**Interfaces:**
- Consumes: `COLORS`, `FONTS` from `src/theme.ts`
- Produces:
  - `LockPadIcon: React.FC<{size: number; pageProgress: number; shackleProgress: number}>` — pageProgress 0..1 draws the page in (stroke dash + lines growing); shackleProgress 0..1 moves the shackle from open (raised 40 px in the 256 viewBox, rotated open) to closed. Paths exactly from `docs/icon/lockpad.svg`.
  - `AppWindow: React.FC<{title: string; statusLeft: string | null; statusRight: string | null; width: number; height: number; children: React.ReactNode}>` — Win11 title bar (small LockPad icon, title, -/□/× glyphs), menu bar `File  Settings  Help`, content area `COLORS.window`, status bar `COLORS.panel` shown only when `statusLeft !== null`. Rounded 8 px corners, 1 px `#787878` border, soft drop shadow.
  - `KeyChip: React.FC<{keys: string[]; pressed: boolean}>` — keys joined with `+`, each a rounded key cap; pressed = scale 0.94 and accent border.
  - `Caption: React.FC<{title: string; subtitle: string | null; startFrame: number; align: 'left' | 'center'}>` — title Inter 700 64 px, subtitle Inter 400 32 px `COLORS.muted`; springs in (opacity + 24 px rise) from `startFrame`.

**Security flag:** none

- [x] **Step 1: Implement the four components.**
- [x] **Step 2: Temporary still check** — temporarily render all four in `LockPadPromo` (icon at pageProgress 1/shackleProgress 1, an AppWindow 1200x700 with status `Saved` / `Locks in 4:59`, KeyChip `['Ctrl','S']`, a Caption). Run `npx remotion still LockPadPromo out/stills/components.png --frame=0`. Read the PNG. Expected: icon matches `docs/icon/preview.png` dark variant; window chrome readable; nothing clipped. Revert LockPadPromo to Background-only.
- [x] **Step 3: Verify** — `npx tsc --noEmit` exits 0.
- [x] **Step 4: Commit** — `Add shared video components`.

---

### Task 3: Recreated LockPad screens

**Files:**
- Create: `src/components/UnlockScreen.tsx`, `src/components/EditorScreen.tsx`

**Interfaces:**
- Consumes: `COLORS`, `FONTS`
- Produces:
  - `UnlockScreen: React.FC<{passwordLength: number; unlockPressed: boolean; message: string | null}>` — layout of `UnlockView.xaml`: 480 px wide centered column (scale up x1.5 for video legibility), heading "Unlock notebook" 20 px, labels "Notebook file", "Master password", "Key file (optional)", path box `C:\Users\you\Documents\private.kdbx` with button "Browse notebook…", password box showing `passwordLength` bullets `●`, key file box empty with "Browse key file…", bottom row "New notebook…" (left) and primary "Unlock" (right, `COLORS.primary`, darker when `unlockPressed`). `message` (muted) under the buttons when non-null (e.g. "Unlocking…").
  - `EditorScreen: React.FC<{notes: string[]; selectedIndex: number; text: string; caretVisible: boolean; selection: [number, number] | null}>` — layout of `EditorView.xaml`: 240 px sidebar ("Filter" label + box, list on `COLORS.editor`, selected row `#094771`, 2x2 buttons New/Rename/Delete/Undo delete), 5 px splitter `COLORS.border`, right panel with "Word wrap" checkbox row on `COLORS.panel`, editor box `COLORS.editor` with `text` in `FONTS.mono` 16 px (scaled with the window), caret `|` when `caretVisible`, `selection` range highlighted `#4DA3FF` at 40% opacity. All sizes x1.5.

**Security flag:** none

- [x] **Step 1: Implement both screens.**
- [x] **Step 2: Still check** — temporarily render each inside `AppWindow` 1500x860 in `LockPadPromo`; editor with notes `['Seed phrase','Recovery codes','Bank PIN','SSH key']`, selectedIndex 0, text `word1 word2 word3 word4\nword5 word6 word7 word8`. `npx remotion still ... out/stills/screens-unlock.png` and `screens-editor.png`. Read both PNGs and compare against the XAML layouts. Expected: labels and order match the XAML; no clipping. Revert LockPadPromo.
- [x] **Step 3: Verify** — `npx tsc --noEmit` exits 0.
- [x] **Step 4: Commit** — `Add recreated LockPad screens`.

---

### Task 4: Scenes 1-2 (Hook, Logo)

**Files:** Create `src/scenes/Hook.tsx`, `src/scenes/Logo.tsx`; Modify `src/Root.tsx`

**Interfaces:**
- Consumes: `COLORS`, `FONTS`, `LockPadIcon`, `Caption`
- Produces: `Hook: React.FC`, `Logo: React.FC` (no props; each runs from its own frame 0, length `SCENE_FRAMES.hook` / `.logo`)

**Security flag:** none

- [x] **Step 1: Hook** — three phrases "Private keys." / "Seed phrases." / "PIN codes." type in character by character (Inter 700 88 px, staggered ~25 frames each, stacked left-aligned), then from frame ~100 they dim to 35% and "Where do you keep them?" springs in below in `COLORS.accent`.
- [x] **Step 2: Logo** — `LockPadIcon` size 260 centered: pageProgress 0→1 over frames 0-35, shackleProgress 0→1 spring from frame 35 (snap). Then wordmark "LockPad" (Inter 700 120 px) and tagline "The encrypted notepad for text you can't afford to lose or leak." (Inter 400 36 px, muted) fade/rise in from frame 55.
- [x] **Step 3: Scene compositions and stills** — in Root, inside `<Folder name="Scenes">`, register `Scene-Hook` and `Scene-Logo` (1920x1080, 30 fps, durationInFrames = `SCENE_FRAMES.hook` / `.logo`), each rendering `<AbsoluteFill><Background/><Scene/></AbsoluteFill>`. Run `npx remotion still Scene-Hook out/stills/hook-60.png --frame=60`, and likewise Hook frame 140, Logo frames 30 and 110. Read PNGs. Expected: text legible, nothing clipped, padlock closed in last still.
- [x] **Step 4: `npx tsc --noEmit`** exits 0. **Commit** — `Add hook and logo scenes`.

---

### Task 5: Scenes 3-4 (Unlock, Editor)

**Files:** Create `src/scenes/Unlock.tsx`, `src/scenes/Editor.tsx`; Modify `src/Root.tsx`

**Interfaces:**
- Consumes: `AppWindow`, `UnlockScreen`, `EditorScreen`, `Caption`, `KeyChip`, `COLORS`
- Produces: `Unlock: React.FC`, `Editor: React.FC`

**Security flag:** none

- [x] **Step 1: Unlock** — AppWindow (title `LockPad`, statusLeft null) 1500x860 springs up from 60 px below. passwordLength rises 0→14 over frames 30-90; unlockPressed true frames 100-110; message "Unlocking…" from 110; from frame 140 cross-fade to EditorScreen (title `notes.kdbx - LockPad`, statusLeft `Saved`, statusRight `Locks in 5:00`). Caption top-left "Your notes, one master password." at frame 10.
- [x] **Step 2: Editor** — same window position (continuity with the end of Unlock). Notes as Task 3; editor text types `word1 word2 … word12` (fake seed) over frames 20-120, status switches to `Unsaved changes` while typing. Caption 1 "AES-256 + Argon2id" (subtitle "Every note, encrypted at rest.") frames 20-120; Caption 2 "Standard KDBX 4.1" (subtitle "KeePassXC opens it too.") from frame 120. Captions sit left of a window scaled to 0.8 and shifted right, so both are readable.
- [x] **Step 3: Scene compositions and stills** — in Root's `Scenes` folder register `Scene-Unlock` and `Scene-Editor` (durations `SCENE_FRAMES.unlock` / `.editor`, rendering `<AbsoluteFill><Background/><Scene/></AbsoluteFill>`). Stills via `npx remotion still Scene-<Name> out/stills/<name>-<frame>.png --frame=<frame>` at Unlock frames 80 and 160, Editor frames 60 and 180. Read PNGs. Expected: dots visible, caption not overlapping window text.
- [x] **Step 4: tsc + Commit** — `Add unlock and editor scenes`.

---

### Task 6: Scenes 5-6 (Safe save, Auto-lock)

**Files:** Create `src/scenes/SafeSave.tsx`, `src/scenes/AutoLock.tsx`; Modify `src/Root.tsx`

**Interfaces:**
- Consumes: `AppWindow`, `EditorScreen`, `UnlockScreen`, `KeyChip`, `Caption`, `COLORS`, `FONTS`
- Produces: `SafeSave: React.FC`, `AutoLock: React.FC`

**Security flag:** none

- [x] **Step 1: SafeSave** — KeyChip `['Ctrl','S']` pressed at frame 15. Then a horizontal four-step pipeline, cards appearing every ~30 frames from frame 40, connected by a line that fills in accent: `1 Write new file` (`private.kdbx.tmp`), `2 Re-open & verify` (check mark), `3 Swap in` (`private.kdbx`), `4 Keep a backup` (`private.kdbx.bak`). Each card: icon/glyph, title Inter 600 30 px, file name mono 22 px muted. Caption "Saving can't destroy your notebook." (subtitle "Verified before it replaces anything.") from frame 160.
- [x] **Step 2: AutoLock** — editor window with statusRight counting `Locks in 0:05` → `0:00` over frames 0-75 (1 s per 15 frames: time is sped up, no label). At 75 the window content cross-fades to UnlockScreen (passwordLength 0) and title becomes `LockPad`. KeyChips row `Ctrl+L`, `Win+L`, `Sleep` (Sleep as a single chip) springs in from frame 100. Caption "Locks itself." (subtitle "After inactivity, on Win+L, before sleep.") from frame 90.
- [x] **Step 3: Scene compositions and stills** — in Root's `Scenes` folder register `Scene-SafeSave` and `Scene-AutoLock` (durations `SCENE_FRAMES.safeSave` / `.autoLock`, rendering `<AbsoluteFill><Background/><Scene/></AbsoluteFill>`). Stills via `npx remotion still Scene-<Name> out/stills/<name>-<frame>.png --frame=<frame>` at SafeSave frames 150 and 230, AutoLock frames 40 and 170. Read PNGs. Expected: all four cards fit horizontally at 1920 px with ≥ 80 px margins.
- [x] **Step 4: tsc + Commit** — `Add safe-save and auto-lock scenes`.

---

### Task 7: Scenes 7-8 (Clipboard, Screen capture)

**Files:** Create `src/scenes/Clipboard.tsx`, `src/scenes/ScreenCapture.tsx`; Modify `src/Root.tsx`

**Interfaces:**
- Consumes: `AppWindow`, `EditorScreen`, `KeyChip`, `Caption`, `COLORS`, `FONTS`
- Produces: `Clipboard: React.FC`, `ScreenCapture: React.FC`

**Security flag:** none

- [x] **Step 1: Clipboard** — window scaled 0.7 on the left with `selection` over the first line, KeyChip `['Ctrl','C']` pressed at frame 10. A clipboard card on the right springs in at 20: "Clipboard" with a circular ring that empties over frames 30-150 labelled `20 s` → `0 s` (time sped up), then the card text becomes "Cleared". Below it a Win+V history panel mock ("Clipboard history") that stays empty with the line "Nothing here" muted. Caption "Kept out of clipboard history." (subtitle "Cleared automatically after 20 seconds.") from frame 40.
- [x] **Step 2: ScreenCapture** — two panels side by side, each a 16:9 mini desktop (Windows-blue gradient wallpaper, taskbar strip). Left label "Your screen": LockPad window visible. Right label "What others see" with a red "You're sharing your screen" pill at top: same desktop, no LockPad window. A thin accent scan line sweeps the right panel at frame 30. Caption "Invisible to screenshots and screen sharing." from frame 50.
- [x] **Step 3: Scene compositions and stills** — in Root's `Scenes` folder register `Scene-Clipboard` and `Scene-ScreenCapture` (durations `SCENE_FRAMES.clipboard` / `.screenCapture`, rendering `<AbsoluteFill><Background/><Scene/></AbsoluteFill>`). Stills via `npx remotion still Scene-<Name> out/stills/<name>-<frame>.png --frame=<frame>` at Clipboard frames 80 and 170, ScreenCapture frame 120. Read PNGs.
- [x] **Step 4: tsc + Commit** — `Add clipboard and screen-capture scenes`.

---

### Task 8: Scenes 9-10 (Features, Call to action)

**Files:** Create `src/scenes/Features.tsx`, `src/scenes/CallToAction.tsx`; Modify `src/Root.tsx`

**Interfaces:**
- Consumes: `LockPadIcon`, `COLORS`, `FONTS`, `GITHUB_URL`
- Produces: `Features: React.FC`, `CallToAction: React.FC`

**Security flag:** none

- [x] **Step 1: Features** — heading "And more" (Inter 700 56 px). 3x2 grid of tiles (glass panels: `rgba(255,255,255,0.04)`, 1 px `rgba(255,255,255,0.08)` border, 16 px radius), staggered spring every 8 frames from frame 10. Tiles (title / line):
  - `No network code` / `No cloud sync, no telemetry, no updater.`
  - `Portable` / `Four files. Runs from a USB stick.`
  - `Read-only when it should be` / `Never writes to files it didn't create.`
  - `Opens in KeePassXC` / `Your way out, if you ever need one.`
  - `Light & dark` / `Follows Windows, or set it yourself.`
  - `Open source` / `GPL-2.0-or-later.`
  Each tile has a simple inline-SVG line glyph in accent.
- [x] **Step 2: CallToAction** — LockPadIcon size 180 (both progress 1) springs in, "LockPad" 110 px, line "Free & open source · Windows 10/11" muted 34 px, then a pill with `GITHUB_URL` in mono 34 px, accent border, from frame 40. Holds still from frame 90 to the end.
- [x] **Step 3: Scene compositions and stills** — in Root's `Scenes` folder register `Scene-Features` and `Scene-CallToAction` (durations `SCENE_FRAMES.features` / `.cta`, rendering `<AbsoluteFill><Background/><Scene/></AbsoluteFill>`). Stills via `npx remotion still Scene-<Name> out/stills/<name>-<frame>.png --frame=<frame>` at Features frame 120 and CallToAction frame 150. Read PNGs.
- [x] **Step 4: tsc + Commit** — `Add features and call-to-action scenes`.

---

### Task 9: Assemble the timeline

**Files:** Modify `src/LockPadPromo.tsx`

**Interfaces:**
- Consumes: all ten scenes, `Background`, `SCENE_FRAMES`, `TRANSITION_FRAMES`
- Produces: final `LockPadPromo`

**Security flag:** none

- [x] **Step 1: Implement** — `<AbsoluteFill><Background/><TransitionSeries>` with ten `TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.x}` in storyboard order and nine `TransitionSeries.Transition timing={linearTiming({durationInFrames: TRANSITION_FRAMES})}`. Presentation: `fade()` everywhere except `slide({direction: 'from-right'})` into SafeSave, Clipboard and ScreenCapture. Unlock→Editor uses `fade()` (window position identical, so it reads as continuous). Scenes render on a transparent background so `Background` shows through.
- [x] **Step 2: Verify duration** — `npx remotion compositions` reports 1800 frames.
- [x] **Step 3: Transition stills** — render a still at each transition midpoint. Midpoint of transition k (k = 1..9) = sum of the first k scene durations − (k − 1) × 15 − 8: frames 157, 277, 457, 667, 907, 1087, 1267, 1447, 1627. Read each PNG. Expected: no empty/black frame; both scenes visible blending.
- [x] **Step 4: tsc + Commit** — `Assemble LockPad promo timeline`.

---

### Task 10: Render, README, final checks

**Files:** Create `marketing/video/README.md`

**Interfaces:**
- Consumes: complete project
- Produces: `out/lockpad-promo.mp4` (gitignored)

**Security flag:** none

- [x] **Step 1: README.md** content:

```markdown
# LockPad marketing video

A 60-second product video made with [Remotion](https://www.remotion.dev).
Everything on screen is drawn in React; LockPad's UI is recreated from its
Dark theme (`src/LockPad.App/Themes/Dark.xaml`).

## Commands

Run these in this folder (`marketing/video`):

- `npm install`: install dependencies (once).
- `npm run dev`: open Remotion Studio to preview and scrub the video.
- `npm run render`: render `out/lockpad-promo.mp4` (1920x1080, 30 fps, 60 s).
- `npm run typecheck`: check the TypeScript.

The first render downloads a headless Chrome and the fonts, so it needs internet.

## Editing

- Text, colors, timings and the GitHub URL: `src/theme.ts` and the files in
  `src/scenes/`, one per scene.
- Scene order and transitions: `src/LockPadPromo.tsx`.

Every claim in the video must match LockPad's README.

## License

Remotion is free for individuals and companies with up to 3 employees; larger
companies need a company license (see remotion.dev/license).
```

- [x] **Step 2: Full render** — `npm run render`. Expected: exits 0, `out/lockpad-promo.mp4` exists.
- [x] **Step 3: Probe** — `npx remotion ffprobe out/lockpad-promo.mp4`. Expected: 1920x1080, 30 fps, duration 60.0 s.
- [x] **Step 4: Name check** — Grep `marketing/` (excluding node_modules/out) for the earlier development name, case-insensitive. Expected: no matches.
- [x] **Step 5: C# side untouched** — `git status --short` shows only `.gitignore`, `marketing/video/`, `docs/specs/...`, `docs/plans/...` (plus the user's existing `todo`).
- [x] **Step 6: Commit** — `Add marketing video README`.
