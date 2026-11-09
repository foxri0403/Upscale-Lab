import type { Genre, Picture } from "./model";

// Mobile gallery posts and immutable web wallpaper assets have separate IDs and APIs.
export type GalleryPost = {
  id: string;
  userId: string;
  username: string;
  profileImageUrl?: string | null;
  title: string;
  imageUrl: string;
  projectId?: string | null;
  isPublic: boolean;
  tag?: string;
  tags?: string[];
  platformTags?: Picture["platformTags"];
  likeCount?: number;
  isLikedByCurrentUser?: boolean;
  downloadCount: number;
  createdAt: string;
};

export function galleryPicture(post: GalleryPost): Picture {
  const tags = post.tags?.length
    ? post.tags
    : [post.tag || "장르 설정되지 않음"];
  return {
    id: `gallery:${post.id}`,
    galleryPostId: post.id,
    ownerId: post.userId,
    name: post.title,
    src: post.imageUrl,
    genre: tags[0] as Genre,
    tags,
    platformTags: post.platformTags || [],
    creator: post.username,
    creatorAvatar: post.profileImageUrl || undefined,
    downloads: post.downloadCount,
    likes: post.likeCount ?? 0,
    liked: post.isLikedByCurrentUser ?? false,
    created: post.createdAt,
    // A shared post's original project belongs to its author, not the viewer.
    // Do not copy its projectId/assetId into the current account's workspace.
  };
}

export function mergeExploreFeed(
  wallpapers: Picture[],
  gallery: GalleryPost[],
): Picture[] {
  const pictures = [
    ...wallpapers,
    ...gallery.filter((post) => post.isPublic).map(galleryPicture),
  ];
  return [...new Map(pictures.map((p) => [p.id, p])).values()];
}

export function sortExplore(pictures: Picture[], sort: string) {
  return [...pictures].sort((a, b) => {
    const rank = sort === "likes" ? (b.likes ?? 0) - (a.likes ?? 0)
      : sort === "downloads" ? (b.downloads ?? 0) - (a.downloads ?? 0)
      : sort === "name" ? a.name.localeCompare(b.name, "ko") : 0;
    return rank || b.created.localeCompare(a.created) || a.id.localeCompare(b.id);
  });
}

export function randomRecommendations(pictures: Picture[], seed: number, limit = 8) {
  // The order remains stable while the same page is open.
  const rank = (id: string) => {
    let value = seed | 0;
    for (const character of id) value = Math.imul(value ^ character.charCodeAt(0), 16777619);
    return value >>> 0;
  };
  return [...pictures].sort((a, b) => rank(a.id) - rank(b.id)).slice(0, limit);
}
