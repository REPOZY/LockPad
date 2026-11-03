import React from "react";
import { COLORS, FONTS, UI } from "../theme";
import { LockPadIcon } from "./LockPadIcon";

const CaptionButton: React.FC<{ glyph: "min" | "max" | "close" }> = ({
  glyph,
}) => {
  const s = 10 * UI;
  return (
    <div
      style={{
        width: 46 * UI,
        height: "100%",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
    >
      <svg width={s} height={s} viewBox="0 0 10 10">
        {glyph === "min" ? (
          <line x1={0} y1={5} x2={10} y2={5} stroke={COLORS.text} strokeWidth={0.8} />
        ) : glyph === "max" ? (
          <rect x={0.5} y={0.5} width={9} height={9} fill="none" stroke={COLORS.text} strokeWidth={0.8} rx={1} />
        ) : (
          <path d="M0 0 L10 10 M10 0 L0 10" stroke={COLORS.text} strokeWidth={0.8} />
        )}
      </svg>
    </div>
  );
};

// LockPad's main window (MainWindow.xaml) with Windows 11 dark chrome: title bar, menu bar, content, status bar.
export const AppWindow: React.FC<{
  title: string;
  // The status bar is shown only while the editor is shown (statusLeft !== null), as in the app.
  statusLeft: string | null;
  statusRight: string | null;
  width: number;
  height: number;
  children: React.ReactNode;
}> = ({ title, statusLeft, statusRight, width, height, children }) => {
  const fontSize = 12 * UI;
  return (
    <div
      style={{
        width,
        height,
        display: "flex",
        flexDirection: "column",
        backgroundColor: COLORS.window,
        border: `1px solid ${COLORS.dialogBorder}`,
        borderRadius: 12,
        overflow: "hidden",
        boxShadow:
          "0 40px 120px rgba(0,0,0,0.55), 0 0 0 1px rgba(255,255,255,0.04), 0 0 80px rgba(77,163,255,0.08)",
        fontFamily: FONTS.sans,
        fontSize,
        color: COLORS.text,
      }}
    >
      <div
        style={{
          height: 32 * UI,
          flexShrink: 0,
          display: "flex",
          alignItems: "center",
          backgroundColor: "#202020",
        }}
      >
        <div style={{ marginLeft: 10 * UI, marginRight: 8 * UI, display: "flex" }}>
          <LockPadIcon size={16 * UI} pageProgress={1} shackleProgress={1} />
        </div>
        <div style={{ flex: 1, fontSize }}>{title}</div>
        <CaptionButton glyph="min" />
        <CaptionButton glyph="max" />
        <CaptionButton glyph="close" />
      </div>
      <div
        style={{
          flexShrink: 0,
          display: "flex",
          backgroundColor: COLORS.panel,
          padding: `${2 * UI}px ${2 * UI}px`,
        }}
      >
        {["File", "Settings", "Help"].map((item) => (
          <div key={item} style={{ padding: `${2 * UI}px ${7 * UI}px` }}>
            {item}
          </div>
        ))}
      </div>
      <div style={{ flex: 1, position: "relative", overflow: "hidden" }}>
        {children}
      </div>
      {statusLeft !== null ? (
        <div
          style={{
            flexShrink: 0,
            height: 24 * UI,
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            padding: `0 ${8 * UI}px`,
            backgroundColor: COLORS.panel,
          }}
        >
          <span>{statusLeft}</span>
          <span style={{ fontVariantNumeric: "tabular-nums" }}>{statusRight}</span>
        </div>
      ) : null}
    </div>
  );
};
