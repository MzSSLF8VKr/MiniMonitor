/* 全局共享状态：/api/state 与 /api/settings 的缓存 + 极简事件总线。 */
import { api } from './api.js';

export const store = {
  state: null,          // GET /api/state 的 data
  settings: null,       // GET /api/settings 的 data
  online: false,
  offlineReported: false
};

const listeners = new Map();

export function on(event, fn) {
  if (!listeners.has(event)) listeners.set(event, new Set());
  listeners.get(event).add(fn);
  return () => listeners.get(event).delete(fn);
}

export function emit(event, payload) {
  const set = listeners.get(event);
  if (!set) return;
  for (const fn of [...set]) {
    try { fn(payload); } catch (err) { console.error(`[store] listener ${event} 失败`, err); }
  }
}

/** 拉取概览状态；失败时把界面切到离线模式。 */
export async function refreshState() {
  try {
    const data = await api.getState();
    store.state = data;
    store.online = true;
    emit('online', true);
    emit('state', data);
    return data;
  } catch (err) {
    store.online = false;
    emit('online', false);
    emit('offline-detail', err && err.message ? err.message : String(err));
    throw err;
  }
}

export async function refreshSettings() {
  try {
    const data = await api.getSettings();
    store.settings = normalizeSettings(data);
    emit('settings', store.settings);
    return store.settings;
  } catch (err) {
    store.settings = store.settings || defaultSettings();
    emit('settings', store.settings);
    throw err;
  }
}

/** settings 各分组补默认值，避免后端字段缺失时读到 undefined。 */
export function normalizeSettings(raw) {
  const base = defaultSettings();
  const src = raw && typeof raw === 'object' ? raw : {};
  return {
    theme: { ...base.theme, ...(src.theme || {}) },
    defaults: { ...base.defaults, ...(src.defaults || {}) },
    clock: { ...base.clock, ...(src.clock || {}) },
    server: { ...base.server, ...(src.server || {}) }
  };
}

export function defaultSettings() {
  return {
    theme: { mode: 'dark' },
    defaults: { priority: 0 },
    clock: { useNtp: true },
    server: { host: '127.0.0.1', port: 50004 }
  };
}

/**
 * 局部更新一组设置。始终发送「完整的嵌套分组」，
 * 后端无论是深合并还是整组替换都能得到正确结果。
 */
export async function patchSettingsGroup(group, value) {
  const patch = { [group]: value };
  const data = await api.patchSettings(patch);
  if (data && typeof data === 'object') {
    store.settings = normalizeSettings(data);
  } else if (store.settings) {
    store.settings = normalizeSettings({ ...store.settings, [group]: value });
  }
  emit('settings', store.settings);
  return store.settings;
}
