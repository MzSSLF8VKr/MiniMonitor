/* 主题：默认 dark；system 模式实时跟随 prefers-color-scheme；同时持久化到 localStorage 与后端。 */
import { patchSettingsGroup, store } from './store.js';
import { track } from './track.js';
import { icon } from './icons.js';

const THEME_KEY = 'todo.theme'; // 与 index.html 内联首屏脚本共用同一键名
const VALID = new Set(['dark', 'light', 'system']);

let mode = 'dark';
let mq = null;
let systemHandler = null;

export function getMode() { return mode; }

export function resolve(modeValue) {
  if (modeValue === 'system') {
    return (mq && mq.matches) ? 'dark' : 'light';
  }
  return modeValue === 'light' ? 'light' : 'dark';
}

function paint() {
  const resolved = resolve(mode);
  const el = document.documentElement;
  el.setAttribute('data-theme', resolved);
  el.setAttribute('data-theme-mode', mode);
  el.setAttribute('data-color-scheme', resolved);
  syncToggle();
  return resolved;
}

function syncToggle() {
  const btn = document.getElementById('themeToggle');
  if (!btn) return;
  const resolved = resolve(mode);
  const name = mode === 'system' ? 'monitor' : (resolved === 'dark' ? 'moon' : 'sun');
  btn.innerHTML = icon(name, { cls: 'ico theme-ico' });
  const label = mode === 'system' ? `主题：跟随系统（当前 ${resolved === 'dark' ? '深色' : '浅色'}），点击切换`
    : `主题：${resolved === 'dark' ? '深色' : '浅色'}，点击切换`;
  btn.setAttribute('aria-label', label);
  btn.title = label;
}

function readStored() {
  let saved = null;
  try { saved = window.localStorage.getItem(THEME_KEY); } catch { saved = null; }
  return VALID.has(saved) ? saved : 'dark';
}

function writeStored(value) {
  try { window.localStorage.setItem(THEME_KEY, value); } catch { /* 隐私模式忽略 */ }
}

/** 只做本地应用（不写后端）。 */
function applyLocal(next) {
  if (!VALID.has(next)) next = 'dark';
  mode = next;
  writeStored(mode);
  return paint();
}

/**
 * 首次访问（localStorage 里没有主题记录）时采纳后端 settings.theme.mode；
 * 本地已有选择则以 localStorage 为准，避免覆盖用户偏好。
 */
export function adoptBackendTheme(next) {
  let stored = null;
  try { stored = window.localStorage.getItem(THEME_KEY); } catch { stored = null; }
  if (stored) return resolve(mode);
  return applyLocal(next);
}

/** 用户主动切换：本地生效 + localStorage + PATCH /api/settings。 */
export async function setTheme(next, info = {}) {
  if (!VALID.has(next)) return resolve(mode);
  mode = next;
  paint();
  writeStored(mode);
  if (store.settings) store.settings.theme = { mode };
  track('settings.theme.set', {
    targetType: 'settings',
    summary: info.summary || `主题切换为${labelOf(mode)}`,
    detail: { mode, source: info.source || 'toggle' }
  });
  try {
    await patchSettingsGroup('theme', { mode });
  } catch { /* 离线时保持本地生效，由顶栏离线横幅提示 */ }
  return resolve(mode);
}

export function labelOf(value) {
  if (value === 'system') return '跟随系统';
  return value === 'light' ? '浅色' : '深色';
}

/** 顶栏按钮：深色 → 浅色 → 跟随系统 → 深色 */
export function cycleTheme() {
  const order = ['dark', 'light', 'system'];
  const next = order[(order.indexOf(mode) + 1) % order.length];
  return setTheme(next, { source: 'topbar' });
}

export function initTheme() {
  mode = readStored();
  if (window.matchMedia) {
    mq = window.matchMedia('(prefers-color-scheme: dark)');
    systemHandler = () => {
      if (mode === 'system') paint();
    };
    if (typeof mq.addEventListener === 'function') mq.addEventListener('change', systemHandler);
    else if (typeof mq.addListener === 'function') mq.addListener(systemHandler);
  }
  paint();
  return resolve(mode);
}

export function disposeThemeListener() {
  if (!mq || !systemHandler) return;
  if (typeof mq.removeEventListener === 'function') mq.removeEventListener('change', systemHandler);
  else if (typeof mq.removeListener === 'function') mq.removeListener(systemHandler);
  systemHandler = null;
}
