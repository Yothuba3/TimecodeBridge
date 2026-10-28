import assert from "node:assert/strict";import test from "node:test";import {defaultLoopEnd,loopRangeValid} from "../src/generator-loop";

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

test("a loop range holds only when the end comes after the start",()=>{
  assert.equal(loopRangeValid("01:00:00:00","01:03:10:00"),true);
  assert.equal(loopRangeValid("03:45:11:20","01:03:10:00"),false);
  assert.equal(loopRangeValid("01:03:10:00","01:03:10:00"),false);
  assert.equal(loopRangeValid("01:03:09:29","01:03:10:00"),true);
  assert.equal(loopRangeValid("01:00:00;10","01:00:00;09"),false);
});

test("without a loop any start is fine, and unreadable values are left to the host",()=>{
  assert.equal(loopRangeValid("23:59:59:29",null),true);
  assert.equal(loopRangeValid("","01:00:00:00"),true);
});
