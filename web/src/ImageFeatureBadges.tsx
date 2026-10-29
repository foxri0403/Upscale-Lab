import { Expand, Layers3, Scan } from "lucide-react";
import type { Picture } from "./model";
import { imageFeatures } from "./image-features";

export function ImageFeatureBadges({ picture }: { picture: Picture }) {
  const features = imageFeatures(picture);
  if (!features.length) return null;
  return (
    <div className="card-meta" aria-label="적용 기술">
      {features.map(feature => (
        <span key={feature} className={`type-badge ${feature === "Live 2D" ? "live" : ""}`}>
          {feature === "Live 2D" ? <Layers3 size={14} />
            : feature === "배경 분리" ? <Scan size={14} /> : <Expand size={14} />}
          {feature}
        </span>
      ))}
    </div>
  );
}
