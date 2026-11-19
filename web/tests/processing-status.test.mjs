import test from "node:test";
import assert from "node:assert/strict";
import { processingStatus, processingPercent, processingStage } from "../src/processing-status.ts";

test("remote queue and character inference are explained as stages", () => {
  assert.match(processingStatus({ status: "Processing", progress: 20, operation: "Layers" }), /GPU 할당 대기/);
  assert.match(processingStatus({ status: "Processing", progress: 45, operation: "CharacterLayers" }), /캐릭터 파츠 생성/);
  assert.match(processingStatus({ status: "Processing", progress: 45, operation: "Upscale" }), /45%/);
  assert.equal(processingStatus({ status: "Failed", progress: 20, errorMessage: "시간 초과" }), "시간 초과");
  assert.equal(processingStatus({ status: "Completed", progress: 100 }), "100% · 처리 완료");
  assert.match(processingStatus({ status: "Cancelled", progress: 45 }), /작업 취소됨.*반환되지/);
});

test("retaining the background reports both phases of the same Live 2D job", () => {
  const job = { operation: "CharacterLayers", status: "Processing", keepBackground: true };
  assert.equal(processingStatus({ ...job, progress: 28 }), "28% · AI 배경 분리 중");
  assert.equal(processingStatus({ ...job, progress: 56 }), "56% · 피사체 Live 2D 파츠 생성 중");
  assert.equal(processingStatus({ ...job, progress: 80 }), "80% · Live 2D 결과 저장 중");
});


test("Upscale, Layers and CharacterLayers all show percent plus current stage", () => {
  for (const operation of ["Upscale", "Layers", "CharacterLayers"]) {
    for (const progress of [5, 15, 20, 45, 70, 80]) {
      const job = { status: "Processing", progress, operation };
      assert.match(processingStatus(job), new RegExp(`^${progress}% · `));
      assert.ok(processingStage(job).length > 0);
    }
  }
  assert.equal(processingPercent({ status: "Queued", progress: 20 }), 0);
  assert.equal(processingPercent({ status: "Processing", progress: 150 }), 99);
  assert.equal(processingPercent({ status: "Processing", progress: -20 }), 0);
  assert.equal(processingPercent({ status: "Processing", progress: NaN }), 0);
  assert.equal(processingPercent({ status: "Completed", progress: 75 }), 100);
  assert.equal(processingStage(null, true), "진행 중인 작업 확인 중");
});
