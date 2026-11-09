export const platformTags = ["pc", "mobile", "tablet"] as const;
export type PlatformTag = typeof platformTags[number];

export function selectPlatform(selected: readonly PlatformTag[], tag: PlatformTag, checked: boolean) {
  return platformTags.filter(value => value === tag ? checked : selected.includes(value));
}
