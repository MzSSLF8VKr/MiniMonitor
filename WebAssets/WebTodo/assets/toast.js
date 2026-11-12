/* 右上角堆叠通知：自动消失，成功/错误/信息/警告四种变体。 */
import { esc } from './format.js';
import { icon } from './icons.js';

const ICON_FOR = { ok: 'check', err: 'alert', info: 'info', warn: 'alert' };

function root() {
  const el = document.getElementById('toasts');
  if (!el) {
    const created = document.createElement('div');
    created.id = 'toasts';
    created.className = 'toasts';
    created.setAttribute('role', 'region');
    created.setAttribute('aria-live', 'polite');
    document.body.appendChild(created);
    return created;
  }
  return el;
}

/**
 * @param {string} message 正文
 * @param {object} [opts]
 * @param {'ok'|'err'|'info'|'warn'} [opts.type]
 * @param {string} [opts.title]
 * @param {number} [opts.ms] 自动消失时间，0 = 不自动消失
 */
export function toast(message, opts = {}) {
  const type = opts.type || 'info';
  const ms = opts.ms === undefined ? (type === 'err' ? 7000 : 3800) : opts.ms;
  const el = document.createElement('div');
  el.className = `toast ${type}`;
  el.setAttribute('role', type === 'err' ? 'alert' : 'status');
  el.innerHTML =
    `<span class="toast-ico">${icon(ICON_FOR[type] || 'info', { cls: 'ico-sm' })}</span>`
    + `<div class="toast-body">`
    + (opts.title ? `<div class="toast-title">${esc(opts.title)}</div>` : '')
    + `<div class="toast-msg">${esc(message)}</div></div>`
    + `<button class="icon-btn tiny toast-close" type="button" aria-label="关闭通知">${icon('x', { cls: 'ico-xs' })}</button>`;

  const dismiss = () => {
    if (el.dataset.closing) return;
    el.dataset.closing = '1';
    el.classList.add('is-out');
    el.addEventListener('animationend', () => el.remove(), { once: true });
    setTimeout(() => el.remove(), 300);
  };
  el.querySelector('.toast-close').addEventListener('click', dismiss);

  const host = root();
  host.appendChild(el);
  while (host.children.length > 5) host.firstElementChild.remove();
  if (ms > 0) setTimeout(dismiss, ms);
  return dismiss;
}

export const toastOk = (m, o = {}) => toast(m, { ...o, type: 'ok' });
export const toastErr = (m, o = {}) => toast(m, { ...o, type: 'err' });
export const toastInfo = (m, o = {}) => toast(m, { ...o, type: 'info' });
export const toastWarn = (m, o = {}) => toast(m, { ...o, type: 'warn' });
