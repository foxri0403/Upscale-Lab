// Exercises the real Workspace -> Studio -> Canvas wiring without accounts or GPU jobs.
import { createRoot } from "react-dom/client";
import { Workspace } from "../src/Workspace";
import { defaultEdit, sampleLibrary } from "../src/model";
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
  picture.src = "/images/fox.png";
  picture.layers = ["Face", "FrontHair"].map((layerType, layerOrder) => ({
    id: `fixture-part-${layerOrder}`, layerType, layerOrder,
    imageUrl: "/images/fox-foreground.png", movementX: 0, movementY: 0,
  }));
  // Force the no-background branch despite the existing fox demo shortcut.
  picture.separationDisabled = true;
}
localStorage.setItem(`upscale.demo.library.v1.${id}`, JSON.stringify(library));
createRoot(document.getElementById("root")!).render(<Workspace
  session={{ id, name: "복구 검사", email: "fixture@upscalelab.local" }}
  route="/app/studio?id=fox" logout={() => {}} />);
