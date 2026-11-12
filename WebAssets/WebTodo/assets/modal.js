/* 模态框：role=dialog + aria-modal + 焦点陷阱 + Esc 关闭 + 关闭后焦点归还。 */
import { esc } from './format.js';
import { icon } from './icons.js';
import { pushScrim } from './overlay.js';

const openStack = [];
const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const KIND_CLASS = { primary: 'btn-primary', danger: 'btn-danger', ghost: 'btn-ghost' };

/**
 * @param {object} opts
 * @param {string} opts.title
 * @param {string} [opts.subtitle]
 * @param {string} [opts.html] 正文 HTML（调用方负责转义）
 * @param {boolean} [opts.wide]
 * @param {Array<{label:string, kind?:'primary'|'danger'|'ghost'|'default', onClick?:(h)=>any, type?:'button'|'submit', disabled?:boolean}>} [opts.actions]
 *   onClick 返回 true 表示保持打开（例如校验失败），其余情况自动关闭。
 * @param {(root:HTMLElement, h:object)=>void} [opts.onMount]
 * @param {()=>void} [opts.onClose]
 * @param {boolean} [opts.dismissable] 点击遮罩是否关闭
 */
export function openModal(opts = {}) {
  const root = document.getElementById('modalRoot');
  const layer = document.createElement('div');
  layer.className = 'modal-layer';

  const previouslyFocused = document.activeElement;

  layer.innerHTML =
    `<div class="modal-card${opts.wide ? ' is-wide' : ''}" role="dialog" aria-modal="true" aria-labelledby="modal-title-${openStack.length + 1}">`
    + `<div class="modal-head">`
    + `<div style="min-width:0"><h2 class="modal-title" id="modal-title-${openStack.length + 1}">${esc(opts.title || '')}</h2>`
    + (opts.subtitle ? `<p class="card-sub" style="margin-top:2px">${esc(opts.subtitle)}</p>` : '')
    + `</div>`
    + `<div class="card-actions">`
    + `<button class="icon-btn modal-x" type="button" aria-label="关闭对话框">${icon('x', { cls: 'ico-sm' })}</button>`
    + `</div></div>`
    + `<div class="modal-body">${opts.html || ''}</div>`
    + `<div class="modal-foot"></div>`
    + `</div>`;

  const card = layer.querySelector('.modal-card');
  const bodyEl = layer.querySelector('.modal-body');
  const footEl = layer.querySelector('.modal-foot');

  const handle = {
    el: card,
    body: bodyEl,
    root: layer,
    close: () => close(),
    setFooterError: (msg) => {
      let box = footEl.querySelector('.modal-error');
      if (!msg) { if (box) box.remove(); return; }
      if (!box) {
        box = document.createElement('div');
        box.className = 'modal-error field-error';
        footEl.insertBefore(box, footEl.firstChild);
      }
      box.textContent = msg;
    }
  };

  (opts.actions || []).forEach((action) => {
    const btn = document.createElement('button');
    btn.type = action.type || 'button';
    btn.className = `btn ${KIND_CLASS[action.kind] || ''}`.trim();
    btn.textContent = action.label;
    if (action.disabled) btn.disabled = true;
    btn.dataset.modalAction = action.label;
    btn.addEventListener('click', async () => {
      if (typeof action.onClick === 'function') {
        const keep = await action.onClick(handle);
        if (keep === true) return; // 校验失败等情况保持打开
      }
      close();
    });
    footEl.appendChild(btn);
  });

  if (typeof opts.onMount === 'function') opts.onMount(bodyEl, handle);

  root.appendChild(layer);
  openStack.push({ layer, close });

  const dismiss = pushScrim(() => { if (opts.dismissable !== false) close(); });

  function onKeydown(event) {
    if (event.key === 'Tab') {
      const items = [...card.querySelectorAll(FOCUSABLE)]
        .filter((el) => !el.hidden && el.getClientRects().length > 0);
      if (!items.length) { event.preventDefault(); return; }
      const first = items[0];
      const last = items[items.length - 1];
      const active = document.activeElement;
      if (event.shiftKey && (active === first || !card.contains(active))) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (active === last || !card.contains(active))) {
        event.preventDefault();
        first.focus();
      }
    }
  }

  layer.addEventListener('keydown', onKeydown);
  card.querySelector('.modal-x').addEventListener('click', () => close());

  // Esc：文档级监听，保证焦点落在遮罩外时同样能关闭最上层对话框
  const onDocKey = (event) => {
    if (event.key !== 'Escape') return;
    const top = openStack[openStack.length - 1];
    if (!top || top.layer !== layer) return;
    if (opts.dismissable === false) return;
    event.preventDefault();
    close();
  };
  document.addEventListener('keydown', onDocKey, true);

  function close() {
    if (layer.dataset.closed) return;
    layer.dataset.closed = '1';
    document.removeEventListener('keydown', onDocKey, true);
    const i = openStack.findIndex((x) => x.layer === layer);
    if (i >= 0) openStack.splice(i, 1);
    dismiss();
    layer.remove();
    if (typeof opts.onClose === 'function') opts.onClose();
    if (previouslyFocused && typeof previouslyFocused.focus === 'function' && document.contains(previouslyFocused)) {
      previouslyFocused.focus();
    }
  }

  // 初始焦点：显式声明 > 第一个可聚焦元素
  const preferred = card.querySelector('[data-autofocus]') || card.querySelector(FOCUSABLE);
  if (preferred) requestAnimationFrame(() => preferred.focus());
  else requestAnimationFrame(() => card.setAttribute('tabindex', '-1'));

  return handle;
}

/** 危险操作确认。 */
export function confirmModal({ title = '请确认', message = '', confirmLabel = '确定', cancelLabel = '取消', danger = true, detail = '' } = {}) {
  return new Promise((resolve) => {
    let answered = false;
    const html = `<div class="note-box ${danger ? 'is-warn' : ''}">${icon(danger ? 'alert' : 'info', { cls: 'ico' })}<div>${esc(message)}${detail ? `<div class="modal-danger-note" style="margin-top:6px">${esc(detail)}</div>` : ''}</div></div>`;
    openModal({
      title,
      html,
      actions: [
        { label: cancelLabel, kind: 'ghost', onClick: () => { answered = true; resolve(false); } },
        { label: confirmLabel, kind: danger ? 'danger' : 'primary', onClick: () => { answered = true; resolve(true); } }
      ],
      onClose: () => { if (!answered) resolve(false); }
    });
  });
}

/** 需要手动输入关键字的高危确认（导入 replace 用）。 */
export function typedConfirmModal({ title = '高危操作确认', message = '', expect = 'REPLACE', label = '请输入确认关键字', confirmLabel = '执行', cancelLabel = '取消' } = {}) {
  return new Promise((resolve) => {
    let answered = false;
    const html = `<div class="note-box is-err">${icon('alert', { cls: 'ico' })}<div>${esc(message)}</div></div>`
      + `<div class="field"><label class="field-label" for="typed-confirm">${esc(label)}</label>`
      + `<input id="typed-confirm" type="text" autocomplete="off" spellcheck="false" placeholder="${esc(expect)}" data-autofocus>`
      + `<p class="field-hint">必须精确输入 <code>${esc(expect)}</code> 才能继续。</p>`
      + `<p class="field-error typed-error" hidden>关键字不匹配。</p></div>`;

    openModal({
      title,
      html,
      actions: [
        { label: cancelLabel, kind: 'ghost', onClick: () => { answered = true; resolve(false); } },
        {
          label: confirmLabel,
          kind: 'danger',
          onClick: (h) => {
            const input = h.body.querySelector('#typed-confirm');
            const err = h.body.querySelector('.typed-error');
            const ok = (input.value || '').trim() === expect;
            err.hidden = ok;
            input.style.borderColor = ok ? '' : 'var(--err)';
            if (!ok) { input.focus(); return true; }
            answered = true;
            resolve(true);
            return false;
          }
        }
      ],
      onClose: () => { if (!answered) resolve(false); }
    });
  });
}

/** 当前是否有模态框打开（键盘快捷键需要避让）。 */
export function modalOpen() { return openStack.length > 0; }
