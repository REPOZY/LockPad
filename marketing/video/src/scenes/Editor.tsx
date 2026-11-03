import React from "react";
import {
  AbsoluteFill,
  Easing,
  interpolate,
  useCurrentFrame,
} from "remotion";
import { AppWindow } from "../components/AppWindow";
import { Caption } from "../components/Caption";
import { EditorScreen } from "../components/EditorScreen";
import {
  NOTES,
  SEED_TEXT,
  WINDOW_CENTER,
  WINDOW_H,
  WINDOW_RIGHT,
  WINDOW_W,
} from "./shared";

// The move starts once the fade from Unlock (same window, same place) is over, so the two never show offset.
const MOVE_START = 15;
const MOVE_END = 45;
const TYPE_START = 25;
const TYPE_END = 125;
const SECOND_CAPTION = 120;

export const Editor: React.FC = () => {
  const frame = useCurrentFrame();
  const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
  const move = interpolate(frame, [MOVE_START, MOVE_END], [0, 1], {
    ...clamp,
    easing: Easing.inOut(Easing.cubic),
  });
  const x = interpolate(move, [0, 1], [WINDOW_CENTER.x, WINDOW_RIGHT.x]);
  const y = interpolate(move, [0, 1], [WINDOW_CENTER.y, WINDOW_RIGHT.y]);
  const scale = interpolate(move, [0, 1], [WINDOW_CENTER.scale, WINDOW_RIGHT.scale]);

  const typed = Math.round(
    interpolate(frame, [TYPE_START, TYPE_END], [0, SEED_TEXT.length], clamp),
  );
  const dirty = typed > 0;
  const typing = typed > 0 && typed < SEED_TEXT.length;

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 110,
          width: 580,
          top: 0,
          bottom: 0,
          display: "flex",
          alignItems: "center",
        }}
      >
        <div style={{ position: "relative", width: "100%", height: 260 }}>
          <div style={{ position: "absolute", top: 0 }}>
            <Caption
              title="AES-256 + Argon2id"
              subtitle="Every note, encrypted at rest."
              startFrame={20}
              endFrame={SECOND_CAPTION}
              align="left"
            />
          </div>
          <div style={{ position: "absolute", top: 0 }}>
            <Caption
              title="Standard KDBX 4.1"
              subtitle="KeePassXC opens it too."
              startFrame={SECOND_CAPTION}
              endFrame={null}
              align="left"
            />
          </div>
        </div>
      </div>
      <div
        style={{
          position: "absolute",
          left: x,
          top: y,
          transform: `scale(${scale})`,
          transformOrigin: "top left",
        }}
      >
        <AppWindow
          title={dirty ? "*notes.kdbx - LockPad" : "notes.kdbx - LockPad"}
          statusLeft={dirty ? "Unsaved changes" : "Saved"}
          statusRight="Locks in 5:00"
          width={WINDOW_W}
          height={WINDOW_H}
        >
          <EditorScreen
            notes={NOTES}
            selectedIndex={0}
            text={SEED_TEXT.slice(0, typed)}
            caretVisible={typing || Math.floor(frame / 15) % 2 === 0}
            selection={null}
          />
        </AppWindow>
      </div>
    </AbsoluteFill>
  );
};
