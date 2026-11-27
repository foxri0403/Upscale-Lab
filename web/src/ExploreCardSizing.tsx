import { ImageCard } from "./ImageCard";
import { imageFeatures } from "./image-features";
import type { Picture } from "./model";

function resolutionLength(picture: Picture) {
  const { width, height } = picture;
  return width && height ? String(width).length + String(height).length : 0;
}

const ignoreAction = () => {};

export function ExploreCardSizing({ pictures }: { pictures: Picture[] }) {
  // Feature badges can wrap at narrower widths. Keep a title and a resolution
  // reference per badge combination from the full filtered list.
  const groups = new Map<string, { title: Picture; resolution: Picture }>();
  for (const picture of pictures) {
    const key = imageFeatures(picture).join(",");
    const group = groups.get(key);
    if (!group) {
      groups.set(key, { title: picture, resolution: picture });
      continue;
    }
    if (picture.name.length > group.title.name.length) group.title = picture;
    if (resolutionLength(picture) > resolutionLength(group.resolution)) group.resolution = picture;
  }
  const references = [...groups.values()].flatMap(({ title, resolution }) =>
    title === resolution ? [title] : [title, resolution],
  );
  return (
    <div className="explore-card-sizing" aria-hidden="true" inert>
      {references.map(picture => (
        <ImageCard key={picture.id} picture={picture} explore layoutOnly
          open={ignoreAction} editMetadata={ignoreAction} remove={ignoreAction} toggleLike={ignoreAction} />
      ))}
    </div>
  );
}
