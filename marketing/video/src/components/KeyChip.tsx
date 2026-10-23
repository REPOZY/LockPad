import React from "react";
import { COLORS, FONTS } from "../theme";

// A keyboard shortcut drawn as key caps, e.g. ["Ctrl", "S"].
export const KeyChip: React.FC<{ keys: string[]; pressed: boolean }> = ({
  keys,
  pressed,
}) => {
  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 12,
        fontFamily: FONTS.sans,
      }}
    >
      {keys.map((key, i) => (
        <React.Fragment key={key}>
          {i > 0 ? (
            <span style={{ color: COLORS.muted, fontSize: 28, fontWeight: 600 }}>
              +
            </span>
          ) : null}
          <div
            style={{
              minWidth: 68,
              height: 68,
              padding: "0 20px",
              boxSizing: "border-box",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              borderRadius: 14,
              background: "linear-gradient(180deg, #273149 0%, #1A2134 100%)",
              border: `1.5px solid ${pressed ? COLORS.accent : "rgba(255,255,255,0.16)"}`,
              borderBottomWidth: pressed ? 1.5 : 5,
              boxShadow: pressed
                ? `0 0 28px ${COLORS.accent}66`
                : "0 8px 20px rgba(0,0,0,0.35)",
              transform: `translateY(${pressed ? 4 : 0}px) scale(${pressed ? 0.96 : 1})`,
              color: COLORS.text,
              fontSize: 28,
              fontWeight: 600,
            }}
          >
            {key}
          </div>
        </React.Fragment>
      ))}
    </div>
  );
};
