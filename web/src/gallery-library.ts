import type { Picture } from "./model";

// Immutable versions still live in library.assets for device previews and direct studio links.
// A source project and its application snapshots occupy one card in My Images.
export function galleryImages(projects: Picture[], assets: Picture[]): Picture[] {
  const ids = new Set(projects.map(p => p.projectId).filter(Boolean));
  return [...projects, ...assets.filter(a => !a.sourceProjectId || !ids.has(a.sourceProjectId))];
}
