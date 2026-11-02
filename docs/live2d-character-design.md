# Live 2D showcase character design

## Current design (v2)

Regenerated on 2026-11-02 with the built-in ImageGen tool, without reference images or existing character designs.

Asset: [live2d-character-v2.png](../web/public/images/live2d-character-v2.png)

The revised original adult female VTuber uses simplified anime facial features, broad hair shapes, flat cel shading and restrained highlights. The front-facing waist-up framing and simple outfit are used in the public Live 2D motion demonstration and phone wallpaper mockup. Original image dimensions: 1024 × 1536 pixels. Alpha transparency verified.

## See-through showcase parts

The public character demonstration renders 12 real RGBA parts produced by the existing `upscalelab-see-through` RunPod endpoint. The source PNG was composited onto white as inference input without altering the original asset. The completed output has a common 1280 × 1280 canvas. Original model output PNGs and their original layer order are preserved under `web/public/images/live2d-character-parts/` and `web/src/showcase-character-data.json` with file hashes and semantic source names.

RunPod job `bce5cf67-5758-4102-84e8-e4036400474e-u2` completed with 12 layers and execution time 333581 ms. Output ZIP SHA-256: `a8da7286fe61ab485492253c6c1a31b4ee2cc98ce71b2beca00006e18498d360`. The endpoint image/settings and production application were not modified.

The existing web character rig rotates the face and facial features, moves the irides independently, and gives front/back hair different delayed motion. Eye whites and eyelashes remain attached to the head. The body, neck and clothing stay fixed. The full composition is always visible, with no pause, gyro or part-selection controls. Desktop mouse input follows the page; touch-first devices default to calibrated gyroscope input. Browsers requiring sensor permission request it from the first page gesture, shared by both previews. Keyboard input is also available. This is the same PNG-part approach as the editor, rather than a single foreground translation or a Cubism mesh. Opening the demo needs no GPU request or account. The phone mockup retains the original static PNG.

The model's topwear contains an opaque inner-collar oval that previously covered the neck. The presentation now draws the existing neck part above that oval, clipped to the curved collar opening so the front collar remains intact. This is a responsive SVG composition correction in the public demonstration, without repainting the model PNGs or changing user projects' layer order.

### Generation prompt (v2)

```text
Use case: stylized-concept
Asset type: original 2D female VTuber avatar illustration for a website Live 2D feature showcase.
Primary request: Generate one original female VTuber character in a clearly hand-drawn-looking, flat Japanese anime illustration style. Make the rendering much less realistic, matte and restrained, with very little gloss. Do not use reference images, existing characters, agency talent designs or copied costumes.
Subject: an adult woman in her twenties, friendly small closed-mouth smile, large expressive anime eyes, a tiny simplified nose indicated with a short stroke, a simple mouth line with no rendered lipstick or lip volume. Medium-length chestnut-brown hair with a simple soft bob silhouette and readable bangs. Keep the hair and outfit simple with no hair accessories or jewelry. Plain cream high-neck blouse and a simple charcoal open cardigan, modest relaxed clothing with natural unexaggerated proportions.
Art direction: a professional 2D streaming avatar illustration drawn with confident economical lines and a few deliberately designed flat color shapes. Dark warm-brown linework with subtle line-weight variation. Two-step cel shading: a flat base color and a single restrained shadow color per material. Flat matte skin, almost no facial gradients, no realistic nose bridge or facial sculpting. Hair formed from broad clean graphic locks with one understated darker shadow group; no glossy shine bands or streaks. Eyes have a clearly drawn iris and pupil with one tiny restrained catchlight, without glassy gem-like shine. Clothing has just a few intentional fold lines with flat angular cel shadows, no fabric texture. Clean, charming, readable, graphic, understated. Prioritize drawn shape design over rendering detail.
Composition: ONE centered character, direct front view, waist-up only, entire head and hair visible with some clear margin above and on both sides, shoulders level, arms relaxed down at her sides, both shoulders and upper arms inside the frame. Calm neutral pose suitable for subtle mouse/gyroscope motion in a website demo. Portrait orientation.
Background: real transparent alpha background; clean character cutout only. No backdrop, no floor, no cast shadow, no checkerboard artwork.
Constraints: a fully original design; no text, no watermark, no logos, no props, no decorative elements, no multiple views, no reference sheet, no full body.
Avoid: semi-realistic portraiture, photorealism, 3D rendering, painterly rendering, airbrushed gradients, volumetric lighting, bloom, glossy skin, shiny noses, wet lips, shiny hair, multiple sparkle highlights, intricate irises, luminous eyes, excessive blush, detailed skin textures, tiny repetitive hair strands, excessive clothing wrinkles, heavy decoration, elaborate costumes, animal ears, hats, headsets, microphones, cleavage, exaggerated anatomy.
```
