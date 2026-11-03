import React from "react";
import {
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { COLORS, FONTS } from "../theme";

// A headline with an optional muted subline. Rises in at startFrame; fades out over 10 frames before endFrame.
export const Caption: React.FC<{
  title: string;
  subtitle: string | null;
  startFrame: number;
  endFrame: number | null;
  align: "left" | "center";
}> = ({ title, subtitle, startFrame, endFrame, align }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const enter = spring({
    frame: frame - startFrame,
    fps,
    config: { damping: 200 },
  });
  const subEnter = spring({
    frame: frame - startFrame - 6,
    fps,
    config: { damping: 200 },
  });
  const exit =
    endFrame === null
      ? 1
      : interpolate(frame, [endFrame - 10, endFrame], [1, 0], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
        });

  return (
    <div
      style={{
        fontFamily: FONTS.sans,
        textAlign: align,
        opacity: exit,
      }}
    >
      <div
        style={{
          fontSize: 64,
          fontWeight: 700,
          color: COLORS.text,
          letterSpacing: -1.5,
          lineHeight: 1.1,
          opacity: enter,
          transform: `translateY(${(1 - enter) * 24}px)`,
        }}
      >
        {title}
      </div>
      {subtitle !== null ? (
        <div
          style={{
            marginTop: 16,
            fontSize: 32,
            fontWeight: 400,
            color: COLORS.muted,
            lineHeight: 1.3,
            opacity: subEnter,
            transform: `translateY(${(1 - subEnter) * 24}px)`,
          }}
        >
          {subtitle}
        </div>
      ) : null}
    </div>
  );
};
