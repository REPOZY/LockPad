import React from "react";
import {
  AbsoluteFill,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { COLORS, FONTS } from "../theme";

const PHRASES = ["Private keys.", "Seed phrases.", "PIN codes."];
const PHRASE_START = 8;
const PHRASE_GAP = 25;
const CHARS_PER_FRAME = 0.9;
const QUESTION_START = 92;

export const Hook: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const dim = interpolate(frame, [QUESTION_START - 8, QUESTION_START + 6], [1, 0.35], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const question = spring({
    frame: frame - QUESTION_START,
    fps,
    config: { damping: 200 },
  });

  return (
    <AbsoluteFill
      style={{
        justifyContent: "center",
        paddingLeft: 240,
        fontFamily: FONTS.sans,
      }}
    >
      {PHRASES.map((phrase, i) => {
        const start = PHRASE_START + i * PHRASE_GAP;
        const shown = Math.max(
          0,
          Math.min(phrase.length, Math.floor((frame - start) * CHARS_PER_FRAME)),
        );
        const typing = shown > 0 && shown < phrase.length;
        return (
          <div
            key={phrase}
            style={{
              fontSize: 104,
              fontWeight: 700,
              letterSpacing: -2.5,
              lineHeight: 1.12,
              color: COLORS.text,
              opacity: dim,
              height: 116,
            }}
          >
            {phrase.slice(0, shown)}
            {typing ? (
              <span style={{ color: COLORS.accent, fontWeight: 400 }}>|</span>
            ) : null}
          </div>
        );
      })}
      <div
        style={{
          marginTop: 40,
          fontSize: 68,
          fontWeight: 600,
          letterSpacing: -1.2,
          color: COLORS.accent,
          opacity: question,
          transform: `translateY(${(1 - question) * 30}px)`,
        }}
      >
        Where do you keep them?
      </div>
    </AbsoluteFill>
  );
};
