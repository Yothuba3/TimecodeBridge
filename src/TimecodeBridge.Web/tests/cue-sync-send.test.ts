import assert from "node:assert/strict";
import test from "node:test";
import {acceptResult} from "../src/commands";
import {cueSyncTargetCount,sendCueSync} from "../src/components/app";
import {fakeState} from "../src/testing/fake-host";

function stubBridge():string[]{const messages:string[]=[];Object.defineProperty(globalThis,"window",{value:{invokeCSharpAction:(json:string)=>messages.push(json)},writable:true,configurable:true});return messages}
const settle=()=>new Promise(resolve=>setTimeout(resolve,0));

test("cueSyncTargetCount counts only enabled hosts among the targets",()=>{const state=fakeState();assert.equal(cueSyncTargetCount(state),2);state.hosts[0]!.enabled=false;assert.equal(cueSyncTargetCount(state),1);state.cueSync.targetHostIds=["missing"];assert.equal(cueSyncTargetCount(state),0)});

test("sendCueSync notices instead of sending when no enabled target host exists",()=>{const messages=stubBridge(),notices:string[]=[];const state=fakeState();state.cueSync.targetHostIds=[];let flashed=0;sendCueSync(state,text=>notices.push(text),()=>flashed++);assert.equal(messages.length,0);assert.equal(flashed,0);assert.deepEqual(notices,["Cue-Sync の送信先ホストを選んでください"])});

test("sendCueSync sends cueSync.send and flashes once the host answers ok",async()=>{const messages=stubBridge(),notices:string[]=[];let flashed=0;sendCueSync(fakeState(),text=>notices.push(text),()=>flashed++);assert.equal(messages.length,1);const message=JSON.parse(messages[0]!);assert.equal(message.command,"cueSync.send");assert.equal(flashed,0);acceptResult({protocolVersion:1,type:"result",requestId:message.requestId,ok:true,data:{sent:true}});await settle();assert.equal(flashed,1);assert.deepEqual(notices,[])});

test("sendCueSync shows the host error instead of flashing",async()=>{const messages=stubBridge(),notices:string[]=[];let flashed=0;sendCueSync(fakeState(),text=>notices.push(text),()=>flashed++);const message=JSON.parse(messages[0]!);acceptResult({protocolVersion:1,type:"result",requestId:message.requestId,ok:false,error:{code:"oscError",message:"送信に失敗しました",retryable:true}});await settle();assert.equal(flashed,0);assert.deepEqual(notices,["送信に失敗しました"])});
