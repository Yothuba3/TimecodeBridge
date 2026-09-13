import type {JSX} from "preact";
import {useEffect,useRef,useState} from "preact/hooks";
import type {FrameRate} from "../protocol";

export type TimecodeParts=[string,string,string,string];

export function normalizeTimecodeDigits(value:string):string {
  return value.replace(/[０-９]/g,c=>String.fromCharCode(c.charCodeAt(0)-0xfee0)).replace(/[^0-9]/g,"");
}

export function splitTimecode(value:string,signed=false):{sign:"+"|"-";parts:TimecodeParts} {
  const sign:"+"|"-"=signed&&value.startsWith("-")?"-":"+";
  const body=signed&&/^[+-]/.test(value)?value.slice(1):value;
  const fields=body.split(/[:;]/).slice(0,4).map(normalizeTimecodeDigits);
  return {sign,parts:[fields[0]??"",fields[1]??"",fields[2]??"",fields[3]??""]};
}

export function joinTimecode(parts:TimecodeParts,sign:"+"|"-"="+",signed=false):string {
  if(parts.every(x=>x===""))return "";
  return `${signed?sign:""}${parts.map(x=>x.padStart(2,"0")).join(":")}`;
}

export function enterTimecodeDigits(parts:TimecodeParts,index:number,raw:string):{parts:TimecodeParts;focus:number|null} {
  const digits=normalizeTimecodeDigits(raw),next=[...parts] as TimecodeParts;
  if(digits.length<=2){next[index]=digits;return {parts:next,focus:digits.length===2&&index<3?index+1:null}}
  for(let i=index,pos=0;i<4&&pos<digits.length;i++,pos+=2)next[i]=digits.slice(pos,pos+2);
  return {parts:next,focus:Math.min(3,index+Math.floor(Math.min(digits.length,8)/2))};
}

export function timecodeFocusForKey(key:string,index:number,isEmpty:boolean):number|null {
  if(key==="ArrowLeft"&&index>0)return index-1;
  if(key==="ArrowRight"&&index<3)return index+1;
  if(key==="Backspace"&&isEmpty&&index>0)return index-1;
  return null;
}

export const frameMaximum=(frameRate:FrameRate):number=>frameRate==="24"?23:frameRate==="25"?24:29;

export function clampTimecodePart(value:string,index:number,frameRate:FrameRate):string {
  if(value==="")return value;
  const maximum=index===0?23:index===1||index===2?59:frameMaximum(frameRate);
  return String(Math.min(maximum,Number(value))).padStart(2,"0");
}

export function TimecodeInput({value,onChange,onCommit,frameRate,signed=false,required=false,disabled=false,ariaLabel,class:className=""}:{value:string;onChange?:(value:string)=>void;onCommit?:(value:string)=>void;frameRate:FrameRate;signed?:boolean;required?:boolean;disabled?:boolean;ariaLabel:string;class?:string}):JSX.Element {
  const parsed=splitTimecode(value,signed),[parts,setParts]=useState<TimecodeParts>(parsed.parts),[sign,setSign]=useState<"+"|"-">(parsed.sign),refs=useRef<Array<HTMLInputElement|null>>([]),published=useRef<string|null>(null),latest=useRef<TimecodeParts>(parsed.parts);
  useEffect(()=>{if(value===published.current){published.current=null;return}const next=splitTimecode(value,signed);latest.current=next.parts;setParts(next.parts);setSign(next.sign)},[value,signed]);
  const publish=(next:TimecodeParts,nextSign=sign)=>{latest.current=next;setParts(next);const result=joinTimecode(next,nextSign,signed);published.current=result;onChange?.(result)};
  const focus=(index:number|null)=>{if(index!==null)refs.current[index]?.focus()};
  const blur=(index:number)=>{const next=[...latest.current] as TimecodeParts;next[index]=clampTimecodePart(next[index]??"",index,frameRate);publish(next);onCommit?.(joinTimecode(next,sign,signed))};
  return <span class={`timecode-input ${className}`} role="group" aria-label={ariaLabel}>
    {signed&&<select class="timecode-sign" aria-label={`${ariaLabel}の符号`} disabled={disabled} value={sign} onChange={e=>{const next=e.currentTarget.value as "+"|"-";setSign(next);const joined=joinTimecode(latest.current,next,true);published.current=joined;onChange?.(joined);onCommit?.(joined)}}><option value="+">+</option><option value="-">−</option></select>}
    {parts.map((part,index)=><span class="timecode-segment"><input ref={el=>{refs.current[index]=el}} class="timecode-part" inputMode="numeric" pattern="[0-9]*" maxlength={2} required={required} disabled={disabled} value={part} aria-label={`${ariaLabel} ${["時","分","秒","フレーム"][index]??""}`} onInput={e=>{const result=enterTimecodeDigits(latest.current,index,e.currentTarget.value);publish(result.parts);focus(result.focus)}} onPaste={e=>{e.preventDefault();const result=enterTimecodeDigits(latest.current,index,e.clipboardData?.getData("text")??"");publish(result.parts);focus(result.focus)}} onBlur={()=>blur(index)} onKeyDown={e=>{if(e.key==="ArrowUp"||e.key==="ArrowDown"){e.preventDefault();const maximum=index===0?23:index===1||index===2?59:frameMaximum(frameRate),current=Number(part||0),next=[...latest.current] as TimecodeParts;next[index]=String(Math.max(0,Math.min(maximum,current+(e.key==="ArrowUp"?1:-1)))).padStart(2,"0");publish(next);return}const target=timecodeFocusForKey(e.key,index,part==="");if(target!==null){e.preventDefault();focus(target)}}}/>{index<3&&<span class="timecode-colon" aria-hidden="true">:</span>}</span>)}
    <small class="timecode-limit">FF 00–{String(frameMaximum(frameRate)).padStart(2,"0")}</small>
  </span>;
}
