/* #/data —— 导入导出四件套 */
import { api, reportError } from '../api.js';
import { store } from '../store.js';
import { track } from '../track.js';
import { esc, fmtBytes, fmtNumber, fmtFull, safeJson, prettyJson } from '../format.js';
import { icon } from '../icons.js';
import { typedConfirmModal } from '../modal.js';
import { toastOk, toastWarn } from '../toast.js';
import { delegate, debounce, selectHtml, stateBlock, offlineStateBlock } from '../ui.js';

export const meta = { id: 'data', title: '数据', hash: '#/data' };

export function render(root) {
  let disposed = false;
  const cleanups = [];
  const state = {
    previewKind: 'full',
    preview: null,
    previewError: null,
    previewLoading: false,
    previewAt: null,
    importTarget: 'todo',       // 'full' | 'todo'
    mode: 'merge',              // 'merge' | 'replace'
    source: '',                 // 'file' | 'paste' 由内容是否存在推断
    fileName: '',
    payloadText: '',
    submitting: false,
    result: null,
    resultTitle: ''
  };

  root.innerHTML = `
    <div class="page page-pad">
      <div class="page-head">
        <div>
          <h2>数据导入 / 导出</h2>
          <p>四个入口与 <code>docs/API.md</code> 一一对应；<b>replace</b> 会清空现有数据，需要手动输入关键字确认。</p>
        </div>
      </div>

      <div class="form-grid-2">
        <div class="card">
          <div class="card-head"><span class="card-title">${icon('download', { cls: 'ico-sm' })}导出</span>
            <span class="card-sub">GET /api/export/*</span></div>
          <div class="card-body">
            <div class="note-box">${icon('info', { cls: 'ico-sm' })}<div>
              <b>全量导出</b>包含配置 + 项目 + 任务 + 统计，可用于异地重建，请妥善保管下载的文件。
              <br><b>仅任务</b>只含项目与任务。
            </div></div>
            <div style="display:flex;gap:10px;flex-wrap:wrap">
              <button class="btn btn-primary" type="button" data-act="export-full" style="flex:1 1 200px">
                ${icon('database', { cls: 'ico-sm' })}全量导出</button>
              <button class="btn" type="button" data-act="export-todo" style="flex:1 1 200px">
                ${icon('tasks', { cls: 'ico-sm' })}导出项目与任务</button>
            </div>
            <div class="divider"></div>
            <div class="field-row" style="align-items:flex-end">
              <div class="field" style="flex:1 1 160px"><label class="field-label" for="pv-kind">不落盘预览</label>
                ${selectHtml('pv-kind', [{ value: 'full', label: '全量 (kind=full)' }, { value: 'todo', label: '仅任务 (kind=todo)' }], state.previewKind, 'data-slot="preview-kind" id="pv-kind"')}</div>
              <button class="btn btn-ghost" type="button" data-act="preview">${icon('eye', { cls: 'ico-sm' })}生成预览</button>
            </div>
            <div data-slot="preview-out"></div>
          </div>
        </div>

        <div class="card">
          <div class="card-head"><span class="card-title">${icon('upload', { cls: 'ico-sm' })}导入</span>
            <span class="card-sub">POST /api/import/*</span>
            <div class="card-actions"><span class="badge badge-no-dot badge-muted">payload 自动识别三种形态</span></div></div>
          <div class="card-body">
            <div class="field"><span class="field-label">导入目标</span>
              <div class="segmented" role="group" aria-label="导入目标">
                <button type="button" data-act="target" data-v="todo" aria-pressed="true">${icon('tasks', { cls: 'ico-sm' })}任务导入</button>
                <button type="button" data-act="target" data-v="full" aria-pressed="false">${icon('database', { cls: 'ico-sm' })}全量导入</button>
              </div>
              <p class="field-hint" data-slot="target-hint"></p></div>

            <div class="field"><span class="field-label">数据来源</span>
              <label class="drop-zone" data-slot="drop" tabindex="0">
                ${icon('fileJson', { cls: 'ico' })}
                <span><b>选择 JSON 文件</b> 或拖拽到此处</span>
                <span class="field-hint" data-slot="file-name">未选择文件；也可以直接在下方粘贴 JSON</span>
                <input type="file" accept="application/json,.json,text/plain" data-slot="file-input">
              </label></div>

            <div class="field"><label class="field-label" for="im-text">或直接粘贴 JSON</label>
              <textarea id="im-text" data-slot="paste" placeholder='{ "kind": "todo", "projects": [ { "name": "工作", "tasks": [ { "title": "写周报", "dueAt": "2026-10-21T16:45" } ] } ] }' style="min-height:120px;font-family:var(--font-mono);font-size:11.5px"></textarea>
              <p class="field-error" data-slot="import-err" hidden></p></div>

            <div class="field"><span class="field-label">导入模式</span>
              <div class="radio-row">
                <label class="radio-card is-active"><input type="radio" name="im-mode" value="merge" checked data-act="mode-radio">
                  <span><span class="rc-t">合并 merge</span><span class="rc-d">按名称 / ID 匹配，存在则更新，不存在则创建。</span></span></label>
                <label class="radio-card"><input type="radio" name="im-mode" value="replace" data-act="mode-radio">
                  <span><span class="rc-t">替换 replace</span><span class="rc-d">先清空现有项目与任务（全量导入还会覆盖配置）。</span></span></label>
              </div></div>

            <div data-slot="replace-warn" style="display:none">
              <div class="note-box is-err">${icon('alert', { cls: 'ico' })}<div><b>replace 是高危操作</b><br>
                现有项目、任务${state.importTarget === 'full' ? '与配置' : ''}都会被导入内容覆盖。提交前需要输入 <code>REPLACE</code> 确认。</div></div>
            </div>

            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
              <button class="btn btn-primary" type="button" data-act="submit" ${state.mode === 'replace' ? 'data-danger="1"' : ''}>
                ${icon('upload', { cls: 'ico-sm' })}<span data-slot="submit-text">开始导入</span></button>
              <button class="btn btn-ghost" type="button" data-act="clear-form">清空表单</button>
              <span class="field-hint" data-slot="submit-hint"></span>
            </div>
            <div data-slot="result-out"></div>
          </div>
        </div>
      </div>
    </div>`;

  const g = (slot) => root.querySelector(`[data-slot="${slot}"]`);

  /* ---------------- 导出 ---------------- */

  async function doExport(kind) {
    const btn = root.querySelector(`[data-act="export-${kind}"]`);
    if (btn) { btn.disabled = true; }
    try {
      const info = kind === 'full' ? await api.exportFull() : await api.exportTodo();
      track(kind === 'full' ? 'data.export.full' : 'data.export.todo', {
        targetType: 'data',
        summary: `${kind === 'full' ? '全量' : '仅任务'}导出 ${info.filename}（${fmtBytes(info.size)}）`,
        detail: { kind, filename: info.filename, bytes: info.size }
      });
      toastOk(`已下载 ${info.filename}`, { title: '导出完成' });
    } catch (err) {
      reportError(err, '导出失败');
    } finally {
      if (btn && !disposed) btn.disabled = false;
    }
  }

  /* ---------------- 预览 ---------------- */

  async function doPreview() {
    state.previewLoading = true;
    state.previewError = null;
    paintPreview();
    try {
      const data = await api.exportPreview(state.previewKind);
      if (disposed) return;
      state.preview = data;
      state.previewAt = new Date();
    } catch (err) {
      if (disposed) return;
      state.preview = null;
      state.previewError = err && err.isNetwork ? 'offline' : (err && err.message) || '预览失败';
    } finally {
      state.previewLoading = false;
      if (!disposed) paintPreview();
    }
  }

  function summarize(obj) {
    if (!obj || typeof obj !== 'object') return [];
    const out = [];
    const counts = obj.counts && typeof obj.counts === 'object' ? obj.counts : null;
    if (Array.isArray(obj.projects)) out.push({ k: '项目', v: obj.projects.length });
    if (Array.isArray(obj.tasks)) out.push({ k: '任务', v: obj.tasks.length });
    if (obj.config && typeof obj.config === 'object') out.push({ k: '配置项', v: Object.keys(obj.config).length });
    if (obj.settings && typeof obj.settings === 'object') out.push({ k: '设置', v: Object.keys(obj.settings).length });
    if (counts) Object.entries(counts).slice(0, 6).forEach(([k, v]) => out.push({ k: `统计 · ${k}`, v: fmtNumber(v) }));
    if (!out.length) out.push({ k: '顶层键', v: Object.keys(obj).length });
    return out.slice(0, 8);
  }

  function paintPreview() {
    const box = g('preview-out');
    if (!box) return;
    if (state.previewLoading) {
      box.innerHTML = stateBlock({ iconName: 'eye', title: '正在生成预览…', text: 'GET /api/export/preview?kind=' + esc(state.previewKind) });
      return;
    }
    if (state.previewError) {
      box.innerHTML = state.previewError === 'offline'
        ? offlineStateBlock()
        : stateBlock({ iconName: 'alert', variant: 'err', title: '预览失败', text: esc(state.previewError), actions: ['<button class="btn btn-ghost" type="button" data-act="preview">重试</button>'] });
      return;
    }
    if (!state.preview) {
      box.innerHTML = stateBlock({ iconName: 'eye', title: '还没有预览', text: '点「生成预览」会在后端内存里组装将要导出的对象，<b>不落盘、不下载</b>。' });
      return;
    }
    const json = JSON.stringify(state.preview, null, 2);
    const cells = summarize(state.preview);
    box.innerHTML = `<div class="divider"></div>
      <div class="summary-grid">${cells.map((c) => `<div class="summary-cell"><div class="sc-k">${esc(c.k)}</div><div class="sc-v">${esc(c.v)}</div></div>`).join('')}</div>
      <div class="toolbar"><span class="field-hint">约 ${fmtBytes(json.length)} · 生成于 ${esc(fmtFull(state.previewAt))}</span>
        <span style="flex:1"></span>
        <button class="btn btn-sm btn-ghost" type="button" data-act="copy-preview">${icon('copy', { cls: 'ico-sm' })}复制 JSON</button></div>
      <pre class="json-box">${prettyJson(state.preview)}</pre>`;
  }

  /* ---------------- 导入 ---------------- */

  function paintImport() {
    root.querySelectorAll('[data-act="target"]').forEach((b) => {
      b.setAttribute('aria-pressed', String(b.dataset.v === state.importTarget));
    });
    const hint = g('target-hint');
    if (hint) {
      hint.innerHTML = state.importTarget === 'full'
        ? 'POST /api/import/full —— 需要「全量导出」得到的文件；replace 会覆盖配置。'
        : 'POST /api/import/todo —— 接受 { kind:"todo", projects:[…]} / { tasks:[…] } / [ { name, tasks:[…] } ] 三种形态。';
    }
    root.querySelectorAll('input[name="im-mode"]').forEach((r) => {
      r.checked = r.value === state.mode;
      const card = r.closest('.radio-card');
      if (card) card.classList.toggle('is-active', r.checked);
    });
    const warn = g('replace-warn');
    if (warn) warn.style.display = state.mode === 'replace' ? 'block' : 'none';
    const submitText = g('submit-text');
    if (submitText) submitText.textContent = state.mode === 'replace' ? '替换导入（高危）' : '开始导入';
    const btn = root.querySelector('[data-act="submit"]');
    if (btn) btn.classList.toggle('btn-danger', state.mode === 'replace');
    const fileName = g('file-name');
    if (fileName) fileName.textContent = state.fileName ? `已读取文件：${state.fileName}` : '未选择文件；也可以直接在下方粘贴 JSON';
    const errBox = g('import-err');
    if (errBox && !errBox.hidden && errBox.textContent) errBox.hidden = false;
    g('submit-hint').textContent = state.submitting ? '提交中…' : '';
    if (btn) btn.disabled = state.submitting;
  }

  function currentPayload() {
    const text = state.payloadText;
    if (!text.trim()) return { error: '请选择文件或粘贴 JSON 内容。' };
    const parsed = safeJson(text);
    if (!parsed.ok) return { error: `JSON 解析失败：${parsed.error}` };
    const value = parsed.value;
    if (value === null || typeof value !== 'object') return { error: 'JSON 顶层必须是对象或数组。' };
    return { payload: value };
  }

  async function submitImport() {
    const check = currentPayload();
    const errBox = g('import-err');
    if (check.error) {
      errBox.hidden = false;
      errBox.textContent = check.error;
      toastWarn(check.error);
      return;
    }
    errBox.hidden = true;

    if (state.mode === 'replace') {
      const ok = await typedConfirmModal({
        title: '确认以 replace 方式导入',
        message: `这会先清空现有的${state.importTarget === 'full' ? '配置、项目与任务' : '项目与任务'}，再写入导入内容。此操作不可撤销。`,
        expect: 'REPLACE',
        label: '输入 REPLACE 确认',
        confirmLabel: '执行替换导入'
      });
      if (!ok) return;
    }

    state.submitting = true;
    paintImport();
    try {
      const data = state.importTarget === 'full'
        ? await api.importFull(state.mode, check.payload)
        : await api.importTodo(state.mode, check.payload);
      state.result = data;
      state.resultTitle = state.importTarget === 'full' ? '全量导入结果' : '任务导入结果';
      track(state.importTarget === 'full' ? 'data.import.full' : 'data.import.todo', {
        targetType: 'data',
        summary: `${state.resultTitle}（${state.mode}）：${describeSummary(data)}`,
        detail: { mode: state.mode, target: state.importTarget, summary: data || null }
      });
      toastOk(describeSummary(data), { title: '导入完成' });
      paintResult();
    } catch (err) {
      errBox.hidden = false;
      errBox.textContent = err && err.message ? err.message : '导入失败';
      reportError(err, '导入失败');
    } finally {
      state.submitting = false;
      if (!disposed) paintImport();
    }
  }

  function describeSummary(data) {
    if (!data || typeof data !== 'object') return '后端未返回摘要';
    const p = data.projects || {};
    const t = data.tasks || {};
    const parts = [];
    if (p.created !== undefined) parts.push(`项目新建 ${p.created}`);
    if (p.updated !== undefined) parts.push(`更新 ${p.updated}`);
    if (t.created !== undefined) parts.push(`任务新建 ${t.created}`);
    if (t.updated !== undefined) parts.push(`更新 ${t.updated}`);
    if (t.skipped !== undefined) parts.push(`跳过 ${t.skipped}`);
    if (Array.isArray(data.warnings) && data.warnings.length) parts.push(`警告 ${data.warnings.length}`);
    return parts.length ? parts.join('，') : `模式 ${data.mode || state.mode}`;
  }

  function paintResult() {
    const box = g('result-out');
    if (!box) return;
    const d = state.result;
    if (!d) { box.innerHTML = ''; return; }
    const cells = [];
    const push = (k, v) => { if (v !== undefined && v !== null) cells.push({ k, v }); };
    const p = d.projects || {};
    const t = d.tasks || {};
    push('项目新建', p.created); push('项目更新', p.updated);
    push('任务新建', t.created); push('任务更新', t.updated); push('任务跳过', t.skipped);
    const warnings = Array.isArray(d.warnings) ? d.warnings : [];

    box.innerHTML = `<div class="divider"></div>
      <div class="section-title">${icon('check', { cls: 'ico-sm' })}${esc(state.resultTitle)} · mode = ${esc(String(d.mode || state.mode))}</div>
      ${cells.length ? `<div class="summary-grid">${cells.map((c) => `<div class="summary-cell"><div class="sc-k">${esc(c.k)}</div><div class="sc-v">${esc(fmtNumber(c.v))}</div></div>`).join('')}</div>`
        : '<p class="field-hint">后端返回了空摘要。</p>'}
      ${warnings.length ? `<div class="note-box is-warn">${icon('alert', { cls: 'ico-sm' })}<div><b>警告 ${warnings.length} 条</b><br>${warnings.map((w) => `· ${esc(w)}`).join('<br>')}</div></div>` : ''}
      <pre class="json-box">${prettyJson(d)}</pre>`;
  }

  async function readFile(file) {
    if (!file) return;
    if (file.size > 8 * 1024 * 1024) toastWarn('文件超过 8 MB，导入可能较慢');
    const reader = new FileReader();
    reader.onload = () => {
      if (disposed) return;
      state.payloadText = String(reader.result || '');
      state.fileName = file.name || 'pasted.json';
      g('paste').value = state.payloadText;
      g('import-err').hidden = true;
      paintImport();
    };
    reader.onerror = () => { if (!disposed) toastWarn('文件读取失败'); };
    reader.readAsText(file, 'utf-8');
  }

  const offClick = delegate(root, {
    'export-full': () => doExport('full'),
    'export-todo': () => doExport('todo'),
    preview: () => doPreview(),
    'copy-preview': () => {
      const text = JSON.stringify(state.preview, null, 2);
      if (!text) return;
      navigator.clipboard.writeText(text)
        .then(() => toastOk('预览 JSON 已复制到剪贴板'))
        .catch(() => toastWarn('浏览器拒绝了剪贴板访问，请手动选择复制'));
    },
    target: (el) => {
      state.importTarget = el.dataset.v === 'full' ? 'full' : 'todo';
      paintImport();
    },
    submit: () => submitImport(),
    'clear-form': () => {
      state.payloadText = '';
      state.fileName = '';
      state.result = null;
      state.mode = 'merge';
      g('paste').value = '';
      g('file-input').value = '';
      g('import-err').hidden = true;
      paintResult();
      paintImport();
    }
  });
  cleanups.push(offClick);

  // 模式单选（radio 用 change 事件更可靠）
  const onModeChange = (e) => {
    const t = e.target;
    if (!t.matches || !t.matches('input[name="im-mode"]')) return;
    state.mode = t.value === 'replace' ? 'replace' : 'merge';
    paintImport();
  };
  root.addEventListener('change', (e) => {
    onModeChange(e);
    const t = e.target;
    if (t.matches && t.matches('[data-slot="preview-kind"]')) {
      state.previewKind = t.value;
      doPreview();
    }
  });
  root.addEventListener('input', debounce((e) => {
    const t = e.target;
    if (t.matches && t.matches('[data-slot="paste"]')) {
      state.payloadText = t.value;
      state.fileName = '';
      g('import-err').hidden = true;
    }
  }, 150));

  const drop = g('drop');
  const fileInput = g('file-input');
  fileInput.addEventListener('change', () => readFile(fileInput.files && fileInput.files[0]));
  ['dragenter', 'dragover'].forEach((evt) => drop.addEventListener(evt, (e) => {
    e.preventDefault();
    drop.classList.add('is-drag');
  }));
  ['dragleave', 'drop'].forEach((evt) => drop.addEventListener(evt, (e) => {
    e.preventDefault();
    drop.classList.remove('is-drag');
    if (evt === 'drop' && e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]) {
      readFile(e.dataTransfer.files[0]);
    }
  }));
  drop.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); fileInput.click(); }
  });

  paintImport();
  paintPreview();
  doPreview();
  if (store.state && store.state.app) {
    g('submit-hint').textContent = `数据目录 ${store.state.app.dataDir || 'data'}`;
  }

  return function cleanup() {
    disposed = true;
    cleanups.reverse().forEach((fn) => { try { fn(); } catch { /* ignore */ } });
  };
}
