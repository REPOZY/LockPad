import React from "react";
import {
  AbsoluteFill,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { COLORS, FONTS } from "../theme";

const stroke = {
  fill: "none",
  stroke: COLORS.accent,
  strokeWidth: 2.2,
  strokeLinecap: "round",
  strokeLinejoin: "round",
} as const;

// Every line is stated in LockPad's README.
const TILES: { title: string; line: string; glyph: React.ReactNode }[] = [
  {
    title: "No network code",
    line: "No cloud, no telemetry, no updater.",
    glyph: (
      <>
        <circle cx={15} cy={15} r={11} {...stroke} />
        <path d="M4 15h22M15 4c4 4 4 18 0 22M15 4c-4 4-4 18 0 22M6 25L24 5" {...stroke} />
      </>
    ),
  },
  {
    title: "Portable",
    line: "Four files. Runs from a USB stick.",
    glyph: (
      <>
        <path d="M9 11h12v15H9zM11 4h8v7h-8z" {...stroke} />
        <path d="M13.5 7h0.5M16 7h0.5" {...stroke} />
      </>
    ),
  },
  {
    title: "Safe read-only mode",
    line: "Never writes to files it didn't create.",
    glyph: (
      <>
        <path d="M6 24l2-6L20 6l4 4-12 12zM17 9l4 4" {...stroke} />
        <path d="M4 4l22 22" {...stroke} />
      </>
    ),
  },
  {
    title: "Opens in KeePassXC",
    line: "Your way out, if you ever need one.",
    glyph: (
      <>
        <circle cx={10} cy={15} r={6} {...stroke} />
        <path d="M16 15h11M23 15v4M27 15v3" {...stroke} />
      </>
    ),
  },
  {
    title: "Light & dark",
    line: "Follows Windows, or set it yourself.",
    glyph: (
      <>
        <circle cx={15} cy={15} r={11} {...stroke} />
        <path d="M15 4a11 11 0 0 1 0 22z" fill={COLORS.accent} />
      </>
    ),
  },
  {
    title: "Open source",
    line: "GPL-2.0-or-later.",
    glyph: <path d="M10 8l-7 7 7 7M20 8l7 7-7 7M17 5l-4 20" {...stroke} />,
  },
];

const TILE_W = 500;
const TILE_H = 210;
const GAP = 40;

export const Features: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const heading = spring({ frame, fps, config: { damping: 200 } });
  const gridW = 3 * TILE_W + 2 * GAP;

  return (
    <AbsoluteFill style={{ fontFamily: FONTS.sans, color: COLORS.text }}>
      <div
        style={{
          position: "absolute",
          top: 180,
          left: 0,
          right: 0,
          textAlign: "center",
          fontSize: 64,
          fontWeight: 700,
          letterSpacing: -1.5,
          opacity: heading,
          transform: `translateY(${(1 - heading) * 24}px)`,
        }}
      >
        And more
      </div>
      <div
        style={{
          position: "absolute",
          top: 340,
          left: (1920 - gridW) / 2,
          width: gridW,
          display: "grid",
          gridTemplateColumns: `repeat(3, ${TILE_W}px)`,
          gap: GAP,
        }}
      >
        {TILES.map((tile, i) => {
          const enter = spring({
            frame: frame - 10 - i * 8,
            fps,
            config: { damping: 200 },
          });
          return (
            <div
              key={tile.title}
              style={{
                height: TILE_H,
                boxSizing: "border-box",
                padding: "30px 32px",
                borderRadius: 16,
                background: "rgba(255,255,255,0.04)",
                border: "1px solid rgba(255,255,255,0.08)",
                opacity: enter,
                transform: `translateY(${(1 - enter) * 30}px) scale(${0.96 + 0.04 * enter})`,
              }}
            >
              <svg width={46} height={46} viewBox="0 0 30 30">
                {tile.glyph}
              </svg>
              <div style={{ marginTop: 20, fontSize: 32, fontWeight: 600 }}>
                {tile.title}
              </div>
              <div style={{ marginTop: 8, fontSize: 24, color: COLORS.muted }}>
                {tile.line}
              </div>
            </div>
          );
        })}
      </div>
    </AbsoluteFill>
  );
};
