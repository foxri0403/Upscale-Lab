import type { Device, Picture } from "./model";

export function ownedExplorePicture(picture: Picture, userId: string, pictures: Picture[]): Picture | undefined {
  if (picture.ownerId && picture.ownerId !== userId) return undefined;
  return pictures.find(candidate =>
    !!picture.assetId && (candidate.assetId === picture.assetId || candidate.sharedAssetIds?.includes(picture.assetId)) ||
    !!picture.galleryPostId && (candidate.galleryPostId === picture.galleryPostId || candidate.galleryPostIds?.includes(picture.galleryPostId)) ||
    !!picture.sharedId && candidate.sharedId === picture.sharedId ||
    picture.ownerId === userId && candidate.id === picture.id,
  );
}

export function isOwnExplorePicture(picture: Picture, userId: string, pictures: Picture[]): boolean {
  return picture.ownerId === userId || !!ownedExplorePicture(picture, userId, pictures);
}

export function wallpaperTargets(devices: Device[]) {
  return devices.filter(device => device.online &&
    (!("connection" in device) || device.connection === undefined || device.connection === "connected")).flatMap(device => device.displays
    .filter(display => display.connected && display.canApplyWallpaper !== false)
    .map(display => ({ id: `${device.id}/${display.id}`, deviceId: device.id, displayId: display.id,
      name: `${device.name} · ${display.name}` })));
}
