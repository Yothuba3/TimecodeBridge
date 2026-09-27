import assert from "node:assert/strict";
import test from "node:test";
import {hostRemovalMessage,validateHostDraft} from "../src/components/host-panel";
import {newestLogsFirst} from "../src/components/log-panel";

test("host draft validation identifies each invalid field",()=>{assert.deepEqual(validateHostDraft({name:"",ipAddress:"",port:"70000"}),{name:"名前は必須です",ipAddress:"IP アドレスは必須です",port:"1〜65535 で入力してください"});assert.deepEqual(validateHostDraft({name:"QLab",ipAddress:"192.168.1.2",port:"53000"}),{})});

test("logs are ordered newest first without mutating host state",()=>{const logs=[{id:"old",timestampUtc:"2026-01-01T00:00:00Z",message:"old",success:true},{id:"new",timestampUtc:"2026-01-02T00:00:00Z",message:"new",success:false}];assert.deepEqual(newestLogsFirst(logs).map(x=>x.id),["new","old"]);assert.deepEqual(logs.map(x=>x.id),["old","new"])});

test("host removal message names the cues and trigger buttons that still target the host",()=>{const host={id:"h1",name:"QLab"};assert.equal(hostRemovalMessage(host,[],[]),"「QLab」を削除しますか？");assert.equal(hostRemovalMessage(host,[{targetHostIds:["h1"]},{targetHostIds:["h2"]},{targetHostIds:["h1","h2"]}],[{targetHostIds:["h1"]}]),"「QLab」を削除しますか？ キュー 2 件とポン出し 1 件が送信先にしています。削除するとそれらは送信先を失います")});
