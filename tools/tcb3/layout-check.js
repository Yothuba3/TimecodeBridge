// UI のレイアウト破綻を機械的に探す。tcb3ctl 経由で WebView 内に流し込んで使う。
//
//   tools/tcb3/tcb3ctl eval "$(cat tools/tcb3/layout-check.js)" | jq .
//
// 見るもの: 画面外へのはみ出し、器からあふれた文字(省略記号なしの切断を含む)、
// 同じ層にある操作要素どうしの重なり、潰れた操作要素。
// 手前に重ねる設計の層(ドロワー・ダイアログ・トースト)と背後の要素の重なりは除く。
(()=>{
const R=[];
const vis=e=>{const s=getComputedStyle(e);if(s.display==='none'||s.visibility==='hidden'||s.opacity==='0')return false;const r=e.getBoundingClientRect();return r.width>0&&r.height>0};
const label=e=>{const t=(e.textContent||'').trim().replace(/\s+/g,' ').slice(0,28);return e.tagName.toLowerCase()+(e.className&&typeof e.className==='string'?'.'+e.className.trim().split(/\s+/).slice(0,2).join('.'):'')+(t?` "${t}"`:'')};
const all=[...document.querySelectorAll('body *')].filter(vis);
// スクロールできる祖先を持つ要素は、枠の外にあってもスクロールすれば見える
const scrollable=e=>{for(let p=e.parentElement;p;p=p.parentElement){const o=getComputedStyle(p).overflowY;if((o==='auto'||o==='scroll')&&p.scrollHeight>p.clientHeight+1)return true}return false};
// スクロールできる器の表示範囲から外れている(＝今は見えていない)要素。
// ただし器が中身を切り取らない(visible が混ざる)場合は、はみ出した部分が他へ重なって見えるので除外しない
const clipped=e=>{const r=e.getBoundingClientRect();for(let p=e.parentElement;p;p=p.parentElement){const cs=getComputedStyle(p);const o=cs.overflowY;if(o==='auto'||o==='scroll'||o==='hidden'){const pr=p.getBoundingClientRect();if(r.bottom>pr.bottom+1||r.top<pr.top-1)return true}}return false};

// 1) 横スクロール(ビューポートからのはみ出し)
const de=document.documentElement;
if(de.scrollWidth>innerWidth+1) R.push({k:'横はみ出し',d:`document ${de.scrollWidth}px > viewport ${innerWidth}px`});
for(const e of all){const r=e.getBoundingClientRect();
  if(r.right>innerWidth+1&&r.width<innerWidth) R.push({k:'画面右外',d:`${label(e)} right=${Math.round(r.right)} > ${innerWidth}`});
  if(r.left<-1) R.push({k:'画面左外',d:`${label(e)} left=${Math.round(r.left)}`});
  // 閉じたドロワーは画面外に置くのが設計、スクロールできる器の中身は見えるので、どちらも除く
  if(r.bottom>innerHeight+1&&!e.closest('.drawer')&&!scrollable(e)) R.push({k:'画面下外',d:`${label(e)} bottom=${Math.round(r.bottom)} > ${innerHeight}`});
}
// 2) 内容が器からあふれている(文字の切れ)
for(const e of all){
  const s=getComputedStyle(e);
  if(s.overflow!=='visible'&&s.overflow!=='')continue;
  if(e.children.length>0)continue;
  // 入力欄は中身をスクロールして読むものなので、枠より長い値が入っていても破綻ではない
  if(/^(INPUT|TEXTAREA|SELECT)$/.test(e.tagName))continue;
  if(e.scrollWidth>e.clientWidth+2&&e.clientWidth>0) R.push({k:'文字が切れる',d:`${label(e)} 必要${e.scrollWidth}px / 枠${e.clientWidth}px`});
}
// 3) クリップされた要素(ellipsis なしで隠れている)
for(const e of all){
  const s=getComputedStyle(e);
  if(s.overflow==='hidden'&&s.textOverflow!=='ellipsis'&&e.children.length===0&&e.scrollWidth>e.clientWidth+2)
    R.push({k:'省略記号なしで切断',d:`${label(e)} 必要${e.scrollWidth}px / 枠${e.clientWidth}px`});
}
// 4) 操作要素の重なり
const ctl=all.filter(e=>/^(button|input|select|textarea|a)$/.test(e.tagName.toLowerCase()));
for(let i=0;i<ctl.length;i++)for(let j=i+1;j<ctl.length;j++){
  const a=ctl[i],b=ctl[j]; if(a.contains(b)||b.contains(a))continue;
  // 手前に重ねる設計の層(ドロワー/ダイアログ)と背後の要素は重なって当然なので除く
  const layer=e=>e.closest('.drawer,.dialog,.modal,.toast');
  if(layer(a)!==layer(b))continue;
  // スクロールできる器からはみ出した部分は切り取られて見えないので、重なりとして数えない
  if(clipped(a)||clipped(b))continue;
  const x=a.getBoundingClientRect(),y=b.getBoundingClientRect();
  const ox=Math.min(x.right,y.right)-Math.max(x.left,y.left), oy=Math.min(x.bottom,y.bottom)-Math.max(x.top,y.top);
  if(ox>2&&oy>2) R.push({k:'操作要素の重なり',d:`${label(a)} ∩ ${label(b)} = ${Math.round(ox)}x${Math.round(oy)}px`});
}
// 4.5) 行が潰れて中身がはみ出している(チェックボックスが隣の行に重なる等)
for(const e of all){
  if(e.children.length===0)continue;
  const r=e.getBoundingClientRect();
  for(const c of e.children){
    // 列幅のつまみのように、わざと親より大きくして掴みやすくしている部品は除く
    if(getComputedStyle(c).position==='absolute')continue;
    const cr=c.getBoundingClientRect();
    if(cr.height>r.height+2&&cr.height>8) R.push({k:'行が潰れて中身がはみ出す',d:`${label(e)} 枠${Math.round(r.height)}px < 中身${Math.round(cr.height)}px (${label(c)})`});
  }
}
// 4.6) 画面からあふれているのに、どの器もスクロールできず届かない
{
  const de=document.documentElement;
  const reachable=e=>{for(let p=e.parentElement;p;p=p.parentElement){const o=getComputedStyle(p).overflowY;if((o==='auto'||o==='scroll')&&p.scrollHeight>p.clientHeight+1)return true}return false};
  const stranded=ctl.filter(e=>{const r=e.getBoundingClientRect();return r.bottom>innerHeight+1&&!reachable(e)&&!e.closest('.drawer')});
  if(stranded.length&&de.scrollHeight<=de.clientHeight+1)
    R.push({k:'画面外で届かない操作要素',d:`${stranded.length} 件(例: ${label(stranded[0])} bottom=${Math.round(stranded[0].getBoundingClientRect().bottom)} > ${innerHeight})`});
}
// 5) 潰れた操作要素
for(const e of ctl){const r=e.getBoundingClientRect();
  if(r.width<8||r.height<8) R.push({k:'潰れた操作要素',d:`${label(e)} ${Math.round(r.width)}x${Math.round(r.height)}`});
  else if(r.height<20&&e.tagName!=='INPUT') R.push({k:'高さが小さい',d:`${label(e)} h=${Math.round(r.height)}`});
}
const seen=new Set(); const uniq=R.filter(x=>{const k=x.k+x.d;if(seen.has(k))return false;seen.add(k);return true});
return JSON.stringify({total:uniq.length,byKind:uniq.reduce((a,x)=>(a[x.k]=(a[x.k]||0)+1,a),{}),items:uniq.slice(0,40)});
})()
