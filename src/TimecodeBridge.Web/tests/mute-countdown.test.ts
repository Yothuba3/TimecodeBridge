import assert from "node:assert/strict";import test from "node:test";import {muteCountdown} from "../src/mute-countdown";

const now=Date.parse("2026-10-22T10:00:00.000Z");
const at=(ms:number)=>new Date(now+ms).toISOString();

test("counts down in whole seconds",()=>{
  assert.equal(muteCountdown(at(9000),now),"9");
  assert.equal(muteCountdown(at(1000),now),"1");
});

test("rounds up so the last second is not shown as finished",()=>{
  // 残り 0.4 秒を「0」と出すと、まだ解除されていないのに終わったように見える
  assert.equal(muteCountdown(at(400),now),"1");
  assert.equal(muteCountdown(at(1600),now),"2");
});

test("switches to minutes and seconds past a minute",()=>{
  assert.equal(muteCountdown(at(60000),now),"1:00");
  assert.equal(muteCountdown(at(95000),now),"1:35");
  assert.equal(muteCountdown(at(59000),now),"59");
});

test("shows nothing once the time has passed or is unset",()=>{
  assert.equal(muteCountdown(at(0),now),"");
  assert.equal(muteCountdown(at(-500),now),"");
  assert.equal(muteCountdown(null,now),"");
  assert.equal(muteCountdown("おかしな値",now),"");
});
