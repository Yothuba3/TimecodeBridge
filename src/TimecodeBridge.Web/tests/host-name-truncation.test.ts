import assert from "node:assert/strict";import test from "node:test";import {h} from "preact";import render from "preact-render-to-string";import {CueDialog} from "../src/components/dialogs";import {CueSync} from "../src/components/cue-sync";import {appStore} from "../src/store";import {fakeState} from "../src/testing/fake-host";

// ホスト名は本番で「現場名 + 機材名」のように長くなる。チェックボックスの label は flex なので、
// 名前を直接置くと text-overflow が効かず、枠を越えて隣の表示に重なる。span で包むことで省略が効く。
const longHost={id:"long",name:"非常に長いホスト名のテスト用エントリです現場名と機材名を併記した場合",ipAddress:"192.0.2.9",port:9000,enabled:true,reachability:"unknown" as const};
const labelsOf=(html:string)=>html.match(/<label[^>]*>.*?<\/label>/gs)??[];
const hostLabels=(html:string)=>labelsOf(html).filter(x=>x.includes(longHost.name));

test("cue dialog wraps each host name so a long name can be truncated",()=>{
  const html=render(h(CueDialog,{frameRate:"24",hosts:[...fakeState().hosts,longHost],close:()=>{},done:()=>{}}));
  const found=hostLabels(html);
  assert.equal(found.length,1,"long host name should appear once in the picker");
  assert.match(found[0]!,new RegExp(`<span>${longHost.name}</span>`),"host name must sit in its own span, not bare in the flex label");
});

test("cue-sync host list wraps each host name so a long name can be truncated",()=>{
  const state=fakeState();
  appStore.value={...appStore.value,host:{...appStore.value.host,state:{...state,hosts:[...state.hosts,longHost]}}};
  const html=render(h(CueSync,{notice:()=>{}}));
  const found=hostLabels(html);
  assert.equal(found.length,1);
  assert.match(found[0]!,new RegExp(`<span>${longHost.name}</span>`));
  // 件数表示は一覧の隣に置かれるので、名前がはみ出すとここに重なる
  assert.match(html,/有効/);
});
