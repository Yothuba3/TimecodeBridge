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

// Host は null の項目を JSON から省く(WhenWritingNull)。ループ無しの loopEndTime は null でなくキーごと無い
test("a generator state without loopEndTime renders with the loop off",()=>{
  const state=fakeState();
  const {loopEndTime:_omitted,...generator}=state.generator;
  appStore.value.host.state=JSON.parse(JSON.stringify({...state,generator}));
  const html=render(h(GeneratorPanel,{notice:()=>{}}));
  assert.match(html,/ループ/);
  assert.doesNotMatch(html,/終了 TC/);
});

test("turning the loop on reveals the end field with the stored value",()=>{
  const html=panel("08:00:30:00");
  assert.match(html,/終了 TC/);
  assert.match(html,/value="08"/);
  assert.match(html,/value="30"/);
  assert.doesNotMatch(html,/反映されません/);
});

// 開始 TC が終了 TC 以降の組み合わせは Host に送らない。画面で反映していないことを伝える
test("an end at or before the start is flagged as not applied",()=>{
  const state=fakeState();
  appStore.value.host.state={...state,generator:{...state.generator,startTime:"03:45:11:20",loopEndTime:"01:03:10:00"}};
  const html=render(h(GeneratorPanel,{notice:()=>{}}));
  assert.match(html,/終了 TC を開始 TC より後にしてください/);
});
