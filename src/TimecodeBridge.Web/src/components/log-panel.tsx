import type {JSX} from "preact";
import {useLayoutEffect,useRef} from "preact/hooks";
import {command} from "../commands";
import type {LogDto} from "../protocol";
import {appStore} from "../store";
export const newestLogsFirst=(logs:LogDto[])=>[...logs].sort((a,b)=>Date.parse(b.timestampUtc)-Date.parse(a.timestampUtc));
export function LogPanel():JSX.Element {const logs=newestLogsFirst(appStore.value.host.state!.logs),ref=useRef<HTMLDivElement>(null),wasBottom=useRef(true);useLayoutEffect(()=>{const el=ref.current;if(el&&wasBottom.current)el.scrollTop=el.scrollHeight},[logs.length]);return <><div class="panel-toolbar"><b>送信ログ</b><span class="tiny">新しい順</span><button class="btn right" onClick={()=>void command("logs.clear",{})}>クリア</button></div><div class="dock log-dock" ref={ref} onScroll={e=>{const el=e.currentTarget;wasBottom.current=el.scrollHeight-el.scrollTop-el.clientHeight<4}}>{logs.length?logs.map(l=><div class={l.success?"log-success":"failure"}><time>{new Date(l.timestampUtc).toLocaleTimeString("ja-JP")}</time><span>{l.message}</span></div>):<div class="muted">送信ログはありません</div>}</div></>}
