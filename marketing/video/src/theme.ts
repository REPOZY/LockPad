import { loadFont as loadInter } from "@remotion/google-fonts/Inter";
import { loadFont as loadMono } from "@remotion/google-fonts/JetBrainsMono";

// Loaded at module level, so every render waits for the fonts.
const inter = loadInter("normal", {
  weights: ["400", "600", "700"],
  subsets: ["latin"],
});
const mono = loadMono("normal", { weights: ["400"], subsets: ["latin"] });

export const FONTS = {
  sans: inter.fontFamily,
  mono: mono.fontFamily,
};

// App colors are LockPad's Dark theme (src/LockPad.App/Themes/Dark.xaml); icon colors are docs/icon/lockpad.svg.
export const COLORS = {
  stage: "#0B1120",
  glowNavy: "#1E40AF",
  glowBlue: "#4DA3FF",
  window: "#1E1E1E",
  panel: "#2B2B2B",
  editor: "#171717",
  text: "#E6E6E6",
  accent: "#4DA3FF",
  border: "#4A4A4A",
  muted: "#9A9A9A",
  error: "#FF6B6B",
  primary: "#0E639C",
  strong: "#6CCB5F",
  fair: "#FFB900",
  control: "#333333",
  controlBorder: "#858585",
  dialogBorder: "#787878",
  highlight: "#094771",
  iconNavy: "#1E40AF",
  iconShackle: "#172554",
  iconOutline: "#475569",
  iconLines: "#94A3B8",
  iconFold: "#E2E8F0",
};

// Planned repository; it does not exist yet.
export const GITHUB_URL = "github.com/REPOZY/LockPad";

export const TRANSITION_FRAMES = 15;

export const SCENE_FRAMES = {
  // 30 frames on screen alone, plus the 15-frame dip into the hook.
  title: 45,
  hook: 165,
  logo: 135,
  unlock: 195,
  editor: 225,
  safeSave: 255,
  autoLock: 195,
  clipboard: 195,
  screenCapture: 195,
  features: 195,
  cta: 180,
} as const;

const sceneCount = Object.keys(SCENE_FRAMES).length;

export const TOTAL_FRAMES =
  Object.values(SCENE_FRAMES).reduce((sum, frames) => sum + frames, 0) -
  (sceneCount - 1) * TRANSITION_FRAMES;

// LockPad's UI is drawn at its native WPF sizes times this factor, so it stays legible at 1080p.
export const UI = 1.5;

export const FPS = 30;
export const WIDTH = 1920;
export const HEIGHT = 1080;
