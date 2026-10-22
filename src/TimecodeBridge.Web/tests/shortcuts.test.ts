import assert from "node:assert/strict";import test from "node:test";import {shortcutFor} from "../src/shortcuts";
const event=(key:string,extra:Record<string,unknown>={})=>({key,repeat:false,isComposing:false,target:null,...extra}) as KeyboardEvent;
test("moves the selection with the arrow keys",()=>{assert.equal(shortcutFor(event("ArrowDown"),false),"selectNext");assert.equal(shortcutFor(event("ArrowUp"),false),"selectPrevious")});

// 本番中の誤爆を避けるため、修飾キーなしの単独キーで外に出る操作はしない
test("the bare space, L and P keys do nothing",()=>{for(const key of [" ","l","L","p","P"])assert.equal(shortcutFor(event(key),false),null)});

test("suppresses composition repeat and modal",()=>{assert.equal(shortcutFor(event("ArrowDown",{isComposing:true}),false),null);assert.equal(shortcutFor(event("ArrowUp",{repeat:true}),false),null);assert.equal(shortcutFor(event("Escape"),true),null)});
test("maps project shortcuts and suppresses them in modal",()=>{assert.equal(shortcutFor(event("s",{metaKey:true}),false),"projectSave");assert.equal(shortcutFor(event("S",{ctrlKey:true,shiftKey:true}),false),"projectSaveAs");assert.equal(shortcutFor(event("z",{metaKey:true}),true),null)});
test("suppresses editable targets",()=>{class Element{isContentEditable=false;tagName="INPUT"};Object.assign(globalThis,{HTMLElement:Element});assert.equal(shortcutFor(event("ArrowDown",{target:new Element()}),false),null)});

test("Ctrl+Y redoes on Windows, Cmd+Y does nothing",()=>{assert.equal(shortcutFor(event("y",{ctrlKey:true}),false),"redo");assert.equal(shortcutFor(event("y",{metaKey:true}),false),null);assert.equal(shortcutFor(event("y",{ctrlKey:true}),true),null)});
