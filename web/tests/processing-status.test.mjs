import test from "node:test";
import assert from "node:assert/strict";
import { processingStatus } from "../src/processing-status.ts";

test("remote queue and character inference are explained as stages", () => {
  assert.match(processingStatus({ status: "Processing", progress: 20, operation: "Layers" }), /GPU 할당 대기/);
  assert.match(processingStatus({ status: "Processing", progress: 45, operation: "CharacterLayers" }), /캐릭터 파츠 생성/);
  assert.match(processingStatus({ status: "Processing", progress: 45, operation: "Upscale" }), /45%/);
  assert.equal(processingStatus({ status: "Failed", progress: 20, errorMessage: "시간 초과" }), "시간 초과");
  assert.equal(processingStatus({ status: "Completed", progress: 100 }), "처리 완료");
});
