import React from "react";
import {
  AbsoluteFill,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { Caption } from "../components/Caption";
import { KeyChip } from "../components/KeyChip";
import { COLORS, FONTS } from "../theme";

type Step = {
  title: string;
  detail: string;
  mono: boolean;
  glyph: React.ReactNode;
};

const stroke = {
  fill: "none",
  stroke: COLORS.accent,
  strokeWidth: 2.4,
  strokeLinecap: "round",
  strokeLinejoin: "round",
} as const;

// The save pipeline from LockPad's README ("Saving"), in four steps.
const STEPS: Step[] = [
  {
    title: "Write a new file",
    detail: "private.kdbx.x7q2.tmp",
    mono: true,
    glyph: (
      <>
        <path d="M8 3h9l5 5v17H8z" {...stroke} />
        <path d="M15 12v8M11 16h8" {...stroke} />
      </>
    ),
  },
  {
    title: "Re-open & verify",
    detail: "Every note compared",
    mono: false,
    glyph: (
      <>
        <circle cx={15} cy={14} r={10} {...stroke} />
        <path d="M10 14.5l3.5 3.5 6.5-7" {...stroke} />
      </>
    ),
  },
  {
    title: "Swap in, one step",
    detail: "private.kdbx",
    mono: true,
    glyph: (
      <>
        <path d="M5 10h18l-4-4M25 18H7l4 4" {...stroke} />
      </>
    ),
  },
  {
    title: "Keep a backup",
    detail: "private.kdbx.bak",
    mono: true,
    glyph: (
      <>
        <path d="M4 7h22v5H4zM6 12v12h18V12M12 17h6" {...stroke} />
      </>
    ),
  },
];

const FIRST_STEP = 40;
const STEP_GAP = 30;
const CARD_W = 340;
const CARD_GAP = 60;

export const SafeSave: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const chip = spring({ frame, fps, config: { damping: 200 } });
  const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
  const active = Math.min(
    STEPS.length - 1,
    Math.floor((frame - FIRST_STEP) / STEP_GAP),
  );
  const rowWidth = STEPS.length * CARD_W + (STEPS.length - 1) * CARD_GAP;

  return (
    <AbsoluteFill style={{ fontFamily: FONTS.sans }}>
      <div
        style={{
          position: "absolute",
          top: 130,
          left: 0,
          right: 0,
          display: "flex",
          justifyContent: "center",
          opacity: chip,
          transform: `translateY(${(1 - chip) * 20}px)`,
        }}
      >
        <KeyChip keys={["Ctrl", "S"]} pressed={frame >= 15 && frame < 24} />
      </div>

      <div
        style={{
          position: "absolute",
          top: 330,
          left: (1920 - rowWidth) / 2,
          width: rowWidth,
          height: 250,
        }}
      >
        {/* Connector behind the cards, filling as each step completes. */}
        {STEPS.slice(1).map((_, i) => {
          const fill = interpolate(
            frame,
            [FIRST_STEP + i * STEP_GAP + 10, FIRST_STEP + (i + 1) * STEP_GAP],
            [0, 1],
            clamp,
          );
          return (
            <div
              key={i}
              style={{
                position: "absolute",
                top: 124,
                left: (i + 1) * CARD_W + i * CARD_GAP,
                width: CARD_GAP * fill,
                height: 3,
                backgroundColor: COLORS.accent,
                boxShadow: `0 0 12px ${COLORS.accent}`,
              }}
            />
          );
        })}
        {STEPS.map((step, i) => {
          const start = FIRST_STEP + i * STEP_GAP;
          const enter = spring({
            frame: frame - start,
            fps,
            config: { damping: 200 },
          });
          const isActive = i === active;
          return (
            <div
              key={step.title}
              style={{
                position: "absolute",
                left: i * (CARD_W + CARD_GAP),
                width: CARD_W,
                height: 250,
                boxSizing: "border-box",
                padding: 30,
                borderRadius: 20,
                background: "rgba(255,255,255,0.045)",
                border: `1.5px solid ${isActive ? COLORS.accent : "rgba(255,255,255,0.10)"}`,
                boxShadow: isActive ? `0 0 40px ${COLORS.accent}33` : "none",
                opacity: enter,
                transform: `translateY(${(1 - enter) * 30}px)`,
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
                <div
                  style={{
                    width: 44,
                    height: 44,
                    borderRadius: 22,
                    backgroundColor: `${COLORS.accent}22`,
                    color: COLORS.accent,
                    fontWeight: 700,
                    fontSize: 24,
                    display: "flex",
                    alignItems: "center",
                    justifyContent: "center",
                  }}
                >
                  {i + 1}
                </div>
                <svg width={44} height={44} viewBox="0 0 30 30">
                  {step.glyph}
                </svg>
              </div>
              <div
                style={{
                  marginTop: 34,
                  fontSize: 31,
                  fontWeight: 600,
                  color: COLORS.text,
                }}
              >
                {step.title}
              </div>
              <div
                style={{
                  marginTop: 12,
                  fontSize: step.mono ? 22 : 24,
                  fontFamily: step.mono ? FONTS.mono : FONTS.sans,
                  color: COLORS.muted,
                }}
              >
                {step.detail}
              </div>
            </div>
          );
        })}
      </div>

      <div
        style={{
          position: "absolute",
          top: 700,
          left: 0,
          right: 0,
          display: "flex",
          justifyContent: "center",
        }}
      >
        <Caption
          title="Saving can't destroy your notebook."
          subtitle="Verified before it replaces anything."
          startFrame={160}
          endFrame={null}
          align="center"
        />
      </div>
    </AbsoluteFill>
  );
};
