import { linearTiming, TransitionSeries } from "@remotion/transitions";
import { fade } from "@remotion/transitions/fade";
import { slide } from "@remotion/transitions/slide";
import React from "react";
import { AbsoluteFill } from "remotion";
import { Background } from "./components/Background";
import { dip } from "./components/dip";
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
import { SCENE_FRAMES, TRANSITION_FRAMES } from "./theme";

const timing = linearTiming({ durationInFrames: TRANSITION_FRAMES });

// Scenes are transparent; the shared Background shows through every scene and transition.
export const LockPadPromo: React.FC = () => {
  return (
    <AbsoluteFill>
      <Background />
      <TransitionSeries>
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.title}>
          <TitleCard />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={dip()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.hook}>
          <Hook />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={dip()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.logo}>
          <Logo />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={dip()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.unlock}>
          <Unlock />
        </TransitionSeries.Sequence>
        {/* The window is in the same place at the end of Unlock and the start of Editor, so the fade reads as one shot. */}
        <TransitionSeries.Transition presentation={fade()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.editor}>
          <Editor />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition
          presentation={slide({ direction: "from-right" })}
          timing={timing}
        />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.safeSave}>
          <SafeSave />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={dip()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.autoLock}>
          <AutoLock />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition
          presentation={slide({ direction: "from-right" })}
          timing={timing}
        />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.clipboard}>
          <Clipboard />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition
          presentation={slide({ direction: "from-right" })}
          timing={timing}
        />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.screenCapture}>
          <ScreenCapture />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={dip()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.features}>
          <Features />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={dip()} timing={timing} />
        <TransitionSeries.Sequence durationInFrames={SCENE_FRAMES.cta}>
          <CallToAction />
        </TransitionSeries.Sequence>
      </TransitionSeries>
    </AbsoluteFill>
  );
};
