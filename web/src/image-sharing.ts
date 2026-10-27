import type { Picture } from "./model";

export function isImageShared(picture?: Picture): boolean {
  return !!(picture?.sharedId || picture?.galleryPostId ||
    picture?.sharedAssetIds?.length || picture?.galleryPostIds?.length);
}

export function projectSharing(
  picture: Picture,
  assets: { id: string; isPublic: boolean; sourceProjectId?: string | null }[],
) {
  const sharedAssetIds = assets
    .filter((asset) => !!picture.projectId && asset.isPublic && asset.sourceProjectId === picture.projectId)
    .map((asset) => asset.id);
  return {
    ...picture,
    sharedAssetIds,
    sharedId: picture.galleryPostId || sharedAssetIds[0],
  };
}

export function sharingTargets(picture: Picture) {
  return {
    galleryIds: [...new Set([
      ...(picture.galleryPostIds || []),
      ...(picture.galleryPostId ? [picture.galleryPostId] : []),
    ])],
    assetIds: [...new Set([
      ...(picture.sharedAssetIds || []),
      ...(picture.assetId && picture.sharedId ? [picture.assetId] : []),
    ])],
  };
}
