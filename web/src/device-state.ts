import type { CloudDevice, CloudState } from "./api";
import type { Device } from "./model";
import { registeredDevices } from "./device-layout.ts";
export function mapDeviceLibrary(states: CloudState[], registrations: CloudDevice[] = []) {
  const created = new Map(registrations.map(device => [device.id, device.createdAt]));
  const mappedDevices: Device[] = states.map((s) => ({
    id: s.id,
    created: created.get(s.id),
    name: s.name,
    platform: s.platform,
    osVersion: s.osVersion,
    physicalDisplayCount: s.displays.filter(d => d.isConnected).length,
    online: s.recentlySeen,
    connection: s.isLinked && s.recentlySeen ? "connected" : s.isLinked && !s.lastSeenAt ? "connecting" : "disconnected",
    supportsWallpaperRemoval: s.supportsWallpaperRemoval ?? false,
    displays: s.displays.flatMap((d) => {
      const assignments: CloudState["displays"][number]["wallpapers"] = s.platform === "Android" && d.canApplyWallpaper !== false
        ? (["Lock", "Home"] as const).map(target => d.wallpapers.find(w => w.target === target) || { target, observation: "Unknown" })
        : d.wallpapers.length
        ? d.wallpapers
        : [
            {
              target: s.platform === "Android" ? "Home" : "Desktop",
              desiredAssetId: undefined,
              reportedAssetId: undefined,
              observation: "Unknown",
              pendingRemoval: false,
            },
          ];
      return assignments.map((w) => ({
        id: `${d.id}:${w.target}`,
        nativeId: d.id,
        target: w.target,
        name:
          s.platform === "Android" && d.canApplyWallpaper !== false
            ? w.target === "Lock"
              ? "잠금 화면"
              : "홈 화면"
            : d.name,
        resolution: `${d.width} × ${d.height}`,
        connected: d.isConnected,
        refreshRateHz: d.refreshRateHz,
        canApplyWallpaper: d.canApplyWallpaper ?? true,
        isPrimary: d.isPrimary,
        observation: w.observation,
        removing: !!w.pendingRemoval,
        current: w.reportedAssetId ? `asset:${w.reportedAssetId}` : "",
        desired:
          w.desiredAssetId && w.desiredAssetId !== w.reportedAssetId
            ? `asset:${w.desiredAssetId}`
            : undefined,
      }));
    }),
  }));
  return { devices: registeredDevices(mappedDevices) };
}
