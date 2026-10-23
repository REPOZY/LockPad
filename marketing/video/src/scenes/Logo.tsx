import React from "react";
import {
  AbsoluteFill,
  Easing,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { LockPadIcon } from "../components/LockPadIcon";
import { COLORS, FONTS } from "../theme";

const SNAP = 35;
const TEXT_START = 48;
// A frame after every animation has settled.
const SETTLED = 120;

const LogoCard: React.FC<{ still: boolean }> = ({ still }) => {
  const current = useCurrentFrame();
  const frame = still ? SETTLED : current;
  const { fps } = useVideoConfig();
  const page = interpolate(frame, [0, SNAP], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: Easing.out(Easing.cubic),
  });
  const shackle = spring({
    frame: frame - SNAP,
    fps,
    config: { damping: 9, stiffness: 220 },
  });
  // A short flash of light when the padlock snaps shut.
  const flash = interpolate(frame, [SNAP + 2, SNAP + 8, SNAP + 30], [0, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const word = spring({ frame: frame - TEXT_START, fps, config: { damping: 200 } });
  const tagline = spring({
    frame: frame - TEXT_START - 8,
    fps,
    config: { damping: 200 },
  });

  return (
    <AbsoluteFill
      style={{
        alignItems: "center",
        justifyContent: "center",
        fontFamily: FONTS.sans,
      }}
    >
      <div style={{ position: "relative", display: "flex", justifyContent: "center" }}>
        <div
          style={{
            position: "absolute",
            width: 560,
            height: 560,
            top: -150,
            borderRadius: "50%",
            background: `radial-gradient(circle, ${COLORS.glowBlue}55 0%, transparent 60%)`,
            opacity: 0.35 + flash * 0.65,
          }}
        />
        <LockPadIcon size={260} pageProgress={page} shackleProgress={shackle} />
      </div>
      <div
        style={{
          marginTop: 28,
          fontSize: 128,
          fontWeight: 700,
          letterSpacing: -4,
          color: COLORS.text,
          opacity: word,
          transform: `translateY(${(1 - word) * 30}px)`,
        }}
      >
        LockPad
      </div>
      <div
        style={{
          marginTop: 12,
          fontSize: 38,
          color: COLORS.muted,
          opacity: tagline,
          transform: `translateY(${(1 - tagline) * 30}px)`,
        }}
      >
        The encrypted notepad for text you can't afford to lose or leak.
      </div>
    </AbsoluteFill>
  );
};

export const Logo: React.FC = () => <LogoCard still={false} />;

// The settled logo from its first frame on. The video opens on it because GitHub's player shows the first frame
// as the preview before play.
export const TitleCard: React.FC = () => <LogoCard still />;
