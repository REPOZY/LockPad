import React from "react";
import { interpolate } from "remotion";
import { COLORS } from "../theme";

// Paths from docs/icon/lockpad.svg (256 px design), split into parts that animate separately.
const PAGE =
  "M 56,12 H 160 L 212,64 V 220 Q 212,236 196,236 H 56 Q 40,236 40,220 V 28 Q 40,12 56,12 Z";
const FOLD = "M 160,12 V 52 Q 160,64 172,64 H 212 Z";
const SHACKLE = "M 156,160 V 128 A 28,28 0 0 1 212,128 V 160";
const LINES: [number, number, number][] = [
  [96, 72, 184],
  [128, 72, 184],
  [160, 72, 184],
  [192, 72, 128],
];

const clamp = {
  extrapolateLeft: "clamp",
  extrapolateRight: "clamp",
} as const;

export const LockPadIcon: React.FC<{
  size: number;
  // 0..1: draws the page outline, then the text lines.
  pageProgress: number;
  // 0..1 (may overshoot from a spring): moves the shackle from open to closed.
  shackleProgress: number;
}> = ({ size, pageProgress, shackleProgress }) => {
  const outline = interpolate(pageProgress, [0, 0.6], [0, 1], clamp);
  const fill = interpolate(pageProgress, [0.4, 0.7], [0, 1], clamp);
  const lock = interpolate(pageProgress, [0.6, 1], [0, 1], clamp);
  const open = 1 - shackleProgress;

  return (
    <svg width={size} height={size} viewBox="0 0 256 256">
      <path
        d={PAGE}
        transform="translate(3,5)"
        fill="#0F172A"
        fillOpacity={0.22 * fill}
      />
      <path
        d={PAGE}
        fill="#FFFFFF"
        fillOpacity={fill}
        stroke={COLORS.iconOutline}
        strokeWidth={6}
        strokeLinejoin="round"
        pathLength={1}
        strokeDasharray={1}
        strokeDashoffset={1 - outline}
      />
      <path
        d={FOLD}
        fill={COLORS.iconFold}
        fillOpacity={fill}
        stroke={COLORS.iconOutline}
        strokeWidth={6}
        strokeLinejoin="round"
        opacity={fill}
      />
      {LINES.map(([y, x1, x2], i) => {
        const grow = interpolate(
          pageProgress,
          [0.45 + i * 0.08, 0.7 + i * 0.08],
          [0, 1],
          clamp,
        );
        return (
          <line
            key={y}
            x1={x1}
            y1={y}
            x2={x1 + (x2 - x1) * grow}
            y2={y}
            stroke={COLORS.iconLines}
            strokeWidth={12}
            strokeLinecap="round"
            opacity={grow > 0 ? 1 : 0}
          />
        );
      })}
      <g
        opacity={lock}
        transform={`translate(184 200) scale(${0.6 + 0.4 * lock}) translate(-184 -200)`}
      >
        {/* Open: lifted and swung out around the right leg. */}
        <g
          transform={`translate(0 ${-34 * open}) rotate(${-28 * open} 212 160)`}
        >
          <path
            d={SHACKLE}
            fill="none"
            stroke="#FFFFFF"
            strokeWidth={34}
            strokeLinecap="round"
            strokeLinejoin="round"
          />
          <path
            d={SHACKLE}
            fill="none"
            stroke={COLORS.iconShackle}
            strokeWidth={18}
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </g>
        <rect x={122} y={148} width={124} height={104} rx={24} fill="#FFFFFF" />
        <rect x={130} y={156} width={108} height={88} rx={16} fill={COLORS.iconNavy} />
        <circle cx={184} cy={190} r={13} fill="#FFFFFF" />
        <rect x={178} y={194} width={12} height={28} rx={3} fill="#FFFFFF" />
      </g>
    </svg>
  );
};
