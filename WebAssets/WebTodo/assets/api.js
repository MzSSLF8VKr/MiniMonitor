/* 统一 API 层：所有路径与字段严格对应 docs/API.md。 */
import { toastErr } from './toast.js';

export class ApiError extends Error {
  constructor(message, info = {}) {
    super(message);
    this.name = 'ApiError';
    this.code = info.code || null;
    this.status = info.status || 0;
    this.kind = info.kind || 'api'; // 'api' | 'network' | 'parse' | 'http'
  }
  get isNetwork() { return this.kind === 'network'; }
}

/** 拼 query string，自动丢弃 null / undefined / 空串。 */
export function qs(params) {
  const search = new URLSearchParams();
  Object.entries(params || {}).forEach(([k, v]) => {
    if (v === null || v === undefined) return;
    const s = String(v).trim();
    if (s === '') return;
    search.set(k, s);
  });
  const out = search.toString();
  return out ? `?${out}` : '';
}

async function request(path, { method = 'GET', body, signal } = {}) {
  const init = { method, signal, headers: {}, credentials: 'same-origin', cache: 'no-store' };
  if (body !== undefined) {
    init.headers['Content-Type'] = 'application/json; charset=utf-8';
    init.body = typeof body === 'string' ? body : JSON.stringify(body);
  }
  let res;
  try {
    res = await fetch(path, init);
  } catch (err) {
    if (err && err.name === 'AbortError') throw err;
    throw new ApiError('无法连接后端，请确认已运行 start.bat / start.sh 且端口可访问。', { kind: 'network', status: 0 });
  }

  const text = await res.text();
  let payload = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      payload = null;
    }
  }

  if (payload && typeof payload === 'object' && 'ok' in payload) {
    if (!payload.ok) {
      throw new ApiError(payload.error || `请求失败（HTTP ${res.status}）`, {
        code: payload.code || null,
        status: res.status
      });
    }
    return payload.data;
  }

  if (!res.ok) {
    throw new ApiError(text ? text.slice(0, 200) : `HTTP ${res.status} ${res.statusText || ''}`.trim(), {
      kind: 'http', status: res.status
    });
  }
  if (payload === null && text) {
    throw new ApiError('响应不是合法 JSON', { kind: 'parse', status: res.status });
  }
  return payload;
}

/**
 * 核心请求函数。
 * @returns {Promise<any>} 解析出的 data；失败抛 ApiError（error.message 可直接展示）。
 */
export async function apiFetch(path, options = {}) {
  return request(path, options);
}

/** 下载附件型接口（/api/export/*）：优先使用后端 Content-Disposition 里的文件名。 */
export async function apiDownload(path, fallbackName) {
  let res;
  try {
    res = await fetch(path, { method: 'GET', credentials: 'same-origin', cache: 'no-store' });
  } catch {
    throw new ApiError('无法连接后端，导出失败。', { kind: 'network' });
  }
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try {
      const j = await res.json();
      if (j && j.error) msg = j.error;
    } catch { /* 忽略非 JSON 错误体 */ }
    throw new ApiError(msg, { status: res.status });
  }
  const blob = await res.blob();
  const disp = res.headers.get('Content-Disposition') || '';
  let name = fallbackName;
  const m = /filename\*?=\s?"?([^";]+)"?/i.exec(disp);
  if (m && m[1]) name = m[1];
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 4000);
  return { filename: name, size: blob.size };
}

/* ---------------- 各端点封装 ---------------- */

export const api = {
  /* 状态 / 概览 */
  getState: () => apiFetch('/api/state'),

  /* 项目 */
  listProjects: ({ includeArchived = false, q = '' } = {}) =>
    apiFetch(`/api/projects${qs({ includeArchived: includeArchived ? 1 : 0, q })}`),
  getProject: (id) => apiFetch(`/api/projects/${encodeURIComponent(id)}`),
  createProject: (body) => apiFetch('/api/projects', { method: 'POST', body }),
  updateProject: (id, patch) => apiFetch(`/api/projects/${encodeURIComponent(id)}`, { method: 'PATCH', body: patch }),
  deleteProject: (id, cascade = false) =>
    apiFetch(`/api/projects/${encodeURIComponent(id)}${qs({ cascade: cascade ? 1 : 0 })}`, { method: 'DELETE' }),
  reorderProjects: (ids) => apiFetch('/api/projects/reorder', { method: 'POST', body: { ids } }),

  /* 任务 */
  listTasks: ({ projectId = '', status = 'open', view = 'all', q = '', limit = 500, offset = 0 } = {}) =>
    apiFetch(`/api/tasks${qs({ projectId, status, view, q, limit, offset })}`),
  getTask: (id) => apiFetch(`/api/tasks/${encodeURIComponent(id)}`),
  createTask: (body) => apiFetch('/api/tasks', { method: 'POST', body }),
  updateTask: (id, patch) => apiFetch(`/api/tasks/${encodeURIComponent(id)}`, { method: 'PATCH', body: patch }),
  completeTask: (id) => apiFetch(`/api/tasks/${encodeURIComponent(id)}/complete`, { method: 'POST', body: {} }),
  reopenTask: (id) => apiFetch(`/api/tasks/${encodeURIComponent(id)}/reopen`, { method: 'POST', body: {} }),
  deleteTask: (id) => apiFetch(`/api/tasks/${encodeURIComponent(id)}`, { method: 'DELETE' }),
  batchTasks: ({ action, ids, projectId = null }) =>
    apiFetch('/api/tasks/batch', { method: 'POST', body: { action, ids, projectId } }),
  reorderTasks: (projectId, ids) =>
    apiFetch('/api/tasks/reorder', { method: 'POST', body: { projectId, ids } }),

  /* 校时 */
  clockStatus: () => apiFetch('/api/clock'),
  clockSync: () => apiFetch('/api/clock/sync', { method: 'POST', body: {} }),

  /* 设置 */
  getSettings: () => apiFetch('/api/settings'),
  patchSettings: (patch) => apiFetch('/api/settings', { method: 'PATCH', body: patch }),

  /* 数据导入导出 */
  exportFull: () => apiDownload('/api/export/full', `todo-full-${stamp()}.json`),
  exportTodo: () => apiDownload('/api/export/todo', `todo-data-${stamp()}.json`),
  exportPreview: (kind = 'full') => apiFetch(`/api/export/preview${qs({ kind })}`),
  importFull: (mode, payload) => apiFetch('/api/import/full', { method: 'POST', body: { mode, payload } }),
  importTodo: (mode, payload) => apiFetch('/api/import/todo', { method: 'POST', body: { mode, payload } })
};

function stamp() {
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}`;
}

/** 统一的失败出口：toast + 返回可读文本。 */
export function reportError(err, prefix = '') {
  const msg = err && err.message ? err.message : String(err);
  toastErr(prefix ? `${prefix}：${msg}` : msg);
  return msg;
}
