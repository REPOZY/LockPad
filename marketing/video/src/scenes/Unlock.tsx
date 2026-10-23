import React from "react";
import {
  AbsoluteFill,
  interpolate,
  spring,
  useCurrentFrame,
  useVideoConfig,
} from "remotion";
import { AppWindow } from "../components/AppWindow";
import { Caption } from "../components/Caption";
import { EditorScreen } from "../components/EditorScreen";
import { UnlockScreen } from "../components/UnlockScreen";
import { NOTES, WINDOW_CENTER, WINDOW_H, WINDOW_W } from "./shared";

const TYPE_START = 30;
const TYPE_END = 90;
const PASSWORD_LENGTH = 14;
const PRESS = 100;
const SWAP = 140;

export const Unlock: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const enter = spring({ frame, fps, config: { damping: 200 } });
  const clamp = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;
  const passwordLength = Math.round(
    interpolate(frame, [TYPE_START, TYPE_END], [0, PASSWORD_LENGTH], clamp),
  );
  const editor = interpolate(frame, [SWAP, SWAP + 15], [0, 1], clamp);
  const unlocked = frame >= SWAP + 7;

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          top: 64,
          display: "flex",
          justifyContent: "center",
        }}
      >
        <Caption
          title="Your notes, one master password."
          subtitle={null}
          startFrame={10}
          endFrame={null}
          align="center"
        />
      </div>
      <div
        style={{
          position: "absolute",
          left: WINDOW_CENTER.x,
          top: WINDOW_CENTER.y + (1 - enter) * 60,
          transform: `scale(${WINDOW_CENTER.scale})`,
          transformOrigin: "top left",
          opacity: enter,
        }}
      >
        <AppWindow
          title={unlocked ? "notes.kdbx - LockPad" : "LockPad"}
          statusLeft={unlocked ? "Saved" : null}
          statusRight={unlocked ? "Locks in 5:00" : null}
          width={WINDOW_W}
          height={WINDOW_H}
        >
          <AbsoluteFill style={{ opacity: 1 - editor }}>
            <UnlockScreen
              passwordLength={passwordLength}
              unlockPressed={frame >= PRESS && frame < PRESS + 10}
              message={frame >= PRESS + 10 ? "Unlocking…" : null}
            />
          </AbsoluteFill>
          <AbsoluteFill style={{ opacity: editor }}>
            <EditorScreen
              notes={NOTES}
              selectedIndex={0}
              text=""
              caretVisible={Math.floor(frame / 15) % 2 === 0}
              selection={null}
            />
          </AbsoluteFill>
        </AppWindow>
      </div>
    </AbsoluteFill>
  );
};
