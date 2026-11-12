/* 应用入口：hash 路由 + 外壳（顶部导航 / NTP 时钟 / 状态轮询）。 */
import { api } from './api.js';
import { store, on, refreshState, refreshSettings } from './store.js';
import { initTheme, cycleTheme } from './theme.js';
import { installGlobalTrack, track } from './track.js';
import { toastErr, toastOk } from './toast.js';
import { esc, fmtClockDate, fmtClockTime, fmtNumber, fmtAgo, parseDate } from './format.js';
import { icon } from './icons.js';
import { modalOpen } from './modal.js';
import * as tasksView from './views/tasks.js';
import * as dataView from './views/data.js';
import * as settingsView from './views/settings.js';

const VIEWS = {
  tasks: tasksView,
  data: dataView,
  settings: settingsView
};

const ROUTE_BY_ID = {
  tasks: '#/tasks',
  data: '#/data',
  settings: '#/settings'
};

const ID_BY_ROUTE = Object.entries(ROUTE_BY_ID).reduce((acc, [id, hash]) => {
  acc[hash.replace('#/', '')] = id;
  return acc;
}, {});

/* 顶栏快捷键顺序（数字键 1..3）。 */
const NAV_ORDER = ['tasks', 'data', 'settings'];

const els = {};
let currentId = null;
let currentCleanup = null;
let lastTrackedView = '';

/* ---------------- 路由 ---------------- */

function parseHash() {
  const raw = (location.hash || '#/tasks').replace(/^#/, '');
  const [pathPart, queryPart] = raw.split('?');
  const routeKey = pathPart.replace(/^\/*/, '').replace(/\/*$/, '') || 'tasks';
  const id = ID_BY_ROUTE[routeKey] || 'tasks';
  const params = {};
  if (queryPart) {
    new URLSearchParams(queryPart).forEach((value, key) => { params[key] = value; });
  }
  return { id, params, routeKey };
}

async function navigate() {
  const { id, params } = parseHash();
  if (currentCleanup) {
    try { currentCleanup(); } catch (err) { console.error('视图清理失败', err); }
    currentCleanup = null;
  }
  currentId = id;
  const view = VIEWS[id] || VIEWS.tasks;

  document.title = `${(view.meta && view.meta.title) || '任务'} · Todo Push`;

  els.topnav.querySelectorAll('.topnav-item').forEach((a) => {
    if (a.dataset.route === id) a.setAttribute('aria-current', 'page');
    else a.removeAttribute('aria-current');
  });

  document.body.classList.remove('projects-open');
  els.boot.hidden = true;
  els.view.hidden = false;
  els.view.innerHTML = '';

  try {
    const maybeCleanup = await view.render(els.view, params);
    if (typeof maybeCleanup === 'function') currentCleanup = maybeCleanup;
  } catch (err) {
    console.error(err);
    els.view.innerHTML = `<div class="page"><div class="card"><div class="card-body">
      <div class="state err"><div class="state-ico">${icon('alert')}</div>
      <h3>这个视图出错了</h3><p>${esc(err && err.message ? err.message : String(err))}</p>
      <div class="state-actions"><button class="btn btn-primary" type="button" data-act-reload="1">重新载入</button></div></div>
    </div></div></div>`;
    const btn = els.view.querySelector('[data-act-reload]');
    if (btn) btn.addEventListener('click', () => navigate());
  }

  const key = `${id}:${JSON.stringify(params)}`;
  if (key !== lastTrackedView) {
    lastTrackedView = key;
    track('ui.view.switch', {
      targetType: 'session',
      summary: `切换到${(view.meta && view.meta.title) || '任务'}`,
      detail: { view: id, params }
    });
  }
}

/* ---------------- 外壳：NTP 时钟 ---------------- */

/* 顶栏时钟不用浏览器本地时间：以 /api/state 的 serverTime（已叠加 NTP 偏移）为锚点，
   再按本机单调时间走秒，两次轮询之间不会跳变。 */
let clockAnchor = null;

function anchorClock(state) {
  const app = (state && state.app) || {};
  const base = parseDate(app.serverTime);
  clockAnchor = base ? { ms: base.getTime(), local: Date.now(), state } : null;
  paintClock();
}

function clockNow() {
  if (!clockAnchor) return new Date();
  return new Date(clockAnchor.ms + (Date.now() - clockAnchor.local));
}

function paintClock() {
  if (!els.clockTime) return;
  const now = clockNow();
  const clock = (clockAnchor && clockAnchor.state && clockAnchor.state.clock) || null;
  const appInfo = (clockAnchor && clockAnchor.state && clockAnchor.state.app) || {};
  const synced = !!(clock && clock.synced) || !!appInfo.clockSynced;

  els.clockSrc.textContent = synced ? 'NTP' : (clock && clock.useNtp === false ? '本机' : '待校时');
  els.clock.dataset.status = synced ? 'synced' : (clock ? 'pending' : 'offline');
  els.clockDate.textContent = fmtClockDate(now);
  els.clockTime.textContent = fmtClockTime(now);
  els.clock.title = synced
    ? `NTP 校时时间 · 偏移 ${(clock.offsetSeconds >= 0 ? '+' : '')}${clock.offsetSeconds}s\n`
      + `来源 ${clock.current || '—'} · 上次校时 ${fmtAgo(clock.lastSyncAt) || '—'} · 下次 ${fmtAgo(clock.nextSyncAt) || '—'}`
    : '尚未完成 NTP 校时，显示的是服务器本机时间。可在设置页手动校时。';
}

function paintState(state) {
  if (!state || typeof state !== 'object') return;
  const counts = state.counts || {};

  const setBadge = (key, value) => {
    const el = els.topnav.querySelector(`[data-badge="${key}"]`);
    if (!el) return;
    const n = Number(value);
    el.textContent = Number.isFinite(n) && n > 0 ? fmtNumber(n) : '';
  };
  setBadge('openTasks', counts.openTasks);

  anchorClock(state);
}

function setOffline(reason) {
  if (els.banner.hidden) els.banner.hidden = false;
  els.offlineDetail.textContent = reason || '/api/state 请求失败，界面处于离线模式。';
}

function setOnline() {
  els.banner.hidden = true;
}

async function pollState(initial) {
  try {
    const data = await refreshState();
    paintState(data);
    if (initial) {
      // 首屏主题由 localStorage 决定；后端 settings 只是预先取回供设置页展示。
      refreshSettings().catch(() => { /* 离线横幅已提示 */ });
    }
    return data;
  } catch (err) {
    setOffline(err && err.message);
    if (!store.offlineReported && !initial) {
      store.offlineReported = true;
      toastErr('后端连接中断，已进入离线模式');
    }
    return null;
  } finally {
    if (store.online) store.offlineReported = false;
  }
}

/* ---------------- 启动 ---------------- */

function cacheEls() {
  els.app = document.getElementById('app');
  els.topnav = document.getElementById('topnav');
  els.view = document.getElementById('view');
  els.boot = document.getElementById('bootState');
  els.clock = document.getElementById('clock');
  els.clockSrc = document.getElementById('clockSrc');
  els.clockDate = els.clock.querySelector('.clock-date');
  els.clockTime = els.clock.querySelector('.clock-time');
  els.banner = document.getElementById('offlineBanner');
  els.offlineDetail = document.getElementById('offlineDetail');
  els.themeToggle = document.getElementById('themeToggle');
  els.retry = document.getElementById('offlineRetry');
  els.scrim = document.getElementById('scrim');
}

function wireShell() {
  els.themeToggle.addEventListener('click', () => { cycleTheme(); });

  els.retry.addEventListener('click', async () => {
    els.retry.disabled = true;
    els.retry.textContent = '连接中…';
    const data = await pollState(true);
    els.retry.disabled = false;
    els.retry.textContent = '重试';
    if (data) toastOkShort('已重新连接后端');
  });

  window.addEventListener('app:retry-state', async () => {
    const data = await pollState(true);
    if (data) navigate();
  });

  // 时钟点击前往设置页。
  els.clock.style.cursor = 'pointer';
  els.clock.addEventListener('click', () => { location.hash = '#/settings'; });

  els.scrim.addEventListener('click', () => {
    document.body.classList.remove('projects-open');
  });

  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    if (modalOpen()) return;
    if (document.body.classList.contains('projects-open')) {
      document.body.classList.remove('projects-open');
    }
  });

  // 顶部导航快捷键：1..3
  document.addEventListener('keydown', (e) => {
    if (e.metaKey || e.ctrlKey || e.altKey || e.defaultPrevented) return;
    const t = e.target;
    const tag = (t && t.tagName) || '';
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || (t && t.isContentEditable)) return;
    if (modalOpen()) return;
    const idx = ['1', '2', '3'].indexOf(e.key);
    if (idx >= 0 && NAV_ORDER[idx] !== currentId) {
      e.preventDefault();
      location.hash = ROUTE_BY_ID[NAV_ORDER[idx]];
    }
  });
}

function toastOkShort(msg) {
  toastOk(msg, { ms: 2400 });
}

async function boot() {
  cacheEls();
  initTheme();
  installGlobalTrack();
  wireShell();
  paintClock();
  setInterval(paintClock, 1000);

  on('online', (ok) => { if (ok) setOnline(); });
  on('offline-detail', (msg) => setOffline(msg));
  on('state', (data) => paintState(data));

  window.addEventListener('hashchange', () => { navigate(); });

  await pollState(true);
  await navigate();

  // 状态轮询：导航徽标 / NTP 时钟锚点保持新鲜
  setInterval(() => { pollState(false); }, 20000);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') pollState(false);
  });
}

boot();
