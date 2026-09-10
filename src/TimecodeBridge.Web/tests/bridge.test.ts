import assert from "node:assert/strict";
import test from "node:test";
import { installBridge } from "../src/bridge";

const wait=(ms:number)=>new Promise(resolve=>setTimeout(resolve,ms));
function mockWindow():Window{
  const value={setTimeout,clearTimeout,setInterval,clearInterval} as unknown as Window;
  Object.defineProperty(globalThis,"window",{value,writable:true,configurable:true});
  Object.defineProperty(globalThis,"requestAnimationFrame",{value:(callback:FrameRequestCallback)=>setTimeout(()=>callback(performance.now()),0),writable:true,configurable:true});
  return value;
}

test("hostAttached sends ready to the real host and is idempotent",()=>{const target=mockWindow();const bridge=installBridge(1000);const messages:string[]=[];target.invokeCSharpAction=json=>messages.push(json);target.tcb.hostAttached();target.tcb.hostAttached();assert.deepEqual(messages.map(json=>JSON.parse(json).type),["ready","ready"]);bridge.dispose()});

test("fake host is installed only after the fallback delay",async()=>{const target=mockWindow();const bridge=installBridge(20);assert.equal(target.invokeCSharpAction,undefined);await wait(10);assert.equal(target.invokeCSharpAction,undefined);await wait(20);assert.equal(typeof target.invokeCSharpAction,"function");bridge.dispose();assert.equal(target.invokeCSharpAction,undefined)});
