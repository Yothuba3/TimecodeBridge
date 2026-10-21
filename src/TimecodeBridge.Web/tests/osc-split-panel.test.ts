import assert from "node:assert/strict";import test from "node:test";import {readFileSync} from "node:fs";import {h} from "preact";import render from "preact-render-to-string";import {TriggerDrawer} from "../src/components/trigger-drawer";import {appStore} from "../src/store";import {fakeState} from "../src/testing/fake-host";

// OSC ポン出しは画面下のドロワーではなく、本体の右に並ぶ側パネル。
// 本体(最小 1100px)を押し潰すと中身がパネルの下に潜り込むので、幅の扱いが要点になる。
const css=readFileSync(new URL("../src/styles/m3.css",import.meta.url),"utf8");

test("the split keeps the main area at its minimum width",()=>{
  // 1100px を下回らせると、本体の中身がパネルの下に潜り込んで触れなくなる
  assert.match(css,/\.split\.osc-open\{grid-template-columns:minmax\(1100px,1fr\)/);
  // 閉じているときは本体が全幅を使う
  assert.match(css,/\.split\{[^}]*grid-template-columns:minmax\(0,1fr\) 0/);
});

test("narrow windows fall back to a bottom overlay instead of squeezing the split",()=>{
  // 本体 1100 + パネル 280 に満たない幅では左右に並べられない
  assert.match(css,/@media\(max-width:1380px\)/);
  assert.match(css,/\.split\.osc-open>\.osc-panel\{position:fixed/);
});

test("panel cells keep an even height instead of stretching down the tall panel",()=>{
  assert.match(css,/\.osc-panel \.grid\{align-content:start;grid-auto-rows:minmax\(54px,max-content\)\}/);
});

test("the run mode hides empty cells so only usable buttons are shown",()=>{
  assert.match(css,/\.osc-panel \.grid:not\(\.editing\) \.empty-cell\{visibility:hidden\}/);
});

test("the panel renders as a side panel with a width handle",()=>{
  appStore.value.host.state=fakeState();
  const html=render(h(TriggerDrawer,{notice:()=>{}}));
  assert.match(html,/class="osc-panel/,"ドロワーではなく側パネルとして出す");
  assert.match(html,/class="osc-resize"/,"幅を変えるつまみがある");
  assert.doesNotMatch(html,/class="drawer/,"旧ドロワーのクラスは残さない");
});
