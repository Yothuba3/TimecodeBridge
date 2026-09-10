/** Contract smoke tests; shared C#/TS JSON fixtures will be added with the Host protocol. */
import assert from "node:assert/strict";
import test from "node:test";
import { PROTOCOL_VERSION } from "../src/protocol";

test("protocol version remains pinned", () => { assert.equal(PROTOCOL_VERSION, 1); });
