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
import { COLORS, FONTS } from "../theme";
import { NOTES, SEED_TEXT, WINDOW_H, WINDOW_W } from "./shared";

const PANEL_W = 780;
const PANEL_H = 439;
const PANEL_GAP = 80;
const MINI_SCALE = 0.36;

// A small Windows desktop: wallpaper, taskbar and (optionally) the LockPad window.
const Desktop: React.FC<{ showWindow: boolean }> = ({ showWindow }) => (
  <div
    style={{
      position: "relative",
      width: PANEL_W,
      height: PANEL_H,
      borderRadius: 16,
      overflow: "hidden",
      border: "1.5px solid rgba(255,255,255,0.14)",
      background:
        "radial-gradient(ellipse at 50% 120%, #3B82F6 0%, transparent 55%), radial-gradient(ellipse at 20% 0%, #1E3A8A 0%, transparent 60%), #0B1B3F",
    }}
  >
    {/* Another, unbranded app: visible in both views, so it is clear only LockPad is left out. */}
    <div
      style={{
        position: "absolute",
        right: 36,
        top: 150,
        width: 300,
        height: 200,
        borderRadius: 8,
        overflow: "hidden",
        background: "#F3F4F6",
        boxShadow: "0 12px 30px rgba(0,0,0,0.4)",
      }}
    >
      <div style={{ height: 22, background: "#E5E7EB" }} />
      {[0.8, 0.65, 0.9, 0.5].map((w, i) => (
        <div
          key={i}
          style={{
            margin: "14px 16px 0",
            width: `${w * 100}%`,
            maxWidth: 268,
            height: 10,
            borderRadius: 5,
            background: "#CBD5E1",
          }}
        />
      ))}
    </div>
    {showWindow ? (
      <div
        style={{
          position: "absolute",
          left: (PANEL_W - WINDOW_W * MINI_SCALE) / 2,
          top: 40,
          transform: `scale(${MINI_SCALE})`,
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
            selection={null}
          />
        </AppWindow>
      </div>
    ) : null}
    <div
      style={{
        position: "absolute",
        left: 0,
        right: 0,
        bottom: 0,
        height: 34,
        background: "rgba(20,24,36,0.85)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        gap: 10,
      }}
    >
      {[0, 1, 2, 3, 4].map((i) => (
        <div
          key={i}
          style={{
            width: 18,
            height: 18,
            borderRadius: 4,
            background: i === 0 ? "#60A5FA" : "rgba(255,255,255,0.28)",
          }}
        />
      ))}
    </div>
  </div>
);

export const ScreenCapture: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const left = spring({ frame, fps, config: { damping: 200 } });
  const right = spring({ frame: frame - 8, fps, config: { damping: 200 } });
  const scan = interpolate(frame, [30, 60], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const scanOpacity = interpolate(frame, [28, 32, 58, 64], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const rowLeft = (1920 - (2 * PANEL_W + PANEL_GAP)) / 2;

  const label = (text: string, extra: React.ReactNode) => (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 16,
        marginBottom: 18,
        fontSize: 30,
        fontWeight: 600,
      }}
    >
      {text}
      {extra}
    </div>
  );

  return (
    <AbsoluteFill style={{ fontFamily: FONTS.sans, color: COLORS.text }}>
      <div
        style={{
          position: "absolute",
          top: 150,
          left: rowLeft,
          opacity: left,
          transform: `translateY(${(1 - left) * 30}px)`,
        }}
      >
        {label("Your screen", null)}
        <Desktop showWindow />
      </div>
      <div
        style={{
          position: "absolute",
          top: 150,
          left: rowLeft + PANEL_W + PANEL_GAP,
          opacity: right,
          transform: `translateY(${(1 - right) * 30}px)`,
        }}
      >
        {label(
          "What others see",
          <span
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              padding: "4px 14px",
              borderRadius: 999,
              background: "rgba(255,107,107,0.16)",
              color: COLORS.error,
              fontSize: 20,
              fontWeight: 600,
            }}
          >
            <span
              style={{
                width: 10,
                height: 10,
                borderRadius: 5,
                background: COLORS.error,
              }}
            />
            Sharing screen
          </span>,
        )}
        <div style={{ position: "relative" }}>
          <Desktop showWindow={false} />
          <div
            style={{
              position: "absolute",
              top: 0,
              bottom: 0,
              left: scan * PANEL_W,
              width: 3,
              background: COLORS.accent,
              boxShadow: `0 0 24px 6px ${COLORS.accent}88`,
              opacity: scanOpacity,
            }}
          />
        </div>
      </div>
      <div
        style={{
          position: "absolute",
          top: 790,
          left: 0,
          right: 0,
          display: "flex",
          justifyContent: "center",
        }}
      >
        <Caption
          title="Hidden from screenshots and screen sharing."
          subtitle={null}
          startFrame={50}
          endFrame={null}
          align="center"
        />
      </div>
    </AbsoluteFill>
  );
};
