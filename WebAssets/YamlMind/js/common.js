/* ==========================================================================
   YAML Mind — shared runtime: theme, API client, toasts, modal, icons, format
   ========================================================================== */
(function (global) {
  'use strict';

  /* ------------------------------------------------------------------ icons */
  const P = {
    branch: '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="12" r="3"/><path d="M6 9v6M9 6h3a3 3 0 0 1 3 3v0M9 18h3a3 3 0 0 0 3-3v0"/>',
    map: '<circle cx="12" cy="12" r="2.6"/><circle cx="4.5" cy="6" r="2"/><circle cx="19.5" cy="6" r="2"/><circle cx="4.5" cy="18" r="2"/><circle cx="19.5" cy="18" r="2"/><path d="M6.3 7.1 10 10.4M17.7 7.1 14 10.4M6.3 16.9 10 13.6M17.7 16.9 14 13.6"/>',
    file: '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h4"/>',
    code: '<path d="m9 18-6-6 6-6M15 6l6 6-6 6"/>',
    gear: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6h.09A1.65 1.65 0 0 0 10 3.09V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9v.09a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    save: '<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><path d="M17 21v-8H7v8M7 3v5h8"/>',
    eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/>',
    trash: '<path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6"/>',
    copy: '<rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
    search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2"/>',
    lock: '<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
    unlock: '<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 7.5-2"/>',
    down: '<path d="M12 3v12m0 0 4.5-4.5M12 15l-4.5-4.5M4 20h16"/>',
    up: '<path d="M12 21V9m0 0 4.5 4.5M12 9l-4.5 4.5M4 4h16"/>',
    sun: '<circle cx="12" cy="12" r="4.2"/><path d="M12 2v2.4M12 19.6V22M4.2 4.2l1.7 1.7M18.1 18.1l1.7 1.7M2 12h2.4M19.6 12H22M4.2 19.8l1.7-1.7M18.1 5.9l1.7-1.7"/>',
    moon: '<path d="M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z"/>',
    monitor: '<rect x="2.5" y="4" width="19" height="13" rx="2"/><path d="M8.5 21h7M12 17v4"/>',
    check: '<path d="m4.5 12.5 5 5 10-11"/>',
    x: '<path d="M6 6l12 12M18 6 6 18"/>',
    alert: '<path d="M10.3 3.9 1.9 18a2 2 0 0 0 1.7 3h16.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4.5M12 17.2h.01"/>',
    info: '<circle cx="12" cy="12" r="9.2"/><path d="M12 11v5.5M12 7.6h.01"/>',
    chevron: '<path d="m9 6 6 6-6 6"/>',
    tree: '<path d="M4 5h6a2 2 0 0 1 2 2v0M4 12h6a2 2 0 0 1 2 2v0M4 19h6M12 7h6M12 14h6"/><circle cx="20" cy="7" r="2"/><circle cx="20" cy="14" r="2"/><circle cx="20" cy="19" r="2"/>',
    radial: '<circle cx="12" cy="12" r="2.4"/><path d="M12 3v3.6M12 17.4V21M3 12h3.6M17.4 12H21M5.6 5.6l2.6 2.6M15.8 15.8l2.6 2.6M18.4 5.6l-2.6 2.6M8.2 15.8l-2.6 2.6"/>',
    refresh: '<path d="M20.5 12a8.5 8.5 0 1 1-2.6-6.1"/><path d="M20.6 4v5h-5"/>',
    db: '<ellipse cx="12" cy="5.5" rx="8" ry="3"/><path d="M4 5.5v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6M4 11.5v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6"/>',
    clock: '<circle cx="12" cy="12" r="9.2"/><path d="M12 7v5.4l3.4 2"/>',
    edit: '<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/>',
    zoomIn: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2M11 8.4v5.2M8.4 11h5.2"/>',
    zoomOut: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2M8.4 11h5.2"/>',
    fit: '<path d="M4 9V5.5A1.5 1.5 0 0 1 5.5 4H9M15 4h3.5A1.5 1.5 0 0 1 20 5.5V9M20 15v3.5a1.5 1.5 0 0 1-1.5 1.5H15M9 20H5.5A1.5 1.5 0 0 1 4 18.5V15"/>',
    layers: '<path d="m12 3 9 5-9 5-9-5 9-5z"/><path d="m3 13 9 5 9-5M3 17l9 5 9-5"/>',
    home: '<path d="M4 11.2 12 4l8 7.2"/><path d="M6.4 10v9.2h11.2V10"/>',
    history: '<path d="M3.5 12a8.5 8.5 0 1 0 2.6-6.1"/><path d="M3.4 4v5h5"/><path d="M12 8v4.4l3 1.8"/>',
    play: '<path d="M7 4.8 19 12 7 19.2z"/>',
    sparkle: '<path d="M12 3.2 13.9 9 20 11l-6.1 2L12 19l-1.9-6L4 11l6.1-2z"/><path d="M18.6 3v3.2M20.2 4.6H17"/>',
    doc: '<path d="M6 3h8l4 4v14H6z"/><path d="M14 3v4h4"/><path d="M9 12h6M9 16h6M9 8h2"/>',
    folder: '<path d="M3 7.5A1.5 1.5 0 0 1 4.5 6h4.1a1.5 1.5 0 0 1 1.2.6l1 1.4h7.7A1.5 1.5 0 0 1 20 9.5V18a1.5 1.5 0 0 1-1.5 1.5h-14A1.5 1.5 0 0 1 3 18z"/>',
    grid: '<rect x="3.5" y="3.5" width="7" height="7" rx="1.6"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.6"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.6"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.6"/>',
    list: '<path d="M8.5 6H21M8.5 12H21M8.5 18H21M3.6 6h.01M3.6 12h.01M3.6 18h.01"/>',
    bolt: '<path d="M13.2 2.5 4.8 13.4h6.1l-1.1 8.1 8.4-10.9h-6.1z"/>',
    shield: '<path d="M12 3 5 6v5.6c0 4.3 2.9 8.2 7 9.4 4.1-1.2 7-5.1 7-9.4V6z"/><path d="m9.2 12 2 2 3.6-3.8"/>',
    key: '<circle cx="7.5" cy="15.5" r="3.5"/><path d="m10 13 8-8M15.5 5.5 18 8M17 4l3 3"/>',
    target: '<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="4.6"/><circle cx="12" cy="12" r="1"/>',
    wrap: '<path d="M4 6h14a3 3 0 0 1 0 6H9"/><path d="m11.5 9.5-2.6 2.5 2.6 2.5M4 18h16"/>',
  };

  function icon(name, cls) {
    const d = P[name];
    if (!d) return '';
    return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" ' +
      'stroke-linecap="round" stroke-linejoin="round"' + (cls ? ' class="' + cls + '"' : '') +
      ' aria-hidden="true">' + d + '</svg>';
  }

  /* ------------------------------------------------------------------ theme */
  const mql = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  let themeSetting = localStorage.getItem('ym:theme') || 'dark';

  function resolve(setting) {
    if (setting === 'auto') return mql && !mql.matches ? 'light' : 'dark';
    return setting === 'light' ? 'light' : 'dark';
  }

  function applyTheme(setting, persist) {
    themeSetting = setting || themeSetting;
    const eff = resolve(themeSetting);
    document.documentElement.setAttribute('data-theme', eff);
    document.documentElement.setAttribute('data-theme-setting', themeSetting);
    if (persist !== false) localStorage.setItem('ym:theme', themeSetting);
    const m = document.querySelector('meta[name="theme-color"]');
    if (m) m.setAttribute('content', eff === 'dark' ? '#0a0c11' : '#f4f2ed');
    return eff;
  }

  // pre-paint bootstrap (also inlined in every <head> for zero flash)
  applyTheme(localStorage.getItem('ym:theme') || 'dark', false);
  if (mql) {
    const h = () => { if (themeSetting === 'auto') applyTheme('auto', false); };
    mql.addEventListener ? mql.addEventListener('change', h) : mql.addListener(h);
  }

  /* -------------------------------------------------------------------- api */
  class ApiError extends Error {
    constructor(status, code, message) {
      super(message || ('请求失败 (' + status + ')'));
      this.status = status; this.code = code; this.name = 'ApiError';
    }
  }

  async function api(path, opts) {
    const o = Object.assign({ headers: {} }, opts || {});
    if (o.body !== undefined && typeof o.body !== 'string' && !(o.body instanceof FormData)) {
      o.body = JSON.stringify(o.body);
      o.headers['Content-Type'] = 'application/json';
    }
    let res;
    try {
      res = await fetch(path, o);
    } catch (err) {
      throw new ApiError(0, 'network', '无法连接服务：' + err.message);
    }
    const type = res.headers.get('content-type') || '';
    const data = type.indexOf('application/json') >= 0 ? await res.json().catch(() => null) : await res.text();
    if (!res.ok) {
      const e = (data && data.error) || {};
      throw new ApiError(res.status, e.code || 'error', e.message || res.statusText);
    }
    return data;
  }

  /* ------------------------------------------------------------------ toast */
  let toastRoot = null;
  function toastRootEl() {
    if (!toastRoot) {
      toastRoot = document.createElement('div');
      toastRoot.className = 'toasts';
      document.body.appendChild(toastRoot);
    }
    return toastRoot;
  }

  function toast(message, opts) {
    const o = opts || {};
    const kind = o.kind || 'info';
    const iconName = o.kind === 'ok' ? 'check' : o.kind === 'danger' ? 'alert' :
      o.kind === 'warn' ? 'alert' : 'info';
    const el = document.createElement('div');
    el.className = 'toast toast--' + kind;
    el.innerHTML =
      '<span class="toast__icon">' + icon(iconName) + '</span>' +
      '<div class="toast__body">' +
      (o.title ? '<div class="toast__title">' + esc(o.title) + '</div>' : '') +
      '<div class="toast__msg">' + esc(message) + '</div>' +
      '</div>' +
      '<button class="toast__close" type="button" aria-label="关闭">' + icon('x') + '</button>';
    const root = toastRootEl();
    root.appendChild(el);
    while (root.children.length > 4) root.removeChild(root.firstChild);
    const timer = setTimeout(close, o.duration || (kind === 'danger' ? 6500 : 3600));
    el.querySelector('.toast__close').addEventListener('click', close);
    function close() {
      clearTimeout(timer);
      if (!el.parentNode) return;
      el.classList.add('is-out');
      setTimeout(() => el.remove(), 260);
    }
    return close;
  }

  /* ------------------------------------------------------------------ modal */
  function modal(spec) {
    return new Promise((resolvePromise) => {
      const overlay = document.createElement('div');
      overlay.className = 'overlay';
      const wide = spec.wide ? ' modal--wide' : '';
      overlay.innerHTML =
        '<div class="modal' + wide + '" role="dialog" aria-modal="true">' +
        '<div class="modal__head">' +
        '<div class="grow"><div class="modal__title">' + esc(spec.title || '') + '</div>' +
        (spec.subtitle ? '<div class="modal__sub">' + esc(spec.subtitle) + '</div>' : '') +
        '</div>' +
        '<button class="icon-btn" data-act="close" aria-label="关闭">' + icon('x') + '</button>' +
        '</div>' +
        '<div class="modal__body">' + (spec.body || '') + '</div>' +
        '<div class="modal__foot">' +
        '<button class="btn btn--ghost" data-act="cancel">' + esc(spec.cancelText || '取消') + '</button>' +
        '<button class="btn ' + (spec.danger ? 'btn--danger-solid' : 'btn--primary') + '" data-act="ok">' +
        (spec.okIcon ? icon(spec.okIcon) : '') + esc(spec.okText || '确定') + '</button>' +
        '</div></div>';
      document.body.appendChild(overlay);
      const prev = document.activeElement;
      let done = false;

      function finish(value) {
        if (done) return;
        done = true;
        overlay.classList.add('is-out');
        setTimeout(() => overlay.remove(), 180);
        if (prev && prev.focus) prev.focus();
        resolvePromise(value);
      }
      overlay.addEventListener('click', (e) => {
        if (e.target === overlay) return finish(spec.cancelValue !== undefined ? spec.cancelValue : false);
        const act = e.target.closest('[data-act]');
        if (!act) return;
        if (act.dataset.act === 'close' || act.dataset.act === 'cancel') finish(false);
        if (act.dataset.act === 'ok') finish(spec.onOk ? spec.onOk(overlay) : true);
      });
      overlay.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') { e.preventDefault(); finish(false); }
        if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) { e.preventDefault(); finish(spec.onOk ? spec.onOk(overlay) : true); }
      });
      const first = overlay.querySelector('.modal__body input, .modal__body textarea, .modal__body button[data-act="ok"]');
      setTimeout(() => first && first.focus && first.focus(), 60);
      if (spec.onMount) spec.onMount(overlay, finish);
    });
  }

  function confirmDialog(opts) {
    return modal({
      title: opts.title, subtitle: opts.subtitle,
      body: '<p class="dim" style="line-height:1.7;font-size:var(--fs-sm)">' + esc(opts.message || '') + '</p>',
      okText: opts.okText || '确定', cancelText: opts.cancelText || '取消',
      danger: !!opts.danger, okIcon: opts.okIcon,
    });
  }

  /* ----------------------------------------------------------------- format */
  function esc(s) {
    return String(s == null ? '' : s)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  function bytes(n) {
    n = Number(n) || 0;
    if (n < 1024) return n + ' B';
    const u = ['KB', 'MB', 'GB', 'TB'];
    let i = -1;
    do { n /= 1024; i++; } while (n >= 1024 && i < u.length - 1);
    return (n >= 100 ? n.toFixed(0) : n >= 10 ? n.toFixed(1) : n.toFixed(2)) + ' ' + u[i];
  }

  function ago(ms) {
    if (!ms) return '从未';
    const s = Math.max(0, (Date.now() - ms) / 1000);
    if (s < 45) return '刚刚';
    if (s < 3600) return Math.round(s / 60) + ' 分钟前';
    if (s < 86400) return Math.round(s / 3600) + ' 小时前';
    if (s < 86400 * 7) return Math.round(s / 86400) + ' 天前';
    const d = new Date(ms);
    return d.getFullYear() + '-' + z(d.getMonth() + 1) + '-' + z(d.getDate());
  }

  function stamp(ms) {
    if (!ms) return '—';
    const d = new Date(ms);
    return d.getFullYear() + '-' + z(d.getMonth() + 1) + '-' + z(d.getDate()) + ' ' +
      z(d.getHours()) + ':' + z(d.getMinutes()) + ':' + z(d.getSeconds());
  }
  function z(n) { return n < 10 ? '0' + n : '' + n; }

  function debounce(fn, wait) {
    let t; return function () { const a = arguments, s = this; clearTimeout(t); t = setTimeout(() => fn.apply(s, a), wait); };
  }

  function qs(name, fallback) {
    const v = new URLSearchParams(location.search).get(name);
    return v == null ? fallback : v;
  }

  /* -------------------------------------------------------------- settings sync */
  const state = { settings: null, stats: null, schedule: null, lock: true, templates: [], version: '' };

  async function bootstrap() {
    const data = await api('/api/bootstrap');
    state.settings = data.settings || {};
    state.stats = data.stats || {};
    state.schedule = data.schedule || {};
    state.lock = (state.settings.lock_mode || 'on') === 'on';
    state.templates = data.templates || [];
    state.version = data.version || '';
    if (state.settings.theme) applyTheme(state.settings.theme, true);
    return data;
  }

  async function setSetting(patch) {
    const data = await api('/api/settings', { method: 'PUT', body: patch });
    state.settings = data.settings || state.settings;
    state.lock = (state.settings.lock_mode || 'on') === 'on';
    if (patch.theme) applyTheme(patch.theme, true);
    return data;
  }

  /* --------------------------------------------------------------- nav shell */
  function renderTopbar(active, actionsHtml) {
    const links = [
      { id: 'home', href: '/', label: '导图管理', ico: 'folder' },
      { id: 'settings', href: '/settings', label: '设置', ico: 'gear' },
    ];
    const nav = links.map((l) =>
      '<a class="nav__link" href="' + l.href + '"' + (l.id === active ? ' aria-current="page"' : '') + '>' +
      icon(l.ico) + '<span>' + l.label + '</span></a>').join('');

    const themeButtons = ['dark', 'light', 'auto'].map((t) =>
      '<button class="seg__btn' + (themeSetting === t ? ' is-on' : '') + '" data-theme-set="' + t + '" ' +
      'title="' + ({ dark: '深色', light: '浅色', auto: '跟随系统' })[t] + '">' +
      icon({ dark: 'moon', light: 'sun', auto: 'monitor' }[t]) + '</button>'
    ).join('');

    return '<header class="topbar">' +
      '<a class="brand" href="/" title="YAML Mind 首页">' +
      '<span class="brand__mark">' +
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.1" stroke-linecap="round" stroke-linejoin="round">' +
      '<circle cx="12" cy="12" r="2.5"/><circle cx="4.6" cy="6.2" r="1.9"/><circle cx="19.4" cy="6.2" r="1.9"/>' +
      '<circle cx="4.6" cy="17.8" r="1.9"/><circle cx="19.4" cy="17.8" r="1.9"/>' +
      '<path d="M6.4 7.3 9.8 10.4M17.6 7.3 14.2 10.4M6.4 16.7 9.8 13.6M17.6 16.7 14.2 13.6"/></svg>' +
      '</span>' +
      '<span class="brand__text"><span class="brand__name">YAML Mind</span>' +
      '<span class="brand__sub">yaml → mind map</span></span></a>' +
      '<nav class="nav">' + nav + '</nav>' +
      '<div class="topbar__spacer"></div>' +
      '<div class="topbar__actions">' +
      (actionsHtml || '') +
      '<div class="seg" role="group" aria-label="主题">' + themeButtons + '</div>' +
      '</div></header>';
  }

  function bindThemeButtons(root) {
    root.querySelectorAll('[data-theme-set]').forEach((b) => {
      b.addEventListener('click', async () => {
        const t = b.dataset.themeSet;
        applyTheme(t, true);
        root.querySelectorAll('[data-theme-set]').forEach((x) => x.classList.toggle('is-on', x === b));
        try { await setSetting({ theme: t }); } catch (e) { /* offline tolerance */ }
      });
    });
  }

  function mountTopbar(active, actionsHtml) {
    const host = document.getElementById('top');
    if (host) {
      host.innerHTML = renderTopbar(active, actionsHtml);
      bindThemeButtons(host);
      return host.firstElementChild;
    }
    const shell = document.createElement('div');
    shell.innerHTML = renderTopbar(active, actionsHtml);
    document.body.insertBefore(shell.firstElementChild, document.body.firstElementChild);
    const bar = document.body.firstElementChild;
    bindThemeButtons(bar);
    return bar;
  }

  global.YM = {
    icon, api, ApiError, toast, modal, confirmDialog, esc, bytes, ago, stamp,
    debounce, qs, state, bootstrap, setSetting, applyTheme, resolve,
    theme: () => themeSetting, mountTopbar, bindThemeButtons, renderTopbar,
  };
})(window);
