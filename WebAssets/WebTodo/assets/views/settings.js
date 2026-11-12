/* #/settings —— 通用设置（外观主题、默认优先级、NTP 校时、只读服务器信息） */
import { api, reportError } from '../api.js';
import { store, defaultSettings, normalizeSettings, patchSettingsGroup, refreshSettings, refreshState } from '../store.js';
import { setTheme, getMode, labelOf } from '../theme.js';
import { track } from '../track.js';
import { esc, fmtFull, fmtNumber, fmtAgo, toInt } from '../format.js';
import { icon } from '../icons.js';
import { toastOk } from '../toast.js';
import { delegate, stateBlock, offlineStateBlock, selectHtml, PRIORITIES, priorityLabel } from '../ui.js';

export const meta = { id: 'settings', title: '设置', hash: '#/settings' };

export function render(root) {
  let disposed = false;
  const cleanups = [];
  let settings = normalizeSettings(store.settings || defaultSettings());
  let loading = !store.settings;
  let error = null;
  // 手动校时的行内结果：paint() 会整块重绘，所以必须存在闭包里而不是 DOM 上。
  const outputs = {};
  const outHtml = (key) => outputs[key] || '';

  root.innerHTML = `
    <div class="page page-pad">
      <div class="page-head">
        <div>
          <h2>通用设置</h2>
          <p>外观主题、默认优先级与 NTP 校时。改动即保存（PATCH /api/settings）；服务器地址为只读展示。</p>
        </div>
        <div class="card-actions">
          <button class="btn btn-ghost btn-sm" type="button" data-act="reload">${icon('refresh', { cls: 'ico-sm' })}重新读取</button>
        </div>
      </div>
      <div data-slot="body"></div>
    </div>`;

  const host = root.querySelector('[data-slot="body"]');

  /* ---------------- 渲染 ---------------- */

  function paint() {
    if (loading) {
      host.innerHTML = `<div class="card"><div class="card-body">${stateBlock({ iconName: 'sliders', title: '读取设置…', text: 'GET /api/settings' })}</div></div>`;
      return;
    }
    if (error) {
      host.innerHTML = error === 'offline'
        ? offlineStateBlock()
        : `<div class="card">${stateBlock({ iconName: 'alert', variant: 'err', title: '设置读取失败', text: esc(error), actions: ['<button class="btn btn-primary" type="button" data-act="reload">重试</button>'] })}</div>`;
      return;
    }
    const s = settings;
    const state = store.state || {};
    const app = state.app || {};
    const clock = state.clock || {};

    host.innerHTML = `
      <div class="form-grid-2">
        ${themeCard()}
        ${clockCard(clock, s.clock, app)}
        ${defaultsCard(s)}
        ${serverCard(s)}
      </div>`;
  }

  function themeCard() {
    return `<div class="card">
      <div class="card-head"><span class="card-title">${icon('moon', { cls: 'ico-sm' })}主题</span>
        <span class="card-sub">当前：${esc(labelOf(getMode()))}</span></div>
      <div class="card-body">
        <div class="segmented" role="group" aria-label="主题模式" data-slot="theme-group">
          ${['dark', 'light', 'system'].map((m) => `<button type="button" data-act="theme" data-v="${m}" aria-pressed="${String(getMode() === m)}">${icon(m === 'dark' ? 'moon' : (m === 'light' ? 'sun' : 'monitor'), { cls: 'ico-sm' })}${m === 'dark' ? '深色' : (m === 'light' ? '浅色' : '跟随系统')}</button>`).join('')}
        </div>
        <p class="field-hint">「跟随系统」实时监听 <code>prefers-color-scheme</code>；选择会同时保存到后端 settings.theme.mode 与 localStorage（键 <code>todo.theme</code>），首屏不会再闪白。</p>
      </div>
    </div>`;
  }

  function clockCard(clock, cfg, app) {
    const has = clock && Object.keys(clock).length;
    const synced = !!(clock && clock.synced);
    const offset = clock && Number.isFinite(Number(clock.offsetSeconds)) ? Number(clock.offsetSeconds) : null;
    return `<div class="card">
      <div class="card-head"><span class="card-title">${icon('clock', { cls: 'ico-sm' })}时间校时（NTP）</span>
        <span class="card-sub">${has ? (synced ? `已校准 · ${esc(clock.current || '')}` : '尚未校时') : '状态读取中'}</span></div>
      <div class="card-body">
        ${switchRow('clock-useNtp', '使用 NTP 校时', '默认开启：顶栏时钟与所有判定都用校后的时间，不再信任操作系统时钟。关闭后立即退回本机时间。', cfg.useNtp !== false)}
        <dl class="kv">
          <dt>当前显示</dt><dd>${esc(fmtFull(app.serverTime))} <span class="cell-muted">${synced ? 'NTP 校时时间' : '未经校时的服务器时间'}</span></dd>
          <dt>本机时间</dt><dd>${esc(fmtFull(clock && clock.localTime))}</dd>
          <dt>偏移</dt><dd>${offset === null ? '—' : `${offset >= 0 ? '+' : ''}${offset.toFixed(3)} s`}
            <span class="cell-muted">${clock && clock.roundTripMs ? `往返 ${clock.roundTripMs} ms` : ''}</span></dd>
          <dt>上次校时</dt><dd>${esc(fmtFull(clock && clock.lastSyncAt, '尚未'))} <span class="cell-muted">${esc(fmtAgo(clock && clock.lastSyncAt) || '')}</span></dd>
          <dt>下次校时</dt><dd>${esc(fmtFull(clock && clock.nextSyncAt, '启动后立即'))} <span class="cell-muted">每 ${fmtNumber((clock && clock.intervalSeconds) || 1800)} 秒</span></dd>
          <dt>服务器</dt><dd>${(clock && clock.servers || []).map((ip) => `<code>${esc(ip)}</code>`).join(' → ') || '—'}</dd>
          <dt>成功 / 失败</dt><dd>${fmtNumber((clock && clock.syncCount) || 0)} / ${fmtNumber((clock && clock.failCount) || 0)}</dd>
        </dl>
        <div class="toolbar">
          <button class="btn btn-ghost btn-sm" type="button" data-act="clock-sync" data-track="ui.clock.sync">
            ${icon('refresh', { cls: 'ico-sm' })}立即校时一次</button>
          <span class="field-hint">依次尝试主要 → 备用 1 → 备用 2，取往返延迟最小的样本。</span>
        </div>
        ${clock && clock.lastError ? `<div class="note-box is-err">${icon('alert', { cls: 'ico-sm' })}<div>最近一次校时失败：${esc(clock.lastError)}</div></div>` : ''}
        <div data-slot="clock-out">${outHtml('clock-out')}</div>
      </div>
    </div>`;
  }

  function defaultsCard(s) {
    return `<div class="card">
      <div class="card-head"><span class="card-title">${icon('tasks', { cls: 'ico-sm' })}任务默认值</span></div>
      <div class="card-body">
        <div class="field"><label class="field-label" for="s-prio">新建任务默认优先级</label>
          ${selectHtml('s-prio', PRIORITIES.map((p) => ({ value: p.value, label: `${p.value} · ${p.label}` })), s.defaults.priority, 'data-slot="priority" id="s-prio"')}</div>
      </div>
    </div>`;
  }

  function serverCard(s) {
    return `<div class="card">
      <div class="card-head"><span class="card-title">${icon('database', { cls: 'ico-sm' })}服务器（只读）</span></div>
      <div class="card-body">
        <dl class="kv">
          <dt>监听地址</dt><dd><code>${esc(s.server.host || '—')}</code></dd>
          <dt>端口</dt><dd><code>${esc(s.server.port === undefined ? '—' : s.server.port)}</code></dd>
        </dl>
        <div class="toolbar">
          <span class="field-hint">host / port 由 <code>start.bat</code> 的环境变量决定，本页不提供修改。</span>
        </div>
      </div>
    </div>`;
  }

  function switchRow(slot, title, hint, checked) {
    return `<label class="switch-row"><span class="sr-text"><span class="sr-title">${esc(title)}</span>
      <span class="sr-hint">${esc(hint)}</span></span>
      <span class="switch"><input type="checkbox" data-slot="${slot}" ${checked ? 'checked' : ''}>
      <span class="switch-track" aria-hidden="true"></span></span></label>`;
  }

  /* ---------------- 保存 ---------------- */

  async function saveGroup(group, patch, summary) {
    const nextGroup = { ...settings[group], ...patch };
    const previous = { ...settings[group] };
    settings = { ...settings, [group]: nextGroup };
    paint();
    try {
      await patchSettingsGroup(group, nextGroup);
      track('settings.update', {
        targetType: 'settings',
        summary: summary || `更新设置 ${group}`,
        detail: { group, before: previous, after: nextGroup }
      });
      toastOk('设置已保存');
      return true;
    } catch (err) {
      settings = { ...settings, [group]: previous };
      paint();
      reportError(err, '设置保存失败，已回滚');
      return false;
    }
  }

  async function syncClock() {
    const btn = root.querySelector('[data-act="clock-sync"]');
    if (btn) { btn.disabled = true; btn.innerHTML = `${icon('refresh', { cls: 'ico-sm' })}校时中`; }
    try {
      const data = await api.clockSync();
      if (disposed) return;
      outputs['clock-out'] = `<div class="note-box is-ok">${icon('check', { cls: 'ico-sm' })}<div>校时成功：来源 <b>${esc(data.current || '—')}</b>，
          偏移 <b>${esc(data.offsetSeconds)}</b> 秒，往返 ${esc(data.roundTripMs)} ms。</div></div>`;
      toastOk('NTP 校时完成');
      track('clock.sync', { targetType: 'settings', summary: `手动 NTP 校时：${data.current}，偏移 ${data.offsetSeconds}s`, detail: data });
      await refreshStateQuietly();
    } catch (err) {
      outputs['clock-out'] = `<div class="note-box is-err">${icon('alert', { cls: 'ico-sm' })}<div>${esc(err && err.message ? err.message : '校时失败')}，沿用上次偏移。</div></div>`;
      reportError(err, '校时失败');
      refreshStateQuietly();
    } finally {
      if (btn && !disposed) { btn.disabled = false; btn.innerHTML = `${icon('refresh', { cls: 'ico-sm' })}立即校时一次`; }
    }
  }

  async function refreshStateQuietly() {
    try {
      const data = await refreshState();
      if (disposed) return;
      paint();
      return data;
    } catch { /* 顶栏负责离线提示 */ }
    return null;
  }

  /* ---------------- 事件 ---------------- */

  const offClick = delegate(root, {
    theme: async (el) => {
      const mode = el.dataset.v;
      host.querySelectorAll('[data-act="theme"]').forEach((b) => b.setAttribute('aria-pressed', String(b === el)));
      await setTheme(mode, { source: 'settings' });
      settings = normalizeSettings({ ...settings, theme: { mode } });
      paint();
    },
    reload: async () => {
      loading = true;
      error = null;
      paint();
      try {
        const data = await refreshSettings();
        if (disposed) return;
        settings = data || normalizeSettings(defaultSettings());
        loading = false;
      } catch (err) {
        if (disposed) return;
        error = err && err.isNetwork ? 'offline' : (err && err.message) || '读取失败';
        loading = false;
      }
      paint();
      refreshStateQuietly();
    },
    'clock-sync': () => syncClock()
  });
  cleanups.push(offClick);

  function onChange(event) {
    const t = event.target;
    if (!t || !t.matches) return;
    const slot = t.dataset ? t.dataset.slot : null;
    if (!slot) return;
    switch (slot) {
      case 'priority':
        saveGroup('defaults', { priority: toInt(t.value, 0) }, `默认优先级改为${priorityLabel(toInt(t.value, 0))}`);
        break;
      case 'clock-useNtp':
        saveGroup('clock', { useNtp: !!t.checked }, `NTP 校时${t.checked ? '开启' : '关闭，改用本机时间'}`).then(() => refreshStateQuietly());
        break;
      default:
        break;
    }
  }

  root.addEventListener('change', onChange);

  async function boot() {
    if (!store.settings) {
      loading = true;
      paint();
      try {
        const data = await refreshSettings();
        if (disposed) return;
        settings = data || normalizeSettings(defaultSettings());
        loading = false;
      } catch (err) {
        if (disposed) return;
        error = err && err.isNetwork ? 'offline' : (err && err.message) || '读取失败';
        loading = false;
      }
    } else {
      settings = normalizeSettings(store.settings);
      loading = false;
    }
    if (!disposed) paint();
  }

  boot();

  return function cleanup() {
    disposed = true;
    cleanups.reverse().forEach((fn) => { try { fn(); } catch { /* ignore */ } });
  };
}
