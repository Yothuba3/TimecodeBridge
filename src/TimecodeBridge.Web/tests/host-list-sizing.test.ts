import assert from "node:assert/strict";import test from "node:test";import {readFileSync} from "node:fs";

const css=readFileSync(new URL("../src/styles/m3.css",import.meta.url),"utf8");
const hostList=css.match(/\.host-list\{display:grid[^}]*\}/)?.[0]??"";

test("host rows keep their own height instead of filling the panel",()=>{
  // 既定の grid は行を引き伸ばすので、ホストが 1 件だと縦いっぱいの行になる
  assert.match(hostList,/grid-auto-rows:min-content/);
  assert.match(hostList,/align-content:start/);
});

test("the panel still scrolls once there are more hosts than fit",()=>{
  assert.match(css,/\.host-list\{min-height:0;overflow:auto\}/);
});
