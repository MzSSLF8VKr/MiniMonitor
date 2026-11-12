/* 跨视图复用的 UI 片段与工具。 */
import { esc } from './format.js';
import { icon } from './icons.js';

/** 后端约定色板（docs/API.md · Project）。 */
export const PALETTE = ['#6366f1', '#f59e0b', '#ef4444', '#10b981', '#06b6d4', '#a855f7', '#ec4899', '#84cc16'];

export const PRIORITIES = [
  { value: 0, label: '无' },
  { value: 1, label: '低' },
  { value: 2, label: '中' },
  { value: 3, label: '高' }
];

export const PROJECT_ICON_NAMES = ['briefcase', 'book', 'cart', 'star', 'heart', 'home', 'flag', 'target', 'code', 'coffee', 'gift', 'plane'];

export const DEFAULT_COLOR = '#6366f1';

export function priorityLabel(value) {
  const hit = PRIORITIES.find((p) => p.value === Number(value));
  return hit ? hit.label : '无';
}

export function normalizePriority(value) {
  const n = Number(value);
  return [0, 1, 2, 3].includes(n) ? n : 0;
}

export function debounce(fn, ms = 250) {
  let t = null;
  return (...args) => {
    if (t) clearTimeout(t);
    t = setTimeout(() => { t = null; fn(...args); }, ms);
  };
}

/** 事件委托：容器内 [data-act] 元素点击 → 对应处理函数。 */
export function delegate(container, handlers) {
  const listener = (event) => {
    const el = event.target instanceof Element ? event.target.closest('[data-act]') : null;
    if (!el || !container.contains(el)) return;
    const fn = handlers[el.dataset.act];
    if (typeof fn === 'function') fn(el, event);
  };
  container.addEventListener('click', listener);
  return () => container.removeEventListener('click', listener);
}

/** 项目色点：实色为纯平面，炫彩为旋转 conic 渐变。 */
export function projectDot(project, extraCls = '') {
  const rainbow = project && project.colorMode === 'rainbow';
  const color = (project && project.color) || DEFAULT_COLOR;
  const cls = ['p-dot', rainbow ? 'rainbow' : '', extraCls].filter(Boolean).join(' ');
  const style = rainbow ? '' : ` style="background:${esc(color)};color:${esc(color)}"`;
  return `<span class="${cls}"${style} aria-hidden="true"></span>`;
}

export function projectIconHtml(project) {
  const name = project && project.icon && project.icon !== 'dot' ? project.icon : null;
  return name ? icon(name, { cls: 'ico ico-sm' }) : '';
}

/** 通用状态块（空态 / 错误态）。 */
export function stateBlock({ iconName = 'inbox', title = '暂无数据', text = '', actions = [], variant = '' }) {
  return `<div class="state ${variant}">`
    + `<div class="state-ico">${icon(iconName, { cls: 'ico' })}</div>`
    + `<h3>${esc(title)}</h3>`
    + (text ? `<p>${text}</p>` : '')
    + (actions.length ? `<div class="state-actions">${actions.join('')}</div>` : '')
    + `</div>`;
}

/** 后端未启动时的专用空态——要看起来像设计过的，而不是坏掉了。 */
export function offlineStateBlock() {
  return stateBlock({
    iconName: 'cloudOff',
    variant: 'err',
    title: '离线模式：后端未启动',
    text: `无法连接 <code>/api/state</code>。请先启动服务：双击 <code>TodoPush.exe</code>（Windows），
      或运行项目根目录下的 <code>start.bat</code> / <code>start.sh</code>，`
      + `然后刷新本页（默认地址 <code>http://127.0.0.1:50004</code>）。前端不缓存任何任务数据，`
      + `数据与配置只存在本机：Windows 是「文档/web_todo」，其他平台是项目下的 <code>data/</code>。`,
    actions: [
      `<button class="btn btn-primary" type="button" data-act="retry-state">${icon('refresh', { cls: 'ico-sm' })}重新连接</button>`,
      `<a class="btn btn-ghost" href="#/settings">${icon('sliders', { cls: 'ico-sm' })}查看服务器信息</a>`
    ]
  });
}

export function skeletonRows(count = 6, cls = '') {
  let out = '';
  for (let i = 0; i < count; i += 1) {
    out += `<div class="sk-row ${cls}">`
      + `<span class="sk" style="width:18px;height:18px;border-radius:50%"></span>`
      + `<span class="sk sk-line" style="flex:1 1 auto;max-width:${38 + (i % 4) * 12}%"></span>`
      + `<span class="sk sk-line" style="width:74px"></span>`
      + `<span class="sk sk-line" style="width:52px"></span>`
      + `</div>`;
  }
  return out;
}

export function skeletonCards(count = 4) {
  let out = '';
  for (let i = 0; i < count; i += 1) {
    out += `<div class="sk" style="height:74px;border-radius:12px"></div>`;
  }
  return `<div class="stats-grid" aria-hidden="true">${out}</div>`;
}

/** 分页控件（每页 size 条）。 */
export function pager({ total, limit, offset }, onLabel = '') {
  const size = Number(limit) || 50;
  const safeTotal = Number(total) || 0;
  const page = Math.floor((Number(offset) || 0) / size) + 1;
  const pages = Math.max(1, Math.ceil(safeTotal / size));
  const from = safeTotal === 0 ? 0 : Math.min(safeTotal, (Number(offset) || 0) + 1);
  const to = Math.min(safeTotal, (Number(offset) || 0) + size);
  return `<div class="pager">`
    + `<button class="btn btn-ghost btn-sm" type="button" data-act="page-prev" ${page <= 1 ? 'disabled' : ''}>${icon('chevronLeft', { cls: 'ico-sm' })}上一页</button>`
    + `<button class="btn btn-ghost btn-sm" type="button" data-act="page-next" ${page >= pages ? 'disabled' : ''}>下一页${icon('chevronRight', { cls: 'ico-sm' })}</button>`
    + `<span class="badge badge-no-dot">第 ${page} / ${pages} 页</span>`
    + `<span class="badge badge-no-dot">每页 ${size}</span>`
    + `<span class="pager-info">显示 ${from}–${to}，共 ${esc(safeTotal)} 条${onLabel ? ` · ${esc(onLabel)}` : ''}</span>`
    + `</div>`;
}

export function selectHtml(name, options, value, attrs = '') {
  const opts = options.map((o) => {
    const v = o.value === undefined ? '' : o.value;
    const selected = String(v) === String(value === undefined || value === null ? '' : value);
    return `<option value="${esc(v)}"${selected ? ' selected' : ''}>${esc(o.label)}</option>`;
  }).join('');
  return `<select ${attrs}>${opts}</select>`;
}

/** 收件人 / 标签一类的可移除 chip 列表。 */
export function chipList(values, { act = 'chip-remove', max = 40, empty = '（空）' } = {}) {
  if (!values || !values.length) return `<span class="field-hint">${esc(empty)}</span>`;
  return values.slice(0, max).map((v) => `<span class="chip">${esc(v)}`
    + `<button class="chip-x" type="button" data-act="${esc(act)}" data-v="${esc(v)}" aria-label="移除 ${esc(v)}">${icon('x', { cls: 'ico-xs' })}</button></span>`).join('');
}

/** 开关行：checkbox + 标题 + 说明。attrs 附加到 input 上（如 data-slot）。 */
export function switchHtml(id, checked, { title, hint = '', attrs = '' } = {}) {
  return `<label class="switch-row">`
    + `<span class="sr-text"><span class="sr-title">${esc(title)}</span>`
    + (hint ? `<span class="sr-hint">${esc(hint)}</span>` : '')
    + `</span>`
    + `<span class="switch"><input type="checkbox" id="${esc(id)}"${attrs ? ` ${attrs}` : ''} ${checked ? 'checked' : ''}>`
    + `<span class="switch-track" aria-hidden="true"></span></span>`
    + `</label>`;
}

export { esc, icon };
