import { defaultHeadRig, type HeadRig } from "./model";
import { rigPart, type RigPart } from "./character-rig";
import data from "./showcase-character-data.json";

export type ShowcaseLayer = { file: string; src: string; name: string; semanticType: string; part: RigPart; renderOrder: number };
export type Live2DExample = "portrait" | "character";

export const portraitImage = "/images/live2d-portrait.jpg";
export const characterImage = "/images/live2d-character-v2.png";

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

const portraitLayers = [
  { name: "background", part: "fixed" },
  { name: "body", part: "fixed" },
  { name: "face", part: "head" },
  { name: "irises", part: "eye" },
  { name: "eyelids", part: "head" },
  { name: "hair", part: "frontHair" },
] satisfies { name: string; part: RigPart }[];

export const showcasePortrait = {
  width: 1024, height: 1536,
  layers: portraitLayers.map((layer, renderOrder) => ({
    ...layer, renderOrder, file: layer.name + ".png",
    src: `/images/live2d-portrait-parts/${layer.name}.png`,
  })),
};

export const portraitRig: HeadRig = {
  ...showcaseRig, pivotY: 62, headTilt: 48, eyeMove: 22, hairSway: 46,
};
