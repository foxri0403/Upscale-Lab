/** Human-readable labels in the existing Korean Studio UI. */
const labels: Record<string, string> = {
  Background: "배경", BackPart: "뒤쪽 파츠", Body: "몸통", FrontPart: "앞쪽 파츠",
  Hair: "머리카락", BackHair: "뒷머리", FrontHair: "앞머리", Face: "얼굴",
  Eye: "눈", Eyebrow: "눈썹", Mouth: "입", Nose: "코", Clothes: "의상",
  Accessory: "장신구", Other: "기타 파츠",
};
export function layerLabel(layerType: string): string {
  return labels[layerType] || layerType;
}
