// Values several scenes share, so the LockPad window stays in place across scene cuts.

export const NOTES = ["Seed phrase", "Recovery codes", "Bank PIN", "SSH key"];

// Obviously fake demo content: never a realistic secret.
export const SEED_TEXT =
  "word1 word2 word3 word4 word5 word6 word7 word8 word9 word10 word11 word12";

export const WINDOW_W = 1500;
export const WINDOW_H = 860;

// Top-left position and scale of the window (transform-origin top left).
export const WINDOW_CENTER = { x: (1920 - WINDOW_W * 0.85) / 2, y: 200, scale: 0.85 };
export const WINDOW_RIGHT = { x: 750, y: (1080 - WINDOW_H * 0.72) / 2, scale: 0.72 };
export const WINDOW_LEFT = { x: 90, y: (1080 - WINDOW_H * 0.72) / 2, scale: 0.72 };
