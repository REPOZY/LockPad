import React from "react";
import { AbsoluteFill, Composition, Folder } from "remotion";
import { Background } from "./components/Background";
import { LockPadPromo } from "./LockPadPromo";
import { AutoLock } from "./scenes/AutoLock";
import { CallToAction } from "./scenes/CallToAction";
import { Clipboard } from "./scenes/Clipboard";
import { Editor } from "./scenes/Editor";
import { Features } from "./scenes/Features";
import { Hook } from "./scenes/Hook";
import { Logo, TitleCard } from "./scenes/Logo";
import { SafeSave } from "./scenes/SafeSave";
import { ScreenCapture } from "./scenes/ScreenCapture";
import { Unlock } from "./scenes/Unlock";
import { FPS, HEIGHT, SCENE_FRAMES, TOTAL_FRAMES, WIDTH } from "./theme";

// One scene on the stage background, so each scene can be previewed and rendered on its own.
const preview = (Scene: React.FC): React.FC => {
  const ScenePreview: React.FC = () => (
    <AbsoluteFill>
      <Background />
      <Scene />
    </AbsoluteFill>
  );
  return ScenePreview;
};

const SCENES: { id: string; component: React.FC; frames: number }[] = [
  { id: "Scene-TitleCard", component: preview(TitleCard), frames: SCENE_FRAMES.title },
  { id: "Scene-Hook", component: preview(Hook), frames: SCENE_FRAMES.hook },
  { id: "Scene-Logo", component: preview(Logo), frames: SCENE_FRAMES.logo },
  { id: "Scene-Unlock", component: preview(Unlock), frames: SCENE_FRAMES.unlock },
  { id: "Scene-Editor", component: preview(Editor), frames: SCENE_FRAMES.editor },
  { id: "Scene-SafeSave", component: preview(SafeSave), frames: SCENE_FRAMES.safeSave },
  { id: "Scene-AutoLock", component: preview(AutoLock), frames: SCENE_FRAMES.autoLock },
  { id: "Scene-Clipboard", component: preview(Clipboard), frames: SCENE_FRAMES.clipboard },
  {
    id: "Scene-ScreenCapture",
    component: preview(ScreenCapture),
    frames: SCENE_FRAMES.screenCapture,
  },
  { id: "Scene-Features", component: preview(Features), frames: SCENE_FRAMES.features },
  { id: "Scene-CallToAction", component: preview(CallToAction), frames: SCENE_FRAMES.cta },
];

export const RemotionRoot: React.FC = () => {
  return (
    <>
      <Composition
        id="LockPadPromo"
        component={LockPadPromo}
        durationInFrames={TOTAL_FRAMES}
        fps={FPS}
        width={WIDTH}
        height={HEIGHT}
      />
      <Folder name="Scenes">
        {SCENES.map((scene) => (
          <Composition
            key={scene.id}
            id={scene.id}
            component={scene.component}
            durationInFrames={scene.frames}
            fps={FPS}
            width={WIDTH}
            height={HEIGHT}
          />
        ))}
      </Folder>
    </>
  );
};
