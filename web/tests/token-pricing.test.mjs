import test from "node:test";
import assert from "node:assert/strict";
import { tokenPricing } from "../src/token-pricing.ts";

test("finalized first use and revision token costs are displayed", () => {
  assert.deepEqual(tokenPricing.map(({ firstUse, revision }) => [firstUse, revision]),
    [[2, 1], [4, 2], [6, 3], [3, 1], [7, 3]]);
});
