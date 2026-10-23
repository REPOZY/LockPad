import React from "react";
import { COLORS, FONTS, UI } from "../theme";

const box: React.CSSProperties = {
  boxSizing: "border-box",
  backgroundColor: COLORS.editor,
  border: `1px solid ${COLORS.border}`,
};

const button: React.CSSProperties = {
  height: 22 * UI,
  boxSizing: "border-box",
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  backgroundColor: COLORS.control,
  border: `1px solid ${COLORS.controlBorder}`,
};

// LockPad's editor page (EditorView.xaml): filter, notes list and note buttons on the left; word wrap and the editor
// on the right.
export const EditorScreen: React.FC<{
  notes: string[];
  selectedIndex: number;
  text: string;
  caretVisible: boolean;
  // Highlighted character range [start, end) of text.
  selection: [number, number] | null;
}> = ({ notes, selectedIndex, text, caretVisible, selection }) => {
  const [selStart, selEnd] = selection ?? [text.length, text.length];
  const m = 8 * UI;

  return (
    <div style={{ position: "absolute", inset: 0, display: "flex" }}>
      <div
        style={{
          width: 240 * UI,
          flexShrink: 0,
          margin: m,
          display: "flex",
          flexDirection: "column",
        }}
      >
        <div style={{ color: COLORS.muted, marginBottom: 4 * UI }}>Filter</div>
        <div style={{ ...box, height: 22 * UI, marginBottom: 8 * UI }} />
        <div style={{ ...box, flex: 1, padding: 1 }}>
          {notes.map((note, i) => (
            <div
              key={note}
              style={{
                padding: `${3 * UI}px ${4 * UI}px`,
                backgroundColor:
                  i === selectedIndex ? COLORS.highlight : "transparent",
              }}
            >
              {note}
            </div>
          ))}
        </div>
        <div
          style={{
            marginTop: 8 * UI,
            display: "grid",
            gridTemplateColumns: "1fr 1fr",
            gap: 8 * UI,
          }}
        >
          {["New", "Rename", "Delete", "Undo delete"].map((b) => (
            <div key={b} style={button}>
              {b}
            </div>
          ))}
        </div>
      </div>

      <div style={{ width: 5 * UI, flexShrink: 0, backgroundColor: COLORS.border }} />

      <div
        style={{
          flex: 1,
          minWidth: 0,
          margin: m,
          display: "flex",
          flexDirection: "column",
        }}
      >
        <div
          style={{
            backgroundColor: COLORS.panel,
            marginBottom: 8 * UI,
            padding: `${4 * UI}px ${6 * UI}px`,
            display: "flex",
            alignItems: "center",
          }}
        >
          <svg width={14 * UI} height={14 * UI} viewBox="0 0 14 14">
            <rect x={0.5} y={0.5} width={13} height={13} fill={COLORS.control} stroke={COLORS.controlBorder} />
            <path d="M 2.5 7 L 5.5 10 L 11.5 3.5" stroke={COLORS.text} strokeWidth={2} fill="none" />
          </svg>
          <span style={{ marginLeft: 4 * UI }}>Word wrap</span>
        </div>
        <div
          style={{
            ...box,
            flex: 1,
            padding: 4 * UI,
            fontFamily: FONTS.mono,
            fontSize: 14 * UI,
            lineHeight: 1.45,
            whiteSpace: "pre-wrap",
            wordBreak: "break-word",
            overflow: "hidden",
          }}
        >
          {text.slice(0, selStart)}
          <span style={{ backgroundColor: `${COLORS.accent}66` }}>
            {text.slice(selStart, selEnd)}
          </span>
          {text.slice(selEnd)}
          <span
            style={{
              display: "inline-block",
              width: 2,
              height: "1.2em",
              verticalAlign: "text-bottom",
              backgroundColor: COLORS.text,
              opacity: caretVisible ? 1 : 0,
            }}
          />
        </div>
      </div>
    </div>
  );
};
