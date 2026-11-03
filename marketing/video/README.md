# LockPad marketing video

A 61-second product video made with [Remotion](https://www.remotion.dev).
Everything on screen is drawn in React; LockPad's UI is recreated from its
Dark theme (`src/LockPad.App/Themes/Dark.xaml`).

## Commands

Run these in this folder (`marketing/video`):

- `npm install`: install dependencies (once).
- `npm run dev`: open Remotion Studio to preview and scrub the video.
- `npm run render`: render `out/lockpad-promo.mp4` (1920x1080, 30 fps, 61 s).
- `npm run typecheck`: check the TypeScript.

The first render downloads a headless Chrome and the fonts, so it needs internet.

## Editing

- Text, colors, timings and the GitHub URL: `src/theme.ts` and the files in
  `src/scenes/`, one per scene.
- Scene order and transitions: `src/LockPadPromo.tsx`.
- Each scene is also its own composition (`Scene-Hook`, `Scene-Logo`, ...) in
  the Studio's "Scenes" folder, so you can preview one scene at a time.

Every claim in the video must match LockPad's README.

## Showing it in LockPad's README

GitHub only plays a video in a README when it was uploaded through github.com; a
`<video>` tag or a link to an `.mp4` in the repository does not play. Once the
repository exists:

1. Render the video (`npm run render`).
2. On github.com, open `README.md` and click the pencil (Edit).
3. Drag `marketing/video/out/lockpad-promo.mp4` onto the `PROMO VIDEO` comment
   below the introduction. GitHub uploads it (limit 10 MB on a free plan; the
   video is about 6 MB) and inserts a `https://github.com/user-attachments/assets/...`
   link.
4. Keep that link on its own line, with a blank line above and below, delete
   the comment, and commit.

The player shows the video's first frame before it is played; that is why the
video opens on a one-second title card. After re-rendering, upload the new file
the same way and replace the link.

## License

Remotion is free for individuals and companies with up to 3 employees; larger
companies need a company license (see remotion.dev/license).
