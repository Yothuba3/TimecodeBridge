/** Pure validation/normalization for timecode, OSC, addresses, ports, and numeric UI drafts. */
export type ValidationResult<T> = { ok: true; value: T } | { ok: false; message: string };
const valid=<T>(value:T):ValidationResult<T>=>({ok:true,value});const invalid=(message:string):ValidationResult<never>=>({ok:false,message});
export function validateTimecode(value:string,signed=false):ValidationResult<string>{const re=signed?/^[+-]\d{2}:\d{2}:\d{2}[:;]\d{2}$/:/^\d{2}:\d{2}:\d{2}[:;]\d{2}$/;if(!re.test(value))return invalid(signed?"±HH:MM:SS:FF 形式で入力してください":"HH:MM:SS:FF 形式で入力してください");const raw=(signed?value.slice(1):value).replace(";",":");const parts=raw.split(":").map(Number);return (parts[1]??99)<60&&(parts[2]??99)<60?valid(value):invalid("分・秒は 00〜59 です")}
export function validateOscAddress(value:string):ValidationResult<string>{return /^\/(?:[^\s/#*,?\[\]{}]+(?:\/[^\s/#*,?\[\]{}]+)*)?$/.test(value)?valid(value):invalid("/ で始まる OSC アドレスを入力してください")}
export function validateIpAddress(value:string):ValidationResult<string>{const p=value.split(".");return p.length===4&&p.every(x=>/^\d{1,3}$/.test(x)&&Number(x)<=255)?valid(value):invalid("IPv4 アドレスが不正です")}
export function validatePort(value:number):ValidationResult<number>{return Number.isInteger(value)&&value>=1&&value<=65535?valid(value):invalid("ポートは 1〜65535 です")}
