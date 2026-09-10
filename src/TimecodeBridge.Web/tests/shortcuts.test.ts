import assert from "node:assert/strict";import test from "node:test";import {shortcutFor} from "../src/shortcuts";
const event=(key:string,extra:Record<string,unknown>={})=>({key,repeat:false,isComposing:false,target:null,...extra}) as KeyboardEvent;
test("maps milestone shortcuts",()=>{assert.equal(shortcutFor(event(" "),false),"cueSync");assert.equal(shortcutFor(event("L"),false),"toggleMute");assert.equal(shortcutFor(event("p"),false),"toggleDrawer");assert.equal(shortcutFor(event("ArrowDown"),false),"selectNext")});
test("suppresses composition repeat and modal",()=>{assert.equal(shortcutFor(event(" ",{isComposing:true}),false),null);assert.equal(shortcutFor(event("l",{repeat:true}),false),null);assert.equal(shortcutFor(event("p"),true),null)});
test("suppresses editable targets",()=>{class Element{isContentEditable=false;tagName="INPUT"};Object.assign(globalThis,{HTMLElement:Element});assert.equal(shortcutFor(event(" ",{target:new Element()}),false),null)});
