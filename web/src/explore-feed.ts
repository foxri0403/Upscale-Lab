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
    creator: post.username,
    creatorAvatar: post.profileImageUrl || undefined,
    downloads: post.downloadCount,
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
