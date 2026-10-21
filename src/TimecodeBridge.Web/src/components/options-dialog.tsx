import type {JSX} from "preact";
import {useEffect} from "preact/hooks";
import {command} from "../commands";
import {appStore} from "../store";
import {ModalShell} from "./dialogs";

/** メニューの「設定 → オプション…」で開く。プロジェクトごとに保存される動作の設定をまとめる。 */
export function OptionsDialog({close,notice}:{close:()=>void;notice:(s:string)=>void}):JSX.Element {
  const t=appStore.value.host.state!.transport;
  useEffect(()=>appStore.updateUi({modalOpen:true}),[]);
  const set=(name:"autoMute.setEnabled"|"autoMute.setOnManualTrigger",enabled:boolean)=>
    void command(name,{enabled}).catch((e:{message:string})=>notice(e.message));
  return <ModalShell title="オプション" close={close}>
    <div class="options">
      <h3>オートミュート</h3>
      <label class="check options-row">
        <input type="checkbox" checked={t.autoMuteEnabled} onChange={e=>set("autoMute.setEnabled",e.currentTarget.checked)}/>
        <span>
          <b>発火後オートミュートを使う</b>
          <small>切ると、キューごとの「発火時ミュート」も働きません。キューリストのツールバーからも切り替えられます</small>
        </span>
      </label>
      <label class="check options-row">
        <input type="checkbox" checked={t.autoMuteOnManualTrigger} onChange={e=>set("autoMute.setOnManualTrigger",e.currentTarget.checked)}/>
        <span>
          <b>手動で叩いたときもミュートする</b>
          <small>行の ▶ で発火したときの動作です。切ってあると、進行の確認や差し込みで叩いてもミュートされず、後続の自動発火が止まりません</small>
        </span>
      </label>
      <div class="dialog-actions"><button type="button" class="btn primary" onClick={close}>閉じる</button></div>
    </div>
  </ModalShell>;
}
