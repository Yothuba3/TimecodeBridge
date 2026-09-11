import assert from "node:assert/strict";
import test from "node:test";
import {clampTimecodePart,enterTimecodeDigits,joinTimecode,normalizeTimecodeDigits,splitTimecode,timecodeFocusForKey} from "../src/components/timecode-input";

test("normalizes full-width digits and discards every other character",()=>{
  assert.equal(normalizeTimecodeDigits("１２a３：四4"),"1234");
});

test("splits and composes ordinary and signed timecodes",()=>{
  assert.deepEqual(splitTimecode("-01:02:03:04",true),{sign:"-",parts:["01","02","03","04"]});
  assert.equal(joinTimecode(["1","02","3","04"]),"01:02:03:04");
  assert.equal(joinTimecode(["01","02","03","04"],"-",true),"-01:02:03:04");
  assert.equal(joinTimecode(["","","",""]),"");
});

test("overflow and pasted digits continue into following fields",()=>{
  assert.deepEqual(enterTimecodeDigits(["","","",""],0,"１２３４５６７８９"),{parts:["12","34","56","78"],focus:3});
  assert.deepEqual(enterTimecodeDigits(["01","","",""],1,"23"),{parts:["01","23","",""],focus:2});
});

test("focus navigation follows arrows and empty Backspace",()=>{
  assert.equal(timecodeFocusForKey("ArrowRight",1,false),2);
  assert.equal(timecodeFocusForKey("ArrowLeft",1,false),0);
  assert.equal(timecodeFocusForKey("Backspace",2,true),1);
  assert.equal(timecodeFocusForKey("Backspace",2,false),null);
});

test("field bounds include the frame-rate-specific FF maximum",()=>{
  assert.equal(clampTimecodePart("99",0,"30"),"23");
  assert.equal(clampTimecodePart("80",1,"30"),"59");
  assert.equal(clampTimecodePart("29",3,"25"),"24");
  assert.equal(clampTimecodePart("29",3,"29.97df"),"29");
});
