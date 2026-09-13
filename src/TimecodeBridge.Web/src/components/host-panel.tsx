import type {JSX} from "preact";
import {useEffect,useState} from "preact/hooks";
import {command} from "../commands";
import type {HostDto,ProtocolError} from "../protocol";
import {appStore} from "../store";
import {ConfirmDialog,ModalShell} from "./dialogs";

export interface HostDraft{name:string;ipAddress:string;port:string}
export function validateHostDraft(d:HostDraft):Record<string,string>{
 const errors:Record<string,string>={};
 if(!d.name.trim())errors.name="名前は必須です";
 if(!d.ipAddress.trim())errors.ipAddress="IP アドレスは必須です";
 const port=Number(d.port);if(!Number.isInteger(port)||port<1||port>65535)errors.port="1〜65535 で入力してください";
 return errors;
}
const hostErrors=(e:ProtocolError)=>Object.fromEntries(Object.entries(e.fieldErrors??{}).filter(([k])=>k.startsWith("host.")).map(([k,v])=>[k.slice(5),v]));

function HostDialog({host,close}:{host:HostDto|undefined;close:()=>void}){
 const[d,setD]=useState<HostDraft>({name:host?.name??"",ipAddress:host?.ipAddress??"",port:String(host?.port??53000)}),[errors,setErrors]=useState<Record<string,string>>({});
 useEffect(()=>appStore.updateUi({modalOpen:true}),[]);
 const field=(key:keyof HostDraft,label:string)=><label class="form-field"><span>{label}</span><input autofocus={key==="name"} class="field" value={d[key]} onInput={e=>{const value=e.currentTarget.value;setD(prev=>({...prev,[key]:value}))}}/><small class="field-error">{errors[key]??""}</small></label>;
 return <ModalShell title={host?"ホストを編集":"ホストを追加"} close={close}><form class="simple-form host-form" onSubmit={async e=>{e.preventDefault();const local=validateHostDraft(d);if(Object.keys(local).length){setErrors(local);return}try{const value={name:d.name.trim(),ipAddress:d.ipAddress.trim(),port:Number(d.port),enabled:host?.enabled??true};await command(host?"host.update":"host.add",host?{id:host.id,host:value}:{host:value});close()}catch(error){setErrors(hostErrors(error as ProtocolError))}}}>{field("name","名前")}{field("ipAddress","IP アドレス")}{field("port","ポート")}<div class="dialog-actions"><button type="button" class="btn" onClick={close}>キャンセル</button><button class="btn primary">{host?"更新":"追加"}</button></div></form></ModalShell>;
}

/** 参照しているキュー・ポン出しボタンの数を添える(旧 UI の削除確認と同じ) */
export function hostRemovalMessage(host:{id:string;name:string},cues:ReadonlyArray<{targetHostIds:string[]}>,buttons:ReadonlyArray<{targetHostIds:string[]}>):string{const c=cues.filter(q=>q.targetHostIds.includes(host.id)).length,b=buttons.filter(x=>x.targetHostIds.includes(host.id)).length;const refs=[c?`キュー ${c} 件`:"",b?`ポン出し ${b} 件`:""].filter(Boolean).join("と");return refs?`「${host.name}」を削除しますか？ ${refs}が送信先にしています。削除するとそれらは送信先を失います`:`「${host.name}」を削除しますか？`}
export function HostPanel({notice}:{notice:(text:string)=>void}):JSX.Element {
 const s=appStore.value.host.state!,[edit,setEdit]=useState<HostDto|null|"new">(null),[remove,setRemove]=useState<HostDto|null>(null);
 const close=()=>{setEdit(null);setRemove(null);appStore.updateUi({modalOpen:false})};
 const run=(name:Parameters<typeof command>[0],args:unknown)=>void command(name,args).then(data=>{if(name==="host.ping"){const r=(data??{}) as {reachable?:boolean;latencyMs?:number};notice(r.reachable===undefined?"疎通確認を送信しました":r.reachable?`疎通 OK · ${r.latencyMs??"--"} ms`:"疎通できません")}}).catch((e:{message:string})=>notice(e.message));
 return <><div class="panel-toolbar"><b>送信先ホスト</b><button class="btn right" onClick={()=>setEdit("new")}>＋ 追加</button></div><div class="host-list">{s.hosts.map(h=><div class={`host-row ${h.enabled?"":"disabled"}`}><span class={`reach ${h.reachability}`}/><b class="host-name">{h.name}</b><span class="mono host-address">{h.ipAddress}:{h.port}</span><div class="host-actions"><label><input type="checkbox" checked={h.enabled} onChange={e=>run("host.setEnabled",{id:h.id,enabled:e.currentTarget.checked})}/>有効</label><button class="btn" onClick={()=>run("host.ping",{id:h.id})}>疎通確認</button><button class="btn" onClick={()=>setEdit(h)}>編集</button><button class="btn danger-link" onClick={()=>{setRemove(h);appStore.updateUi({modalOpen:true})}}>削除</button></div></div>)}</div>{edit&&<HostDialog host={edit==="new"?undefined:edit} close={close}/>} {remove&&<ConfirmDialog message={hostRemovalMessage(remove,s.cues,s.triggerPanel.buttons)} close={close} confirm={()=>{run("host.remove",{id:remove.id});close()}}/>}</>;
}
