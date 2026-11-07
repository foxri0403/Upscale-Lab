import { defaultHeadRig, type HeadRig } from "./model";
import { rigPart, type RigPart } from "./character-rig";
import data from "./showcase-character-data.json";

export type ShowcaseLayer = { file: string; src: string; name: string; semanticType: string; part: RigPart; renderOrder: number };

const collarOrder = data.layers.find(layer => layer.name === "topwear")!.order * 2;

export const showcaseCharacter: { width: number; height: number; layers: ShowcaseLayer[] } = {
  width: data.width, height: data.height,
  layers: data.layers.map(layer => ({ ...layer,
    src: `/images/live2d-character-parts/${layer.file}`,
    part: rigPart(layer.semanticType, layer.name),
    // Draw the neck over the back of the collar; its opening clip keeps the front rim intact.
    renderOrder: layer.name === "neck" ? collarOrder + 1 : layer.order * 2,
  })),
};

export const showcaseRig: HeadRig = {
  ...defaultHeadRig,
  enabled: true, headTilt: 60, eyeMove: 45, hairSway: 65,
  pivotX: 50, pivotY: 44, idle: true,
};
