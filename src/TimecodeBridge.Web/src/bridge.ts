/** NativeWebView transport boundary: receives Host envelopes and owns request correlation/resync. */
import { acceptResult,send } from "./commands";import { parseHostMessage } from "./protocol";import { appStore,applyHostMessage } from "./store";
import { installFakeHost, type FakeHostHandle } from "./testing/fake-host";
declare global {interface Window{tcb:{receive:(json:string|unknown)=>void;hostAttached:()=>void}}}
let pending=false,dirty=false;
function schedule():void{dirty=true;if(pending)return;pending=true;requestAnimationFrame(()=>{pending=false;if(dirty){dirty=false;appStore.notify()}})}
const ready=():void=>send({protocolVersion:1,type:"ready",clientVersion:"3.0.0",capabilities:["clock-latest-only","wave-minmax","m1","m2a","m2b","m3"]});
export interface BridgeHandle { dispose:()=>void }
export function installBridge(fakeHostDelayMs=800):BridgeHandle{let attached=false,fakeHost:FakeHostHandle|null=null;const fallbackTimer=window.setTimeout(()=>{if(attached||window.invokeCSharpAction)return;fakeHost=installFakeHost();ready()},fakeHostDelayMs);window.tcb={receive:(json)=>{const message=parseHostMessage(json);if(!message)return;if(message.type==="result"){acceptResult(message);return}const result=applyHostMessage(appStore.value,message);if(result==="resync")send({protocolVersion:1,type:"resync",currentRevision:appStore.value.host.revision,reason:"patch baseRevision mismatch"});schedule()},hostAttached:()=>{attached=true;window.clearTimeout(fallbackTimer);fakeHost?.dispose();fakeHost=null;ready()}};ready();return{dispose:()=>{attached=true;window.clearTimeout(fallbackTimer);fakeHost?.dispose();fakeHost=null}}}
