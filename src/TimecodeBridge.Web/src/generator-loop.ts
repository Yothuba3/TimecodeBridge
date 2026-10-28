import type {FrameRate} from "./protocol";

const fps=(rate:FrameRate):number=>rate==="24"?24:rate==="25"?25:30;

/**
 * ループを入れた直後の終了 TC の初期値。
 * 空欄のまま有効にすると何を繰り返すのか決まらないので、既定の長さを置く。
 * 24 時間を越える場合は 23:59:59:FF で止める(TC は巻き戻さない)。
 */
export function defaultLoopEnd(startTime:string,frameRate:FrameRate,seconds=10):string {
  const parts=startTime.split(":").map(n=>Number(n));
  if (parts.length!==4||parts.some(n=>!Number.isFinite(n))) return startTime;
  const rate=fps(frameRate);
  const total=((parts[0]!*60+parts[1]!)*60+parts[2]!)*rate+parts[3]!+seconds*rate;
  const limit=24*3600*rate-1;
  const capped=Math.min(total,limit);
  const pad=(n:number)=>String(n).padStart(2,"0");
  return [pad(Math.floor(capped/(3600*rate))),pad(Math.floor(capped/(60*rate))%60),pad(Math.floor(capped/rate)%60),pad(capped%rate)].join(":");
}

// 前後だけ比べられればよいので、フレームは 100 進で並べる(レートに依らない)
const order=(timecode:string):number|null=>{
  const parts=timecode.split(/[:;]/).map(n=>Number(n));
  if (parts.length!==4||parts.some(n=>!Number.isFinite(n))) return null;
  return ((parts[0]!*60+parts[1]!)*60+parts[2]!)*100+parts[3]!;
};

/**
 * 終了 TC が開始 TC より後か。ループなしは常に成り立つ。
 * 読めない値は Host に判断を任せる(形式の誤りは Host が返す)。
 */
export function loopRangeValid(startTime:string,loopEndTime:string|null):boolean {
  if (loopEndTime===null) return true;
  const start=order(startTime),end=order(loopEndTime);
  return start===null||end===null||end>start;
}
