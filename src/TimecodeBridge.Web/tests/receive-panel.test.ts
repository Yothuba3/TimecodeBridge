import assert from "node:assert/strict";
import test from "node:test";
import {selectReceiveDevice} from "../src/components/receive-panel";

test("selecting the empty receive device sends receive.selectDevice with null",()=>{const messages:string[]=[];Object.defineProperty(globalThis,"window",{value:{invokeCSharpAction:(json:string)=>messages.push(json)},writable:true,configurable:true});selectReceiveDevice("");assert.equal(messages.length,1);const message=JSON.parse(messages[0]!);assert.equal(message.command,"receive.selectDevice");assert.deepEqual(message.args,{deviceId:null})});
