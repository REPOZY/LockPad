import type {
  TransitionPresentation,
  TransitionPresentationComponentProps,
} from "@remotion/transitions";
import React from "react";
import { AbsoluteFill, interpolate } from "remotion";

type DipProps = Record<string, never>;

// Fades the old scene out in the first half and the new scene in during the second half, so two unrelated layouts
// never sit on top of each other. The shared Background shows through in the middle.
const DipPresentation: React.FC<
  TransitionPresentationComponentProps<DipProps>
> = ({ children, presentationDirection, presentationProgress }) => {
  const opacity =
    presentationDirection === "entering"
      ? interpolate(presentationProgress, [0.5, 1], [0, 1], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
        })
      : interpolate(presentationProgress, [0, 0.5], [1, 0], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
        });

  return <AbsoluteFill style={{ opacity }}>{children}</AbsoluteFill>;
};

export const dip = (): TransitionPresentation<DipProps> => {
  return { component: DipPresentation, props: {} };
};
