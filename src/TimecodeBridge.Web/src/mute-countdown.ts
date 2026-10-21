/** 自動解除までの残り時間。MUTE ボタンに出すので、本番で一目で読める短い表記にする。 */
export function muteCountdown(untilUtc: string | null, nowMs: number): string {
  if (!untilUtc) return "";
  const until = Date.parse(untilUtc);
  if (Number.isNaN(until)) return "";
  const remainingMs = until - nowMs;
  if (remainingMs <= 0) return "";
  // 切り上げ: 残り 0.4 秒を「0」と出すと、まだ解除されていないのに終わったように見える
  const seconds = Math.ceil(remainingMs / 1000);
  if (seconds < 60) return `${seconds}`;
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}
