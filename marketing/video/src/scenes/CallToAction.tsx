import React from "react";
import {
  AbsoluteFill,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { LockPadIcon } from "../components/LockPadIcon";
import { COLORS, FONTS, GITHUB_URL } from "../theme";

export const CallToAction: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const icon = spring({ frame, fps, config: { damping: 14 } });
  const word = spring({ frame: frame - 10, fps, config: { damping: 200 } });
  const line = spring({ frame: frame - 22, fps, config: { damping: 200 } });
  const url = spring({ frame: frame - 40, fps, config: { damping: 200 } });

  const rise = (p: number): React.CSSProperties => ({
    opacity: p,
    transform: `translateY(${(1 - p) * 30}px)`,
  });

  return (
    <AbsoluteFill
      style={{
        alignItems: "center",
        justifyContent: "center",
        fontFamily: FONTS.sans,
        color: COLORS.text,
      }}
    >
      <div style={{ transform: `scale(${icon})` }}>
        <LockPadIcon size={180} pageProgress={1} shackleProgress={1} />
      </div>
      <div
        style={{
          marginTop: 20,
          fontSize: 110,
          fontWeight: 700,
          letterSpacing: -3.5,
          ...rise(word),
        }}
      >
        LockPad
      </div>
      <div style={{ marginTop: 8, fontSize: 34, color: COLORS.muted, ...rise(line) }}>
        Free & open source · Windows 10/11
      </div>
      <div
        style={{
          marginTop: 44,
          padding: "16px 36px",
          borderRadius: 999,
          border: `2px solid ${COLORS.accent}`,
          background: `${COLORS.accent}14`,
          boxShadow: `0 0 40px ${COLORS.accent}33`,
          fontFamily: FONTS.mono,
          fontSize: 34,
          ...rise(url),
        }}
      >
        {GITHUB_URL}
      </div>
    </AbsoluteFill>
  );
};
