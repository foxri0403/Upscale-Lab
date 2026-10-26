// One-time development helper. Downloaded files are committed so runtime needs no photo service.
import { mkdir, writeFile } from "node:fs/promises";
const photos = {
  lake: "photo-1501785888041-af3ef285b470",
  city: "photo-1519501025264-65ba15a82390",
  stars: "photo-1462331940025-496dfbfc7564",
  ocean: "photo-1507525428034-b723cf961d3e",
  forest: "photo-1448375240586-882707db888b",
  mountain: "photo-1454496522488-7a8e488e8606",
  sunset: "photo-1472120435266-53107fd0c44a",
};
await mkdir(new URL("../public/images/", import.meta.url), { recursive: true });
for (const [name, id] of Object.entries(photos)) {
  const response = await fetch(
    `https://images.unsplash.com/${id}?auto=format&fit=crop&w=1400&q=85`,
  );
  if (
    !response.ok ||
    !response.headers.get("content-type")?.startsWith("image/")
  )
    throw new Error(`${name}: HTTP ${response.status}`);
  await writeFile(
    new URL(`../public/images/${name}.jpg`, import.meta.url),
    new Uint8Array(await response.arrayBuffer()),
  );
  console.log(`Saved ${name}`);
}
