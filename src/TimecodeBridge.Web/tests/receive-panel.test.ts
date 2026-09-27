import assert from "node:assert/strict";
import test from "node:test";
import {h} from "preact";
import render from "preact-render-to-string";
import {acceptResult} from "../src/commands";
import {RescanButton,rescanDevices,selectReceiveDevice} from "../src/components/receive-panel";

function captureMessages():string[]{const messages:string[]=[];Object.defineProperty(globalThis,"window",{value:{invokeCSharpAction:(json:string)=>messages.push(json)},writable:true,configurable:true});return messages}

test("selecting the empty receive device sends receive.selectDevice with null",()=>{const messages=captureMessages();selectReceiveDevice("");assert.equal(messages.length,1);const message=JSON.parse(messages[0]!);assert.equal(message.command,"receive.selectDevice");assert.deepEqual(message.args,{deviceId:null})});

test("rescan shows scanning until the host returns the result",async()=>{const messages=captureMessages();const states:boolean[]=[];const done=rescanDevices(v=>states.push(v));assert.deepEqual(states,[true]);const message=JSON.parse(messages[0]!);assert.equal(message.command,"audio.refreshDevices");assert.deepEqual(message.args,{direction:"capture"});acceptResult({protocolVersion:1,type:"result",requestId:message.requestId,ok:true});await done;assert.deepEqual(states,[true,false])});

test("rescan stops showing scanning when the host reports an error",async()=>{const messages=captureMessages();const states:boolean[]=[];const done=rescanDevices(v=>states.push(v));const message=JSON.parse(messages[0]!);acceptResult({protocolVersion:1,type:"result",requestId:message.requestId,ok:false,error:{code:"audioError",message:"x",retryable:true}});await done;assert.deepEqual(states,[true,false])});

test("rescan button is disabled with an indeterminate progress bar while scanning",()=>{const idle=render(h(RescanButton,{scanning:false,onClick:()=>{}}));assert.match(idle,/再スキャン/);assert.doesNotMatch(idle,/progressbar/);assert.doesNotMatch(idle,/disabled/);const busy=render(h(RescanButton,{scanning:true,onClick:()=>{}}));assert.match(busy,/スキャン中…/);assert.match(busy,/disabled/);assert.match(busy,/role="progressbar"/)});
