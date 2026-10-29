import { useState } from "react";
import { UserRound } from "lucide-react";
import "./crop.css";

export function CreatorAvatar({ name, source }: { name?: string; source?: string }) {
  const [failed, setFailed] = useState<string>();
  return <span className="creator-avatar">{source && failed !== source
    ? <img src={source} alt={`${name || "작성자"} 프로필 사진`} loading="lazy" onError={() => setFailed(source)} />
    : <UserRound size={16} aria-hidden="true" />}</span>;
}
