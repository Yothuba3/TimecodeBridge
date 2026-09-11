import assert from "node:assert/strict";
import test from "node:test";
import {validateHostDraft} from "../src/components/host-panel";
import {newestLogsFirst} from "../src/components/log-panel";

test("host draft validation identifies each invalid field",()=>{assert.deepEqual(validateHostDraft({name:"",ipAddress:"",port:"70000"}),{name:"名前は必須です",ipAddress:"IP アドレスは必須です",port:"1〜65535 で入力してください"});assert.deepEqual(validateHostDraft({name:"QLab",ipAddress:"192.168.1.2",port:"53000"}),{})});

test("logs are ordered newest first without mutating host state",()=>{const logs=[{id:"old",timestampUtc:"2026-01-01T00:00:00Z",message:"old",success:true},{id:"new",timestampUtc:"2026-01-02T00:00:00Z",message:"new",success:false}];assert.deepEqual(newestLogsFirst(logs).map(x=>x.id),["new","old"]);assert.deepEqual(logs.map(x=>x.id),["old","new"])});
