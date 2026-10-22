import assert from "node:assert/strict";import test from "node:test";import {defaultLoopEnd} from "../src/generator-loop";

test("places the end ten seconds after the start",()=>{
  assert.equal(defaultLoopEnd("08:00:00:00","30"),"08:00:10:00");
  assert.equal(defaultLoopEnd("08:00:00:15","30"),"08:00:10:15");
});

test("uses the frame rate when carrying into the next second",()=>{
  assert.equal(defaultLoopEnd("00:00:00:23","24"),"00:00:10:23");
  assert.equal(defaultLoopEnd("00:00:00:24","25"),"00:00:10:24");
});

test("does not wrap past the end of the day",()=>{
  assert.equal(defaultLoopEnd("23:59:55:00","30"),"23:59:59:29");
});

test("leaves an unparseable start alone",()=>{
  assert.equal(defaultLoopEnd("","30"),"");
  assert.equal(defaultLoopEnd("おかしな値","30"),"おかしな値");
});
