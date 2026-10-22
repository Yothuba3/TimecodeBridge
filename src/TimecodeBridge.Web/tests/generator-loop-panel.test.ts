import assert from "node:assert/strict";import test from "node:test";import {h} from "preact";import render from "preact-render-to-string";import {GeneratorPanel} from "../src/components/generator-panel";import {appStore} from "../src/store";import {fakeState} from "../src/testing/fake-host";

// 内部生成の範囲ループ。終了 TC の欄はループを入れたときだけ出す。
const panel=(loopEndTime:string|null)=>{
  const state=fakeState();
  appStore.value.host.state={...state,generator:{...state.generator,loopEndTime}};
  return render(h(GeneratorPanel,{notice:()=>{}}));
};

test("the end field is hidden until the loop is turned on",()=>{
  const html=panel(null);
  assert.match(html,/ループ/);
  assert.doesNotMatch(html,/終了 TC/);
});

test("turning the loop on reveals the end field with the stored value",()=>{
  const html=panel("08:00:30:00");
  assert.match(html,/終了 TC/);
  assert.match(html,/value="08"/);
  assert.match(html,/value="30"/);
});
