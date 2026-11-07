/** Immutable wallpaper assets are not editable projects. A public gallery post has
 * its own server-side import endpoint, while assets need an editable PNG copy.
 * Use authenticated first-party content endpoints instead of presigned S3 URLs.
 */
export type SharedAssetSource = {
  assetId?: string;
  projectId?: string;
  background?: string;
  foregrounds?: string[];
  exportSources?: { foregrounds?: string[] };
};

export function needsEditableCopy(picture: SharedAssetSource): boolean {
  return !!picture.assetId && !picture.projectId;
}

export function assetCharacterLayers(picture: SharedAssetSource): { source: string; endpoint?: string }[] {
  // A received public share link also supplies authenticated/anonymous local
  // content endpoints for every part, even though it has no asset ID.
  const linkedParts = (picture.exportSources?.foregrounds?.length ?? 0) >= 2;
  if (picture.projectId || picture.background || (picture.foregrounds?.length ?? 0) < 2 ||
      (!picture.assetId && !linkedParts)) return [];
  return picture.foregrounds!.map((source, index) => ({
    source,
    endpoint: picture.exportSources?.foregrounds?.[index],
  }));
}
