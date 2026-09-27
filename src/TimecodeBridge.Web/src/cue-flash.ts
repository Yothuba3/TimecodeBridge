/** 発火は Host が cue.runtime.flashToken を進めることで伝わる。前回見た値から進んだキューの id を返す(初見は発火扱いにしない)。 */
export function firedSince(seen:Map<string,number>,cues:ReadonlyArray<{id:string;runtime:{flashToken:number}}>):string[] {
  const fired:string[]=[];
  for(const q of cues){const token=q.runtime.flashToken,previous=seen.get(q.id);seen.set(q.id,token);if(previous!==undefined&&token>previous)fired.push(q.id)}
  return fired;
}
