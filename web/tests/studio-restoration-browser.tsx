// Exercises the real Workspace -> Studio -> Canvas wiring without accounts or GPU jobs.
import { createRoot } from "react-dom/client";
import { Workspace } from "../src/Workspace";
import { defaultEdit, sampleLibrary } from "../src/model";
import { showcaseCharacter, showcaseRig } from "../src/showcase-character";
import "../src/styles.css";
import "../src/responsive.css";

const character = new URLSearchParams(location.search).has("character");
const id = `restoration-fixture-${character ? "character" : "subject"}`;
const library = sampleLibrary();
const picture = library.images.find(image => image.id === "fox")!;
picture.name = character ? "캐릭터 미리보기 복구 검사" : "수동 분리 복구 검사";
picture.edit = { ...defaultEdit };
picture.width = 1536; picture.height = 1024;
if (character) {
  picture.src = "/images/live2d-character-v2.png";
  picture.width = showcaseCharacter.width;
  picture.height = showcaseCharacter.height;
  picture.background = undefined;
  picture.foregrounds = undefined;
  picture.features = ["Live 2D"];
  const layers = [...showcaseCharacter.layers].sort((a, b) => a.renderOrder - b.renderOrder);
  picture.layers = layers.map((layer, layerOrder) => ({
    id: layer.name, layerType: layer.semanticType, layerOrder, automaticOrder: layerOrder,
    imageUrl: layer.src, movementX: 0, movementY: 0,
  }));
  picture.edit = { ...defaultEdit, rig: { ...showcaseRig, partAssignments: Object.fromEntries(layers.map(layer => [layer.name, layer.part])) } };
}
const storageKey = `upscale.demo.library.v1.${id}`;
if (!character || !localStorage.getItem(storageKey)) localStorage.setItem(storageKey, JSON.stringify(library));
createRoot(document.getElementById("root")!).render(<Workspace
  session={{ id, name: "복구 검사", email: "fixture@upscalelab.local" }}
  route={character ? "/app/studio?id=fox&tool=live" : "/app/studio?id=fox"} logout={() => {}} />);
