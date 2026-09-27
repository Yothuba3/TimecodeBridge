import type { CommandName, HostMessage, ProtocolError, WebMessage } from "./protocol";
declare global { interface Window { invokeCSharpAction?: (json:string)=>void } }
let counter=0;
const pending=new Map<string,{resolve:(v:unknown)=>void;reject:(e:ProtocolError)=>void}>();
export function send(message:WebMessage):void{window.invokeCSharpAction?.(JSON.stringify(message))}
export function command(name:CommandName,args:unknown,expectedRevision?:number):Promise<unknown>{const requestId=`web-${Date.now()}-${++counter}`;const message:WebMessage={protocolVersion:1,type:"command",requestId,command:name,args,...(expectedRevision===undefined?{}:{expectedRevision})};send(message);return new Promise((resolve,reject)=>pending.set(requestId,{resolve,reject}))}
export function acceptResult(message:Extract<HostMessage,{type:"result"}>):void{const request=pending.get(message.requestId);if(!request)return;pending.delete(message.requestId);if(message.ok)request.resolve(message.data);else request.reject(message.error)}
