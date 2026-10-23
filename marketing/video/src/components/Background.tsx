import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame } from "remotion";
import { COLORS } from "../theme";

export const Background: React.FC = () => {
  const frame = useCurrentFrame();
  // Slow drift so the stage never looks frozen.
  const drift = interpolate(frame, [0, 1800], [0, 1]);
  const navyX = 18 + drift * 10;
  const blueX = 82 - drift * 12;

  return (
    <AbsoluteFill style={{ backgroundColor: COLORS.stage }}>
      <AbsoluteFill
        style={{
          background: `radial-gradient(circle at ${navyX}% 20%, ${COLORS.glowNavy}55 0%, transparent 45%)`,
        }}
      />
      <AbsoluteFill
        style={{
          background: `radial-gradient(circle at ${blueX}% 85%, ${COLORS.glowBlue}2A 0%, transparent 40%)`,
        }}
      />
      {/* Fine grid, faded towards the edges. */}
      <AbsoluteFill
        style={{
          backgroundImage:
            "linear-gradient(rgba(255,255,255,0.035) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.035) 1px, transparent 1px)",
          backgroundSize: "64px 64px",
          maskImage:
            "radial-gradient(ellipse at center, black 30%, transparent 75%)",
        }}
      />
    </AbsoluteFill>
  );
};
