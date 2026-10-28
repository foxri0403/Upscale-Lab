import type { CloudDevice, CloudState, CloudCommand } from "./api";
import type { Device, Command } from "./model";
import { registeredDevices } from "./device-layout.ts";
export function mapDeviceLibrary(states: CloudState[], commands: CloudCommand[][], assets: { id: string; title: string }[], registrations: CloudDevice[] = []) {
  const created = new Map(registrations.map(device => [device.id, device.createdAt]));
  const mappedDevices: Device[] = states.map((s) => ({
    id: s.id,
    created: created.get(s.id),
    name: s.name,
    platform: s.platform,
    osVersion: s.osVersion,
    manufacturer: s.manufacturer,
    model: s.model,
    architecture: s.architecture,
    physicalDisplayCount: s.displays.filter(d => d.isConnected).length,
    online: s.recentlySeen,
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
        current: w.reportedAssetId ? `asset:${w.reportedAssetId}` : "",
        desired:
          w.desiredAssetId && w.desiredAssetId !== w.reportedAssetId
            ? `asset:${w.desiredAssetId}`
            : undefined,
      }));
    }),
  }));
  const mappedCommands: Command[] = commands.flatMap((list, i) =>
    list.map(
      (c) =>
        ({
          id: c.id,
          device: states[i].name,
          display:
            states[i].platform === "Android"
              ? c.target === "Lock"
                ? "잠금 화면"
                : "홈 화면"
              : states[i].displays.find((d) => d.id === c.displayId)?.name ||
                "화면",
          picture: assets.find((a) => a.id === c.assetId)?.title || "배경화면",
          status:
            c.status === "Applied"
              ? "완료"
              : ["Failed", "Expired", "Superseded"].includes(c.status)
                ? "실패"
                : "대기",
          created: "",
          detail: c.error || c.status,
        }) as Command,
    ),
  );
  return { devices: registeredDevices(mappedDevices), commands: mappedCommands };
}
