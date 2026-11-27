import assert from "node:assert/strict";
import test from "node:test";
import { collectExplorePages, explorePage } from "../src/explore-pagination.ts";

test("탐색 결과는 실제 열 수에 맞춰 최대 세 줄로 자른다", () => {
  for (const columns of [1, 2, 3, 4]) {
    const size = columns * 3;
    assert.deepEqual(explorePage(size * 2 + 1, columns, size), {
      page: 2, pageCount: 3, pageSize: size, start: size, numbers: [1, 2, 3],
    });
  }
});

test("번호는 현재 페이지가 포함된 실제 범위에서 최대 열 개를 보여 준다", () => {
  assert.deepEqual(explorePage(241, 4, 0).numbers, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  assert.deepEqual(explorePage(241, 4, 108).numbers, [6, 7, 8, 9, 10, 11, 12, 13, 14, 15]);
  assert.deepEqual(explorePage(241, 4, 240).numbers, [12, 13, 14, 15, 16, 17, 18, 19, 20, 21]);
});

test("삭제 후 범위를 벗어난 페이지를 보정하고 빈 결과에는 번호를 만들지 않는다", () => {
  assert.deepEqual(explorePage(13, 4, 50), { page: 2, pageCount: 2, pageSize: 12, start: 12, numbers: [1, 2] });
  assert.deepEqual(explorePage(0, 4, 25), { page: 1, pageCount: 0, pageSize: 12, start: 0, numbers: [] });
});

test("공유 결과의 100건 이후 배치를 가져오고 실제 마지막 배치에서 멈춘다", async () => {
  const source = Array.from({ length: 207 }, (_, id) => ({ id: String(id) }));
  const offsets = [];
  const results = await collectExplorePages(async offset => {
    offsets.push(offset);
    return source.slice(offset, offset + 100);
  });
  assert.deepEqual(results, source);
  assert.deepEqual(offsets, [0, 100, 200]);
});

test("이전 API가 첫 배치를 다시 반환하면 중복 없이 종료한다", async () => {
  const source = Array.from({ length: 100 }, (_, id) => ({ id: String(id) }));
  const offsets = [];
  assert.deepEqual(await collectExplorePages(async offset => {
    offsets.push(offset);
    return source;
  }), source);
  assert.deepEqual(offsets, [0, 100]);
});
