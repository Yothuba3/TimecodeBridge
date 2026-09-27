import assert from "node:assert/strict";import test from "node:test";import {firedSince} from "../src/cue-flash";

const cue=(id:string,flashToken:number)=>({id,runtime:{flashToken}});

test("a cue flashes only when its token advances after it was first seen",()=>{const seen=new Map<string,number>();assert.deepEqual(firedSince(seen,[cue("a",3),cue("b",0)]),[]);assert.deepEqual(firedSince(seen,[cue("a",3),cue("b",0)]),[]);assert.deepEqual(firedSince(seen,[cue("a",4),cue("b",1)]),["a","b"]);assert.deepEqual(firedSince(seen,[cue("a",4)]),[]);assert.deepEqual(firedSince(seen,[cue("a",4),cue("c",0)]),[]);assert.deepEqual(firedSince(seen,[cue("c",2)]),["c"])});
