import React from "react";
import { COLORS, FONTS, UI } from "../theme";

const field: React.CSSProperties = {
  height: 22 * UI,
  boxSizing: "border-box",
  padding: `0 ${4 * UI}px`,
  display: "flex",
  alignItems: "center",
  backgroundColor: COLORS.editor,
  border: `1px solid ${COLORS.border}`,
  overflow: "hidden",
  whiteSpace: "nowrap",
};

const button: React.CSSProperties = {
  height: 22 * UI,
  boxSizing: "border-box",
  padding: `0 ${8 * UI}px`,
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  backgroundColor: COLORS.control,
  border: `1px solid ${COLORS.controlBorder}`,
};

const label: React.CSSProperties = {
  marginTop: 12 * UI,
  marginBottom: 4 * UI,
};

// LockPad's unlock page (UnlockView.xaml): notebook file, master password, optional key file, New notebook / Unlock.
export const UnlockScreen: React.FC<{
  passwordLength: number;
  unlockPressed: boolean;
  message: string | null;
}> = ({ passwordLength, unlockPressed, message }) => {
  return (
    <div
      style={{
        position: "absolute",
        inset: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
    >
      <div style={{ width: 480 * UI }}>
        <div style={{ fontSize: 20 * UI }}>Unlock notebook</div>

        <div style={label}>Notebook file</div>
        <div style={{ display: "flex" }}>
          <div style={{ ...field, flex: 1 }}>
            C:\Users\you\Documents\private.kdbx
          </div>
          <div style={{ ...button, marginLeft: 8 * UI, minWidth: 120 * UI }}>
            Browse notebook…
          </div>
        </div>

        <div style={label}>Master password</div>
        <div style={{ ...field, letterSpacing: 2 * UI, fontSize: 10 * UI }}>
          {"●".repeat(passwordLength)}
        </div>

        <div style={label}>Key file (optional)</div>
        <div style={{ display: "flex" }}>
          <div style={{ ...field, flex: 1 }} />
          <div style={{ ...button, marginLeft: 8 * UI, minWidth: 120 * UI }}>
            Browse key file…
          </div>
        </div>

        <div
          style={{
            marginTop: 16 * UI,
            display: "flex",
            justifyContent: "space-between",
          }}
        >
          <div style={{ ...button, minWidth: 110 * UI }}>New notebook…</div>
          <div
            style={{
              ...button,
              minWidth: 90 * UI,
              color: "#FFFFFF",
              backgroundColor: unlockPressed ? "#0B4F7D" : COLORS.primary,
              border: `1px solid ${COLORS.accent}`,
            }}
          >
            Unlock
          </div>
        </div>

        <div
          style={{
            marginTop: 12 * UI,
            height: 16 * UI,
            color: COLORS.muted,
            fontFamily: FONTS.sans,
          }}
        >
          {message}
        </div>
      </div>
    </div>
  );
};
