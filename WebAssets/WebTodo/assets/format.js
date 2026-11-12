/* 时间/文本格式化工具：任何 null / 非法值都必须安全返回，界面永不显示 undefined、NaN、Invalid Date。 */

const WEEKDAYS = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'];

/** HTML 转义；所有插值数据都必须过这里。 */
export function esc(value) {
  if (value === null || value === undefined) return '';
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/** 安全解析后端本地时间字符串 → Date | null。 */
export function parseDate(value) {
  if (!value) return null;
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;
  let s = String(value).trim();
  if (!s) return null;
  if (s.includes(' ')) s = s.replace(' ', 'T');
  if (/^\d{4}-\d{2}-\d{2}$/.test(s)) s += 'T00:00:00';
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(s)) s += ':00';
  const d = new Date(s);
  return Number.isNaN(d.getTime()) ? null : d;
}

const pad2 = (n) => String(n).padStart(2, '0');
const hm = (d) => `${pad2(d.getHours())}:${pad2(d.getMinutes())}`;
const md = (d) => `${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
const ymd = (d) => `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;

function startOfDay(d) {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x;
}

/** 与今天的天数差（0=今天，1=明天，-1=昨天）。 */
export function dayDelta(value, now = new Date()) {
  const d = parseDate(value);
  if (!d) return null;
  return Math.round((startOfDay(d) - startOfDay(now)) / 86400000);
}

/**
 * 人话时间：今天 16:45 / 明天 09:00 / 昨天 20:00 / 10-21 09:00 / 2026-12-31 18:00。
 * null 或非法值返回 fallback（默认空串）。
 */
export function fmtDateTime(value, fallback = '') {
  const d = parseDate(value);
  if (!d) return fallback;
  const delta = dayDelta(d);
  if (delta === 0) return `今天 ${hm(d)}`;
  if (delta === 1) return `明天 ${hm(d)}`;
  if (delta === -1) return `昨天 ${hm(d)}`;
  if (d.getFullYear() === new Date().getFullYear()) return `${md(d)} ${hm(d)}`;
  return `${ymd(d)} ${hm(d)}`;
}

/** 完整时间，日志表格用：2026-10-20 10:12:03。 */
export function fmtFull(value, fallback = '—', withSeconds = true) {
  const d = parseDate(value);
  if (!d) return fallback;
  return `${ymd(d)} ${hm(d)}${withSeconds ? `:${pad2(d.getSeconds())}` : ''}`;
}

/** 仅日期：2026-10-20。 */
export function fmtDate(value, fallback = '—') {
  const d = parseDate(value);
  if (!d) return fallback;
  return ymd(d);
}

/** 仅时间：16:45。 */
export function fmtTime(value, fallback = '—') {
  const d = parseDate(value);
  if (!d) return fallback;
  return hm(d);
}

/** 星期 + 日期，用于顶栏时钟：10月20日 周二。 */
export function fmtClockDate(d = new Date()) {
  return `${d.getMonth() + 1}月${d.getDate()}日 ${WEEKDAYS[d.getDay()]}`;
}

export function fmtClockTime(d = new Date()) {
  return `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
}

/** datetime-local 输入框需要 YYYY-MM-DDTHH:mm（本地时间，不能用 toISOString）。 */
export function toInputValue(value) {
  const d = parseDate(value);
  if (!d) return '';
  return `${ymd(d)}T${hm(d)}`;
}

/** 输入框值 → 后端时间字符串（补齐秒）。 */
export function fromInputValue(raw) {
  const s = (raw || '').trim();
  if (!s) return null;
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(s)) return `${s}:00`;
  return s;
}

/** 在本地时间字符串上加减分钟，返回后端格式。 */
export function shiftMinutes(value, minutes) {
  const d = parseDate(value);
  if (!d) return null;
  const out = new Date(d.getTime() - minutes * 60000);
  return `${ymd(out)}T${hm(out)}:00`;
}

export function isOverdue(value, now = new Date()) {
  const d = parseDate(value);
  if (!d) return false;
  return d.getTime() < now.getTime();
}

/** 「3 分钟前 / 2 小时后」这类相对描述。 */
export function fmtAgo(value, now = new Date()) {
  const d = parseDate(value);
  if (!d) return '';
  const diff = now.getTime() - d.getTime();
  const abs = Math.abs(diff);
  const suffix = diff >= 0 ? '前' : '后';
  const min = Math.round(abs / 60000);
  if (min < 1) return diff >= 0 ? '刚刚' : '即将';
  if (min < 60) return `${min} 分钟${suffix}`;
  const hour = Math.round(min / 60);
  if (hour < 24) return `${hour} 小时${suffix}`;
  const day = Math.round(hour / 24);
  if (day < 31) return `${day} 天${suffix}`;
  return fmtDateTime(d);
}

/** 剩余天数描述（项目过期时间用）。 */
export function fmtExpiry(value, now = new Date()) {
  const d = parseDate(value);
  if (!d) return null;
  const deltaDays = dayDelta(d, now);
  const label = fmtDateTime(d, '');
  if (deltaDays === null) return { text: label, tone: '' };
  if (deltaDays < 0) return { text: `${label} · 已过期`, tone: 'is-overdue' };
  if (deltaDays === 0) return { text: `${label} · 今天到期`, tone: 'is-soon' };
  if (deltaDays <= 7) return { text: `${label} · 还剩 ${deltaDays} 天`, tone: 'is-soon' };
  return { text: label, tone: '' };
}

export function fmtDuration(ms) {
  if (ms === null || ms === undefined || ms === '' || Number.isNaN(Number(ms))) return '—';
  const n = Number(ms);
  if (n < 1000) return `${n.toFixed(n < 10 ? 1 : 0)} ms`;
  return `${(n / 1000).toFixed(2)} s`;
}

export function fmtNumber(n) {
  const v = Number(n);
  if (Number.isNaN(v)) return '0';
  return v.toLocaleString('zh-CN');
}

export function fmtBytes(bytes) {
  const n = Number(bytes);
  if (!Number.isFinite(n) || n <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  let i = 0;
  let v = n;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i += 1; }
  return `${v.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

/** 一行文本预览（备注摘要）。 */
export function preview(text, max = 90) {
  if (text === null || text === undefined) return '';
  const flat = String(text).replace(/\s+/g, ' ').trim();
  if (!flat) return '';
  return flat.length > max ? `${flat.slice(0, max)}…` : flat;
}

export function isValidEmail(value) {
  return /^[^\s@,;]+@[^\s@,;]+\.[^\s@,;]+$/.test(String(value || '').trim());
}

export function toInt(value, fallback = null) {
  const n = parseInt(value, 10);
  return Number.isNaN(n) ? fallback : n;
}

export function toNum(value, fallback = null) {
  const n = Number(value);
  return Number.isFinite(n) ? n : fallback;
}

/** 安全 JSON 解析，失败返回 {ok:false, error}。 */
export function safeJson(text) {
  try {
    return { ok: true, value: JSON.parse(text) };
  } catch (err) {
    return { ok: false, error: err.message || 'JSON 解析失败' };
  }
}

/** 美化 JSON 并做轻量高亮（值已全部转义，安全）。 */
export function prettyJson(value) {
  let text;
  if (typeof value === 'string') {
    const parsed = safeJson(value);
    text = parsed.ok ? JSON.stringify(parsed.value, null, 2) : value;
  } else {
    try {
      text = JSON.stringify(value, null, 2);
    } catch {
      text = String(value);
    }
  }
  if (!text) return '';
  return esc(text)
    .replace(/^(\s*)&quot;(.+?)&quot;(\s*:)/gm, '$1<span class="k">"$2"</span>$3')
    .replace(/:\s&quot;(.*?)&quot;/g, ': <span class="s">"$1"</span>')
    .replace(/:\s(-?\d+(?:\.\d+)?)/g, ': <span class="n">$1</span>');
}

/** 逗号/空格/分号分隔的多收件人解析。 */
export function splitList(raw) {
  return String(raw || '')
    .split(/[,;\s]+/)
    .map((s) => s.trim())
    .filter(Boolean);
}
