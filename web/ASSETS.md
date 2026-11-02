# 웹 샘플 이미지

사진은 `public/images/`에 포함되어 웹 실행 시 사진 서비스 요청이 필요하지 않습니다. 실제 사용자 이미지는 기존 API의 S3 저장 경로를 사용합니다.

## 생성 이미지

기본 제공 ImageGen 도구로 생성했습니다(CLI/API 키 방식 아님). 최종 프로젝트 파일:

- `public/images/fox.png`
- `public/images/fox-background.png`
- `public/images/fox-foreground.png` (알파 투명 배경)
- `public/images/live2d-character-v2.png` (1024 × 1536, 알파 투명 배경)

Live 2D 소개와 휴대폰 목업의 여성 버튜버 캐릭터는 별도의 참고 이미지 없이 생성한 최종 디자인이다. 선과 평면 셀 채색을 중심으로 광택을 줄였다. [캐릭터 생성 프롬프트](../docs/live2d-character-design.md)를 참고한다.

`public/images/live2d-character-parts/000.png`~`011.png`는 해당 캐릭터를 기존 See-through 전용 RunPod worker에서 실제 분리한 12개 RGBA 파츠다. 모두 1280 × 1280 캔버스를 유지하며 재생성·수동 잘라내기로 대체하지 않았다. 합성 순서·의미 분류·원본/파츠 SHA-256은 `src/showcase-character-data.json`, 처리 작업과 원본 생성 프롬프트는 위 디자인 문서에 기록했다. 소개 화면은 이 정적 파츠만 읽고 GPU 요청을 만들지 않는다.

여우 이미지의 최종 프롬프트 세트:

1. `Use case: photorealistic-natural. Create one website wallpaper asset, a landscape 3:2 wildlife photograph, a beautiful red fox standing full body on mossy woodland ground, head turned gently toward camera, lush emerald ferns, tree trunks and warm sunlit golden bokeh behind. Natural orange fur and white chest, elegant rich detailed realistic photograph. Fox positioned near center slightly right, whole fox including paws and tail in view. This is an image asset for an existing Upscale Lab UI, NOT a UI mockup. No typography, no graphics, no frames, no watermark. Match the natural red fox in a green forest photo in the approved UI mood.`
2. `Use case: background-extraction. Edit target: supplied red fox forest photograph. Extract ONLY the complete fox as a clean transparent-background foreground layer, retaining exactly the fox's original size, position, pose and detailed fur edges. Preserve the full 1536x1024 landscape canvas and alignment from original photo: no centering, no cropping or repositioning. Remove ALL forest and ground, with actual transparency. No shadow, no checkerboard printed pixels, no text. This foreground will overlay the original aligned background in a website parallax demo.`
3. `Use case: precise-object-edit. Edit target: supplied red fox forest photograph. Remove ONLY the entire fox and naturally inpaint the woodland behind it, giving a clean empty forest background plate for an aligned parallax demo. Preserve original 1536x1024 landscape canvas, composition, mossy ground, ferns, trees and golden bokeh precisely. No animals, no typography, no graphics. Do not crop, reframe or change lighting.`

이 레이어는 소개 화면·로컬 데모용으로 준비한 예시이며 실제 사용자 이미지 AI 처리 결과로 표시하지 않습니다.

## 사진 출처

출처는 Unsplash이며 원본 ID는 아래 링크에 기록했습니다. 포함된 사진은 초기 준비 시 `auto=format&fit=crop&w=1400&q=85` 옵션으로 내려받았습니다. 실행 시 외부 사진 서비스를 호출하지 않습니다.

| 파일 | 원본 |
|---|---|
| lake.jpg | https://images.unsplash.com/photo-1501785888041-af3ef285b470 |
| city.jpg | https://images.unsplash.com/photo-1519501025264-65ba15a82390 |
| stars.jpg | https://images.unsplash.com/photo-1462331940025-496dfbfc7564 |
| ocean.jpg | https://images.unsplash.com/photo-1507525428034-b723cf961d3e |
| forest.jpg | https://images.unsplash.com/photo-1448375240586-882707db888b |
| mountain.jpg | https://images.unsplash.com/photo-1454496522488-7a8e488e8606 |
| sunset.jpg | https://images.unsplash.com/photo-1472120435266-53107fd0c44a |
