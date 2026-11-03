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
import { LockPadIcon } from "../components/LockPadIcon";
import { UnlockScreen } from "../components/UnlockScreen";
import { COLORS, FONTS } from "../theme";
import { NOTES, SEED_TEXT, WINDOW_H, WINDOW_LEFT, WINDOW_W } from "./shared";

// Idle time is sped up: one second of countdown per 15 frames.
const COUNTDOWN_FROM = 5;
const FRAMES_PER_SECOND = 15;
const LOCK = COUNTDOWN_FROM * FRAMES_PER_SECOND;
const CAPTION = 90;

export const AutoLock: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
  const secondsLeft = Math.max(
    0,
    COUNTDOWN_FROM - Math.floor(frame / FRAMES_PER_SECOND),
  );
  const locked = interpolate(frame, [LOCK, LOCK + 15], [0, 1], clamp);
  const isLocked = frame >= LOCK + 7;
  const timer = interpolate(frame, [0, 10, LOCK, CAPTION - 5], [0, 1, 1, 0], clamp);
  const padlock = spring({
    frame: frame - LOCK - 4,
    fps,
    config: { damping: 9, stiffness: 220 },
  });
  const padlockFade = interpolate(frame, [LOCK, LOCK + 6, LOCK + 34, LOCK + 46], [0, 1, 1, 0], clamp);
  const windowScale = WINDOW_LEFT.scale;

  return (
    <AbsoluteFill style={{ fontFamily: FONTS.sans }}>
      <div
        style={{
          position: "absolute",
          left: WINDOW_LEFT.x,
          top: WINDOW_LEFT.y,
          transform: `scale(${windowScale})`,
          transformOrigin: "top left",
        }}
      >
        <AppWindow
          title={isLocked ? "LockPad" : "notes.kdbx - LockPad"}
          statusLeft={isLocked ? null : "Saved"}
          statusRight={isLocked ? null : `Locks in 0:0${secondsLeft}`}
          width={WINDOW_W}
          height={WINDOW_H}
        >
          <AbsoluteFill style={{ opacity: 1 - locked }}>
            <EditorScreen
              notes={NOTES}
              selectedIndex={0}
              text={SEED_TEXT}
              caretVisible={false}
              selection={null}
            />
          </AbsoluteFill>
          <AbsoluteFill style={{ opacity: locked }}>
            <UnlockScreen passwordLength={0} unlockPressed={false} message={null} />
          </AbsoluteFill>
        </AppWindow>
        <div
          style={{
            position: "absolute",
            inset: 0,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            opacity: padlockFade,
          }}
        >
          <div
            style={{
              padding: 50,
              borderRadius: 48,
              background: "rgba(11,17,32,0.75)",
              boxShadow: `0 0 80px ${COLORS.accent}55`,
            }}
          >
            <LockPadIcon size={220} pageProgress={1} shackleProgress={padlock} />
          </div>
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          left: 1230,
          width: 640,
          top: 0,
          bottom: 0,
          display: "flex",
          flexDirection: "column",
          justifyContent: "center",
        }}
      >
        <div style={{ position: "relative", height: 380 }}>
          <div style={{ position: "absolute", top: 40, opacity: timer }}>
            <div style={{ fontSize: 30, color: COLORS.muted }}>Locks in</div>
            <div
              style={{
                fontFamily: FONTS.mono,
                fontSize: 150,
                color: COLORS.accent,
                lineHeight: 1.1,
              }}
            >
              0:0{secondsLeft}
            </div>
          </div>
          <div style={{ position: "absolute", top: 0 }}>
            <Caption
              title="Locks itself."
              subtitle="After inactivity, on Win+L, before sleep."
              startFrame={CAPTION}
              endFrame={null}
              align="left"
            />
          </div>
          <div style={{ position: "absolute", top: 230, display: "flex", gap: 28 }}>
            {[["Ctrl", "L"], ["Win", "L"], ["Sleep"]].map((keys, i) => {
              const enter = spring({
                frame: frame - 100 - i * 8,
                fps,
                config: { damping: 200 },
              });
              return (
                <div
                  key={keys.join("+")}
                  style={{ opacity: enter, transform: `translateY(${(1 - enter) * 20}px)` }}
                >
                  <KeyChip keys={keys} pressed={false} />
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </AbsoluteFill>
  );
};
