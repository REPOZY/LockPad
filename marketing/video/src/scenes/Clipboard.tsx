import React from "react";
import {
  AbsoluteFill,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { AppWindow } from "../components/AppWindow";
import { Caption } from "../components/Caption";
import { EditorScreen } from "../components/EditorScreen";
import { KeyChip } from "../components/KeyChip";
import { COLORS, FONTS } from "../theme";
import { NOTES, SEED_TEXT, WINDOW_H, WINDOW_LEFT, WINDOW_W } from "./shared";

const COPY = 10;
const CARD = 20;
// The 20 second clear delay, sped up to 120 frames.
const CLEAR_START = 30;
const CLEAR_END = 150;
const CLEAR_SECONDS = 20;
const RING_R = 34;
const RING_C = 2 * Math.PI * RING_R;

const panel: React.CSSProperties = {
  borderRadius: 20,
  background: "rgba(255,255,255,0.045)",
  border: "1.5px solid rgba(255,255,255,0.10)",
  padding: "24px 28px",
  boxSizing: "border-box",
};

export const Clipboard: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
  const remaining = interpolate(frame, [CLEAR_START, CLEAR_END], [1, 0], clamp);
  const seconds = Math.ceil(remaining * CLEAR_SECONDS);
  const cleared = frame >= CLEAR_END;
  const clearedIn = spring({ frame: frame - CLEAR_END, fps, config: { damping: 200 } });
  const card = spring({ frame: frame - CARD, fps, config: { damping: 200 } });
  const history = spring({ frame: frame - CARD - 10, fps, config: { damping: 200 } });
  const chip = spring({ frame, fps, config: { damping: 200 } });

  return (
    <AbsoluteFill style={{ fontFamily: FONTS.sans, color: COLORS.text }}>
      <div
        style={{
          position: "absolute",
          left: WINDOW_LEFT.x,
          top: WINDOW_LEFT.y,
          transform: `scale(${WINDOW_LEFT.scale})`,
          transformOrigin: "top left",
        }}
      >
        <AppWindow
          title="notes.kdbx - LockPad"
          statusLeft="Saved"
          statusRight="Locks in 5:00"
          width={WINDOW_W}
          height={WINDOW_H}
        >
          <EditorScreen
            notes={NOTES}
            selectedIndex={0}
            text={SEED_TEXT}
            caretVisible={false}
            selection={frame >= 4 ? [0, SEED_TEXT.length] : null}
          />
        </AppWindow>
      </div>
      <div
        style={{
          position: "absolute",
          left: WINDOW_LEFT.x,
          top: 890,
          opacity: chip,
        }}
      >
        <KeyChip keys={["Ctrl", "C"]} pressed={frame >= COPY && frame < COPY + 8} />
      </div>

      <div style={{ position: "absolute", left: 1230, width: 640, top: 150 }}>
        <Caption
          title="Kept out of clipboard history."
          subtitle="Cleared automatically after 20 seconds."
          startFrame={40}
          endFrame={null}
          align="left"
        />
      </div>

      <div
        style={{
          ...panel,
          position: "absolute",
          left: 1230,
          width: 640,
          top: 440,
          height: 170,
          display: "flex",
          alignItems: "center",
          gap: 28,
          opacity: card,
          transform: `translateY(${(1 - card) * 30}px)`,
        }}
      >
        <svg width={92} height={92} viewBox="0 0 92 92" style={{ flexShrink: 0 }}>
          <circle cx={46} cy={46} r={RING_R} fill="none" stroke="rgba(255,255,255,0.12)" strokeWidth={7} />
          <circle
            cx={46}
            cy={46}
            r={RING_R}
            fill="none"
            stroke={cleared ? COLORS.strong : COLORS.accent}
            strokeWidth={7}
            strokeLinecap="round"
            strokeDasharray={RING_C}
            strokeDashoffset={RING_C * (1 - remaining)}
            transform="rotate(-90 46 46)"
          />
          {cleared ? (
            <path
              d="M33 47l9 9 17-19"
              fill="none"
              stroke={COLORS.strong}
              strokeWidth={6}
              strokeLinecap="round"
              strokeLinejoin="round"
              opacity={clearedIn}
            />
          ) : (
            <text
              x={46}
              y={47}
              textAnchor="middle"
              dominantBaseline="middle"
              fill={COLORS.text}
              fontSize={26}
              fontWeight={700}
              fontFamily={FONTS.sans}
            >
              {seconds}s
            </text>
          )}
        </svg>
        <div style={{ minWidth: 0 }}>
          <div style={{ fontSize: 24, color: COLORS.muted }}>Clipboard</div>
          <div
            style={{
              marginTop: 8,
              fontSize: cleared ? 34 : 24,
              fontWeight: cleared ? 700 : 400,
              fontFamily: cleared ? FONTS.sans : FONTS.mono,
              color: cleared ? COLORS.strong : COLORS.text,
              whiteSpace: "nowrap",
              overflow: "hidden",
              textOverflow: "ellipsis",
            }}
          >
            {cleared ? "Cleared" : SEED_TEXT}
          </div>
        </div>
      </div>

      <div
        style={{
          ...panel,
          position: "absolute",
          left: 1230,
          width: 640,
          top: 640,
          opacity: history,
          transform: `translateY(${(1 - history) * 30}px)`,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <KeyChipSmall label="Win" />
          <span style={{ color: COLORS.muted, fontSize: 22 }}>+</span>
          <KeyChipSmall label="V" />
          <span style={{ fontSize: 24, color: COLORS.muted, marginLeft: 8 }}>
            Clipboard history
          </span>
        </div>
        <div
          style={{
            marginTop: 18,
            padding: "18px 20px",
            borderRadius: 12,
            border: "1.5px dashed rgba(255,255,255,0.16)",
            fontSize: 26,
            color: COLORS.muted,
          }}
        >
          Nothing from LockPad
        </div>
      </div>
    </AbsoluteFill>
  );
};

const KeyChipSmall: React.FC<{ label: string }> = ({ label }) => (
  <div
    style={{
      padding: "4px 12px",
      borderRadius: 8,
      border: "1.5px solid rgba(255,255,255,0.16)",
      background: "#1E2638",
      fontSize: 20,
      fontWeight: 600,
    }}
  >
    {label}
  </div>
);
