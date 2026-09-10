/** NativeWebView transport boundary: receives Host envelopes and owns request correlation/resync. */
import { acceptResult,send } from "./commands";import { parseHostMessage } from "./protocol";import { appStore,applyHostMessage } from "./store";
declare global {interface Window{tcb:{receive:(json:string|unknown)=>void}}}
let pending=false,dirty=false;
function schedule():void{dirty=true;if(pending)return;pending=true;requestAnimationFrame(()=>{pending=false;if(dirty){dirty=false;appStore.notify()}})}
export function installBridge():void{window.tcb={receive:(json)=>{const message=parseHostMessage(json);if(!message)return;if(message.type==="result"){acceptResult(message);return}const result=applyHostMessage(appStore.value,message);if(result==="resync")send({protocolVersion:1,type:"resync",currentRevision:appStore.value.host.revision,reason:"patch baseRevision mismatch"});schedule()}};send({protocolVersion:1,type:"ready",clientVersion:"3.0.0",capabilities:["clock-latest-only","wave-minmax","m1","m2a"]})}
