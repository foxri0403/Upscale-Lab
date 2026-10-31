import type { Device } from "./model";

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
