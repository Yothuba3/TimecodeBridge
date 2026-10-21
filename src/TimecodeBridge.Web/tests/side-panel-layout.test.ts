import assert from "node:assert/strict";import test from "node:test";import {readFileSync} from "node:fs";

// ホストを 10 件ほど登録すると側パネルの中身が縦に伸びる。高さの制約が無いと
// 一覧が画面の下へあふれて届かなくなり、スクロールすれば今度は上のモニター領域に重なる。
// 実際の崩れは tools/tcb3/layout-check.js で実測して確かめるが、
// 崩れの原因になった指定が消えていないことはここで押さえる。
const css=readFileSync(new URL("../src/styles/m3.css",import.meta.url),"utf8");

test("side panel keeps a height constraint so its content cannot escape the pane",()=>{
  assert.match(css,/\.side\{[^}]*min-height:0/,"側パネルは高さを制約する(display:block+overflow:auto だけだと上の領域に重なる)");
  assert.doesNotMatch(css,/\.side\{display:block;overflow:auto\}/,"全体スクロールに戻すと、スクロール時に中身がモニター領域へ重なる");
});

test("tab panel reserves a row for its header so the list cannot cover it",()=>{
  // タブ・見出し・一覧の 3 つの子に対し 3 行を定義する(2 行だと見出しが 0px に潰れる)
  assert.match(css,/\.tab-panel\{display:grid;grid-template-rows:max-content max-content minmax\(0,1fr\)/);
});

test("both host lists scroll instead of pushing the panel out of the screen",()=>{
  assert.match(css,/\.host-list\{min-height:0;overflow:auto\}/,"ホスト管理の一覧はスクロールさせる");
  assert.match(css,/\.hosts\{max-height:\d+px;overflow:auto\}/,"Cue-Sync の送信先一覧にも上限を設ける");
});

test("host picker rows keep their own height instead of being squeezed",()=>{
  // 高さ 80px の器に 10 行入ると均等配分で 5px まで潰れ、チェックボックスが隣の行に重なる
  assert.match(css,/\.host-pick-list\{grid-auto-rows:min-content\}/);
});

test("side panel scrolls when the window is too short to fit its sections",()=>{
  // 最小サイズ(1100x640)では残り高さが 32px しかないのに、タブ枠に min-height:150px が効いて
  // 送信ログが画面の下へ 118px はみ出していた。収まらないときはパネルをスクロールさせる。
  assert.match(css,/\.side\{min-height:0;overflow:auto\}/);
});

test("dialog action buttons stay visible while the form scrolls",()=>{
  // 最小サイズではダイアログの中身が画面より高くなり、「追加」が初期表示で見えなかった。
  // 下端に貼り付けて、スクロールせずに押せるようにする。
  assert.match(css,/\.dialog \.dialog-actions\{position:sticky/);
});
