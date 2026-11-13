import type { Device, Picture } from "./model";

export function wallpaperSourceForDisplay(current: Picture | undefined, images: Picture[]): Picture | undefined {
  if (!current?.isDeviceVariant || !current.sourceProjectId) return current;
  return images.find(image => image.projectId === current.sourceProjectId && !image.isDraft && !image.isReadOnly)
    ?? current;
}

// Use the built-in display's logical short edge; external monitors must not
// turn a phone into a tablet. Older agents without density remain phones.
export function androidFormFactor(displays: {
  width: number; height: number; scale?: number; isPrimary?: boolean;
  canApplyWallpaper?: boolean; isConnected: boolean;
}[]): "phone" | "tablet" {
  const builtIn = displays.find(d => d.isPrimary && d.canApplyWallpaper !== false)
    ?? displays.find(d => d.isConnected && d.canApplyWallpaper === true);
  if (!builtIn || !Number.isFinite(builtIn.scale) || builtIn.scale! <= 0) return "phone";
  return Math.min(builtIn.width, builtIn.height) / builtIn.scale! >= 600 ? "tablet" : "phone";
}

export function registeredDevices(devices: Device[]): Device[] {
  const time = (device: Device) => {
    const value = Date.parse(device.created || "");
    return Number.isFinite(value) ? value : Number.POSITIVE_INFINITY;
  };
  return [...devices].sort((a, b) => time(a) === time(b) ? (time(a) === Number.POSITIVE_INFINITY ? 0 : a.id.localeCompare(b.id)) : time(a) - time(b));
}

export function deviceDisplays(device: Device) {
  const rank = (display: Device["displays"][number]) => display.canApplyWallpaper === false ? 2
    : display.target === "Lock" || display.id === "lock" ? 0 : 1;
  return device.platform === "Android"
    ? [...device.displays].sort((a, b) => rank(a) - rank(b) || a.id.localeCompare(b.id)) : device.displays;
}
