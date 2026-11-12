/* ==========================================================================
   YAML editor — CodeMirror + dotted indent guides + autosave + outline
   ========================================================================== */
(function () {
  'use strict';
  const { api, icon, esc, toast, modal, bytes, debounce, state, qs } = YM;

  const TAB = 2;                       // indent step (spaces)
  const AUTOSAVE_MS = 900;             // debounce for 输入即保存
  const VALIDATE_MS = 520;

  const $ = (id) => document.getElementById(id);
  let cm = null;
  let docId = qs('id');
  let revision = 0;
  let dirty = false;
  let saving = false;
  let guides = true;
  let guideCanvas = null, gctx = null, guideRAF = 0;
  let errLineMark = null;
  let errWidget = null;
  let lastSavedAt = 0;
  let outlineRows = [];

  /* ------------------------------------------------------------------ icons */
  function paintIcons() {
    document.querySelectorAll('[data-ico]').forEach((el) => { el.innerHTML = icon(el.dataset.ico); });
    const seg = $('themeSeg');
    seg.innerHTML = ['dark', 'light', 'auto'].map((t) =>
      '<button class="seg__btn" data-theme-set="' + t + '" title="' +
      { dark: '深色', light: '浅色', auto: '跟随系统' }[t] + '">' +
      icon({ dark: 'moon', light: 'sun', auto: 'monitor' }[t]) + '</button>').join('');
    YM.bindThemeButtons(seg);
    seg.querySelectorAll('[data-theme-set]').forEach((b) =>
      b.classList.toggle('is-on', b.dataset.themeSet === YM.theme()));
    seg.addEventListener('click', (e) => {
      const b = e.target.closest('[data-theme-set]');
      if (!b) return;
      seg.querySelectorAll('[data-theme-set]').forEach((x) => x.classList.toggle('is-on', x === b));
      requestAnimationFrame(scheduleGuides);
    });
  }

  /* ------------------------------------------------------------------- boot */
  async function init() {
    paintIcons();
    bindToolbar();
    // placeholder tab opened by the home page while a file import is being created
    if (qs('creating') === '1') {
      document.title = '正在创建… · YAML 编辑器';
      const msg = $('loading').querySelector('span');
      if (msg) msg.textContent = '正在导入文件并创建文档…';
      return;                       // the opener redirects this tab to ?id=…
    }
    try { await YM.bootstrap(); } catch (e) { /* keep editing even if bootstrap fails */ }
    // this browser's choice wins; otherwise fall back to the stored setting
    const lsGuides = localStorage.getItem('ym:guides');
    guides = lsGuides !== null ? lsGuides !== 'off'
      : ((state.settings || {}).guide_lines !== 'off');
    $('optGuides').classList.toggle('is-on', guides);
    $('optWrap').classList.toggle('is-on', localStorage.getItem('ym:wrap') === 'on');
    try {
      if (!docId) {
        const created = await createFromQuery();
        if (!created) return;
      } else {
        const r = await api('/api/docs/' + encodeURIComponent(docId));
        setupEditor(r.doc.content || '');
        revision = r.doc.revision || 0;
        $('docTitle').value = r.doc.title || '';
        applyMeta(r.doc);
      }
    } catch (e) {
      $('loading').innerHTML = '<div class="empty__icon">' + icon('alert') + '</div>' +
        '<div class="empty__title">无法载入文档</div><p class="empty__text">' + esc(e.message) + '</p>' +
        '<a class="btn btn--ghost" href="/">返回导图管理</a>';
      return;
    }
    markClean(savedNow());
    setTimeout(() => { $('loading').classList.add('is-out'); }, 120);
    const jump = Number(qs('line'));
    if (jump > 0) setTimeout(() => gotoLine(jump), 260);
    setTimeout(() => { validate(true); loadHistory(); }, 260);
  }

  async function createFromQuery() {
    const title = qs('title') || '未命名导图';
    const tpl = qs('tpl') || 'blank';
    const r = await api('/api/docs', { method: 'POST', body: { title, template: tpl } });
    docId = r.doc.id;
    history.replaceState(null, '', '/editor?id=' + encodeURIComponent(docId));
    setupEditor(r.doc.content || '');
    revision = r.doc.revision || 0;
    $('docTitle').value = r.doc.title || '';
    applyMeta(r.doc);
    toast('新导图已创建，输入内容会自动保存', { kind: 'ok', title: r.doc.title });
    return true;
  }

  function applyMeta(d) {
    document.title = (d.title || 'YAML') + ' · YAML 编辑器';
    $('revChip').textContent = 'v' + (d.revision || 0);
  }

  /* ---------------------------------------------------------- CodeMirror */
  function setupEditor(text) {
    cm = CodeMirror.fromTextArea($('src'), {
      mode: 'yaml',
      theme: 'default',
      lineNumbers: true,
      lineWrapping: localStorage.getItem('ym:wrap') === 'on',
      tabSize: TAB,
      indentUnit: TAB,
      indentWithTabs: false,
      smartIndent: true,
      styleActiveLine: { nonEmpty: true },
      autoCloseBrackets: true,
      matchBrackets: true,
      foldGutter: { gutter: ['CodeMirror-linenumbers', 'CodeMirror-foldgutter'] },
      extraKeys: extraKeys(),
      gutters: ['CodeMirror-linenumbers', 'CodeMirror-foldgutter'],
    });
    cm.setValue(text);
    cm.clearHistory();
    fs = fontSizePref();
    applyFontSize();
    setupGuideLayer();

    cm.on('cursorActivity', onCursor);
    cm.on('scroll', scheduleGuides);
    cm.on('viewportChange', scheduleGuides);
    cm.on('changes', debounce(() => {
      dirty = true;
      setSaveState('dirty', '未保存的更改');
      validateNow();
      if (autosaveOn()) scheduleSave();
    }, 40));
    cm.on('changes', () => { updateCounts(); scheduleGuides(); });
    updateGuides();
    updateCounts();
    onCursor();
  }

  function extraKeys() {
    return {
      'Ctrl-S': saveNow, 'Cmd-S': saveNow,
      'Tab': (c) => {
        if (c.somethingSelected()) c.indentSelection('add');
        else c.replaceSelection(Array(TAB + 1).join(' '), 'end');
      },
      'Shift-Tab': (c) => c.indentSelection('subtract'),
      'Enter': (c) => smartEnter(c),
      'Ctrl-/': (c) => toggleComment(c), 'Cmd-/': (c) => toggleComment(c),
      'Ctrl-D': (c) => { if (!duplicateLine(c)) return; }, 'Cmd-D': (c) => duplicateLine(c),
      'Ctrl-Alt-Down': (c) => copyLineDown(c), 'Cmd-Alt-Down': (c) => copyLineDown(c),
    };
  }

  /* keep list bullets and indentation when the user presses Enter */
  function smartEnter(c) {
    const cur = c.getCursor();
    const line = c.getLine(cur.line);
    const indent = /^\s*/.exec(line)[0];
    const bullet = /^(\s*)(-\s+|\d+[.)]\s+)/.exec(line);
    if (bullet && line.trim().length === bullet[2].trim().length) {
      // empty list item -> leave the list, keep the parent indent
      const parent = indent.slice(0, Math.max(0, indent.length - TAB));
      c.operation(() => {
        c.replaceRange('', { line: cur.line, ch: 0 }, { line: cur.line, ch: line.length });
        c.replaceSelection('\n' + parent);
      });
      return;
    }
    let next = indent;
    if (bullet) next = bullet[1] + bullet[2];
    else if (/^\s*[^#\s].*:\s*(#.*)?$/.test(line)) next = indent + ' '.repeat(TAB);
    c.replaceSelection('\n' + next);
  }

  function toggleComment(c) {
    const s = c.listSelections()[0];
    const from = Math.min(s.anchor.line, s.head.line), to = Math.max(s.anchor.line, s.head.line);
    let all = true;
    for (let i = from; i <= to; i++) {
      const t = c.getLine(i);
      if (t.trim() && !/^\s*#/.test(t)) { all = false; break; }
    }
    c.operation(() => {
      for (let i = from; i <= to; i++) {
        const t = c.getLine(i);
        if (!t.trim()) continue;
        if (all) c.replaceRange(t.replace(/^(\s*)#\s?/, '$1'), { line: i, ch: 0 }, { line: i, ch: t.length });
        else c.replaceRange('# ' + t.replace(/^\s*/, ''), { line: i, ch: 0 }, { line: i, ch: t.length });
      }
    });
  }

  function duplicateLine(c) {
    const cur = c.getCursor(), line = c.getLine(cur.line);
    c.replaceRange('\n' + line, { line: cur.line, ch: line.length });
    return true;
  }
  function copyLineDown(c) { return duplicateLine(c); }

  /* ------------------------------------------------- dotted indent guides */
  function setupGuideLayer() {
    const host = $('code');
    guideCanvas = document.createElement('canvas');
    guideCanvas.className = 'guide-layer' + (guides ? ' is-on' : '');
    guideCanvas.setAttribute('aria-hidden', 'true');
    host.insertBefore(guideCanvas, host.firstChild);
    gctx = guideCanvas.getContext('2d');
    if (window.ResizeObserver) new ResizeObserver(scheduleGuides).observe(host);
  }

  function scheduleGuides() {
    if (guideRAF) return;
    guideRAF = requestAnimationFrame(() => { guideRAF = 0; drawGuides(); });
  }

  /**
   * Paint one dashed vertical line per indent level, spanning the runs of
   * consecutive lines that are indented at least that deep — the Notepad++
   * indentation-guide look, aligned to the editor character grid.
   */
  function drawGuides() {
    if (!cm || !gctx) return;
    const dpr = window.devicePixelRatio || 1;
    const box = guideCanvas.getBoundingClientRect();
    const W = Math.max(1, Math.round(box.width)), H = Math.max(1, Math.round(box.height));
    if (guideCanvas.width !== Math.round(W * dpr)) guideCanvas.width = Math.round(W * dpr);
    if (guideCanvas.height !== Math.round(H * dpr)) guideCanvas.height = Math.round(H * dpr);
    gctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    gctx.clearRect(0, 0, W, H);
    if (!guides) return;

    const sc = cm.getScrollInfo();
    const lh = cm.defaultTextHeight();
    const first = cm.lineAtHeight(sc.top, 'local');
    const last = Math.min(cm.lineCount() - 1, cm.lineAtHeight(sc.top + sc.clientHeight, 'local'));
    if (last < first) return;

    const rows = [];
    let maxLevels = 0;
    for (let i = first; i <= last; i++) {
      const text = cm.getLine(i) || '';
      let cols = 0;
      for (const ch of text) {
        if (ch === ' ') cols += 1;
        else if (ch === '\t') cols += TAB;
        else break;
      }
      const levels = Math.floor(cols / TAB);
      if (levels > maxLevels) maxLevels = levels;
      const top = cm.charCoords({ line: i, ch: 0 }, 'local').top - sc.top;
      rows.push({ top, bottom: top + lh, levels });
    }
    if (!maxLevels) return;

    const charW = cm.defaultCharWidth();
    const x0 = cm.charCoords({ line: first, ch: 0 }, 'local').left - sc.left;
    const css = getComputedStyle(document.documentElement);
    const colour = css.getPropertyValue('--ed-guide').trim() || 'rgba(140,158,186,.3)';
    const gridColour = css.getPropertyValue('--ed-grid').trim() || 'rgba(140,158,186,.09)';

    // layer 1: a faded, full-height dot column at every indent step, so the
    // grid stays visible even on lines that have no leading whitespace
    const step = TAB * charW;
    const cols = Math.max(0, Math.min(24, Math.floor((W - x0) / step)));
    if (cols > 0 && rows.length) {
      const topY = rows[0].top, bottomY = rows[rows.length - 1].bottom;
      gctx.strokeStyle = gridColour;
      gctx.lineWidth = 1;
      gctx.setLineDash([1, 3.5]);
      gctx.beginPath();
      for (let L = 1; L <= cols; L++) {
        const x = Math.round(x0 + L * step) + 0.5;
        if (x < 0 || x > W) continue;
        gctx.moveTo(x, topY);
        gctx.lineTo(x, Math.max(bottomY, H));
      }
      gctx.stroke();
    }

    // layer 2: crisp dashed guides inside the actual indentation of each line
    gctx.lineWidth = 1;
    gctx.lineCap = 'butt';
    gctx.setLineDash([Math.max(2, lh * 0.16), Math.max(2, lh * 0.15)]);
    gctx.beginPath();
    gctx.strokeStyle = colour;
    for (let L = 1; L <= maxLevels; L++) {
      const x = Math.round(x0 + L * TAB * charW) + 0.5;
      if (x <= 0 || x >= W) continue;
      let start = null, end = null;
      for (const r of rows) {
        if (r.levels >= L) {
          if (!start) start = r;
          end = r;
        } else if (start) {
          gctx.moveTo(x, start.top + 1);
          gctx.lineTo(x, end.bottom - 1);
          start = end = null;
        }
      }
      if (start) { gctx.moveTo(x, start.top + 1); gctx.lineTo(x, end.bottom - 1); }
    }
    gctx.stroke();
  }

  function updateGuides() {
    applyFontSize();
    if (cm) cm.refresh();
    scheduleGuides();
  }

  let fs = 14;
  function fontSizePref() {
    return Number((state.settings && state.settings.font_size) || localStorage.getItem('ym:fontsize') || 14);
  }
  function fontSize() { return fs; }

  function applyFontSize() {
    document.documentElement.style.setProperty('--ed-fs', fs + 'px');
    const v = $('fsVal'); if (v) v.textContent = fs;
  }

  function setFontSize(next, persist) {
    fs = Math.max(10, Math.min(22, Number(next) || fs));
    localStorage.setItem('ym:fontsize', String(fs));
    applyFontSize();
    updateGuides();
    if (persist) YM.setSetting({ font_size: fs }).catch(() => {});
  }

  /* force CodeMirror to rebuild the visible lines after a font change */
  function rerender() {
    if (!cm) return;
    const hist = cm.getHistory(), cur = cm.getCursor(), sc = cm.getScrollInfo();
    cm.setValue(cm.getValue());
    cm.setHistory(hist);
    cm.setCursor(cur);
    cm.scrollTo(sc.left, sc.top);
  }

  /* ------------------------------------------------------------ status bar */
  function onCursor() {
    if (!cm) return;
    const cur = cm.getCursor();
    $('stLine').textContent = cur.line + 1;
    $('stCol').textContent = cur.ch + 1;
    const text = cm.getLine(cur.line) || '';
    let cols = 0;
    for (const ch of text) { if (ch === ' ') cols++; else if (ch === '\t') cols += TAB; else break; }
    $('stIndent').textContent = cols;
    const sel = cm.getSelection();
    $('stSelWrap').hidden = !sel;
    if (sel) $('stSel').textContent = sel.length + ' 字符';
    highlightOutline(cur.line + 1);
  }

  function updateCounts() {
    if (!cm) return;
    const text = cm.getValue();
    $('stChars').textContent = text.length;
    $('stBytes').textContent = bytes(new Blob([text]).size);
  }

  function setYamlState(v) {
    const box = $('stYaml');
    if (!v) return;
    box.className = 'eb__grp ' + (v.ok ? 'eb__grp--ok' : 'eb__grp--bad');
    box.innerHTML = icon(v.ok ? 'check' : 'alert') + '<b>' +
      (v.ok ? (v.empty ? '空文档 · 等待内容' : 'YAML 有效') : '语法错误 L' + (v.error.line || '?')) + '</b>';
    if (v.ok) {
      $('stNodes').textContent = v.stats.nodes;
      $('stDepth').textContent = v.stats.max_depth;
      $('docStat').textContent = v.stats.nodes + ' 节点 · 深度 ' + v.stats.max_depth + ' · ' + v.stats.branches + ' 分支';
    }
  }

  function setSaveState(kind, text) {
    const el = $('saveState');
    el.className = 'sstate sstate--' + kind;
    el.innerHTML = '<i class="dot' + (kind === 'saving' ? ' dot--live' : '') + '"></i><span>' + esc(text) + '</span>';
  }
  function savedNow() {
    lastSavedAt = Date.now();
    return '已保存 ' + new Date().toTimeString().slice(0, 8);
  }
  function markClean(msg) {
    dirty = false;
    setSaveState('saved', msg || '已保存');
    $('stSaved').innerHTML = icon('clock') + '<b>' + new Date().toTimeString().slice(0, 8) + '</b>';
  }
  function autosaveOn() {
    const s = state.settings || {};
    return s.auto_save !== 'off' && localStorage.getItem('ym:autosave') !== 'off';
  }

  /* ----------------------------------------------------------------- saving */
  const scheduleSave = debounce(() => { if (dirty && !saving) doSave(); }, AUTOSAVE_MS);

  async function saveNow(event) {
    if (event) event.preventDefault();
    if (!cm || !docId) return;
    await doSave(true);
  }

  async function doSave(explicit) {
    if (saving || !docId) return;
    const text = cm.getValue();
    if (!dirty && !explicit) return;
    saving = true;
    setSaveState('saving', explicit ? '保存中…' : '自动保存中…');
    try {
      const r = await api('/api/docs/' + docId, {
        method: 'PUT',
        body: { content: text, revision, snapshot: true },
      });
      revision = r.doc.revision;
      $('revChip').textContent = 'v' + revision;
      markClean(savedNow());
      if (r.doc.revision_conflict) showConflict(r.doc);
      if (explicit) toast('已保存 · v' + revision, { kind: 'ok', title: '保存成功' });
      if (r.parsed) { setYamlState({ ok: r.parsed.ok, error: r.parsed.error, empty: false, stats: r.parsed.stats }); }
    } catch (e) {
      setSaveState('error', '保存失败');
      if (e.status === 423) toast(e.message, { kind: 'warn', title: '锁定模式' });
      else toast(e.message, { kind: 'danger', title: '保存失败' });
    } finally {
      saving = false;
    }
  }

  function flushBeacon() {
    if (!dirty || !docId || !cm) return;
    try {
      navigator.sendBeacon('/api/docs/' + docId + '/flush',
        new Blob([cm.getValue()], { type: 'text/plain;charset=UTF-8' }));
    } catch (e) { /* best effort */ }
  }

  /* --------------------------------------------------------------- validate */
  const validateNow = debounce(() => validate(), VALIDATE_MS);

  async function validate(immediate) {
    if (!cm) return;
    try {
      const r = await api('/api/validate', { method: 'POST', body: { content: cm.getValue(), title: currentTitle() } });
      setYamlState(r);
      if (r.ok) {
        clearError();
        outlineRows = r.outline || [];
        renderOutline(r.root_title || currentTitle(), r.stats);
      } else {
        showError(r.error);
      }
    } catch (e) {
      setYamlState({ ok: false, error: { message: e.message, line: null }, stats: { nodes: '—', max_depth: '—', branches: '—' } });
    }
  }

  function clearError() {
    if (errLineMark != null) { cm.removeLineClass(errLineMark, 'wrap', 'cm-line-error'); cm.removeLineClass(errLineMark, 'background', null); errLineMark = null; }
    if (errWidget) { errWidget.clear(); errWidget = null; }
    document.querySelectorAll('.lineerr').forEach((n) => n.remove());
  }

  function showError(err) {
    if (!cm || !err) return;
    clearError();
    const ln = Math.max(0, (err.line || 1) - 1);
    if (ln >= cm.lineCount()) return;
    cm.addLineClass(ln, 'wrap', 'cm-line-error');
    errLineMark = ln;
    const tpl = $('tplLineError').content.cloneNode(true);
    tpl.querySelector('.lineerr__ico').innerHTML = icon('alert');
    tpl.querySelector('.lineerr__msg').textContent = err.message || '无法解析';
    tpl.querySelector('.lineerr__pos').textContent = '第 ' + (err.line || '?') + ' 行' +
      (err.column ? ' · 第 ' + err.column + ' 列' : '') + (err.context ? ' · ' + err.context : '');
    tpl.querySelector('.lineerr__close').innerHTML = icon('x');
    tpl.querySelector('.lineerr__close').addEventListener('click', clearError);
    tpl.querySelector('.lineerr__fix').addEventListener('click', () => gotoLine((err.line || 1)));
    errWidget = cm.addLineWidget(ln, tpl, { coverGutter: false, noHScroll: true, above: false });
  }

  function gotoLine(n) {
    const line = Math.max(0, Math.min(cm.lineCount() - 1, Number(n) - 1));
    cm.setCursor({ line, ch: 0 });
    cm.scrollIntoView({ line, ch: 0 }, 140);
    cm.focus();
  }

  /* ---------------------------------------------------------------- outline */
  function currentTitle() { return ($('docTitle').value || '').trim() || '思维导图'; }

  function renderOutline(rootTitle, stats) {
    const host = $('outline');
    const keepTop = host.scrollTop;
    if (!outlineRows.length) {
      host.innerHTML = '<div class="empty" style="border:0;padding:26px 14px;background:transparent">' +
        '<div class="empty__icon">' + icon('tree') + '</div>' +
        '<div class="empty__title" style="font-size:14px">还没有节点</div>' +
        '<p class="empty__text" style="font-size:12px">在左侧写下 YAML 键值，大纲与编号会实时出现在这里。</p></div>';
      return;
    }
    const rows = outlineRows.slice(0, 600).map((r, i) =>
      '<button class="ol__item" data-line="' + (r.line || 0) + '" data-i="' + i + '" ' +
      'style="--lvl:' + Math.min(9, r.depth) + '" title="' + esc(r.num + ' ' + r.title + (r.value ? ': ' + r.value : '')) + '">' +
      '<span class="ol__num">' + esc(r.num) + '</span>' +
      '<span class="ol__t">' + esc(r.title) + '</span>' +
      (r.value ? '<span class="ol__v">' + esc(r.value) + '</span>' : '') + '</button>').join('');
    host.innerHTML = '<div class="ol">' +
      '<div class="ol__root"><span class="dot"></span>' + esc(rootTitle || currentTitle()) +
      '<span class="ol__count">' + (stats ? stats.nodes : outlineRows.length) + ' 节点</span></div>' +
      rows + '</div>';
    host.querySelectorAll('.ol__item').forEach((b) => b.addEventListener('click', () => gotoLine(b.dataset.line)));
    host.scrollTop = keepTop;
  }

  function highlightOutline(line) {
    if (!outlineRows.length) return;
    let active = null;
    for (const r of outlineRows) { if (r.line && r.line <= line) active = r; else if (r.line && r.line > line) break; }
    document.querySelectorAll('#outline .ol__item').forEach((el) => {
      const r = outlineRows[Number(el.dataset.i)];
      el.classList.toggle('is-active', !!active && !!r && r === active);
    });
  }

  /* ---------------------------------------------------------------- history */
  async function loadHistory() {
    if (!docId) return;
    const host = $('history');
    try {
      const r = await api('/api/docs/' + docId + '/versions');
      const list = r.versions || [];
      if (!list.length) {
        host.innerHTML = '<div class="empty" style="border:0;padding:26px 14px;background:transparent">' +
          '<div class="empty__icon">' + icon('history') + '</div>' +
          '<div class="empty__title" style="font-size:14px">暂无历史快照</div>' +
          '<p class="empty__text" style="font-size:12px">每次保存（间隔 2 分钟以上）会自动留一份快照，最多保留 ' +
          50 + ' 份，可随时回滚。</p></div>';
        $('asideFoot').textContent = '快照按文档轮换保存，数据库优化时会自动清理';
        return;
      }
      host.innerHTML = '<div class="hist">' + list.map((v) =>
        '<button class="hist__item" data-vid="' + v.vid + '" title="回滚到 v' + v.revision + '">' +
        '<span class="hist__rev">v' + v.revision + '</span>' +
        '<span class="hist__body"><span class="hist__when">' + esc(YM.stamp(v.created_at)) + '</span><br>' +
        '<span class="hist__size">' + bytes(v.raw_bytes) + ' · ' + esc(v.title) + '</span></span>' +
        '<span class="hist__act">' + icon('refresh') + '</span></button>').join('') + '</div>';
      host.querySelectorAll('.hist__item').forEach((b) =>
        b.addEventListener('click', () => restoreVersion(b.dataset.vid)));
      $('asideFoot').textContent = list.length + ' 份快照 · 点击可回滚';
    } catch (e) {
      host.innerHTML = '<p class="tiny muted" style="padding:14px">' + esc(e.message) + '</p>';
    }
  }

  async function restoreVersion(vid) {
    const ok = await modal({
      title: '回滚到历史快照', danger: true, okText: '回滚', okIcon: 'refresh',
      body: '<p class="dim" style="line-height:1.75">当前内容会先存成一份新快照，然后用所选版本覆盖编辑器内容。可以继续撤销或再次回滚。</p>',
    });
    if (!ok) return;
    try {
      await doSave(false);
      const r = await api('/api/versions/' + vid + '/restore', { method: 'POST' });
      cm.setValue(r.doc.content || '');
      revision = r.doc.revision || revision;
      $('revChip').textContent = 'v' + revision;
      toast('已回滚到所选快照', { kind: 'ok', title: '回滚成功' });
      validate(true); updateCounts(); loadHistory();
      markClean(savedNow());
    } catch (e) { toast(e.message, { kind: 'danger', title: '回滚失败' }); }
  }

  /* ------------------------------------------------------------------ tools */
  function normalizeIndent() {
    const src = cm.getValue().split('\n');
    const out = [];
    src.forEach((raw) => {
      let line = raw.replace(/\t/g, ' '.repeat(TAB)).replace(/\s+$/, '');
      const m = /^ +/.exec(line);
      if (m) {
        const n = Math.round(m[0].length / TAB) * TAB;
        line = ' '.repeat(n) + line.slice(m[0].length);
      }
      out.push(line);
    });
    const text = out.join('\n');
    if (text !== cm.getValue()) cm.setValue(text);
    toast('已展开 Tab 并规范层级缩进（注释与内容不变）', { kind: 'ok', title: '整理完成' });
  }

  function showConflict(serverDoc) {
    const b = $('conflict');
    b.hidden = false;
    b.innerHTML = '<span class="banner__ico">' + icon('alert') + '</span>' +
      '<span><b>版本冲突：</b>另一个标签页已保存过这份文档（当前 v' + revision + '）。' +
      '你的这份内容已经写入，但建议确认一下最新版本。</span>' +
      '<span class="right row">' +
      '<button class="btn btn--sm btn--ghost" id="cfReload">载入服务端版本</button>' +
      '<button class="btn btn--sm" id="cfClose">知道了</button></span>';
    $('cfClose').onclick = () => { b.hidden = true; b.innerHTML = ''; };
    $('cfReload').onclick = async () => {
      const r = await api('/api/docs/' + docId);
      cm.setValue(r.doc.content || '');
      revision = r.doc.revision;
      $('revChip').textContent = 'v' + revision;
      b.hidden = true; b.innerHTML = '';
      validate(true);
      toast('已载入服务端最新版本 v' + revision, { kind: 'ok' });
    };
  }

  /* --------------------------------------------------------------- toolbar */
  function bindToolbar() {
    $('btnSave').addEventListener('click', saveNow);
    $('btnMap').addEventListener('click', openMap);
    $('btnFormat').addEventListener('click', normalizeIndent);
    $('optGuides').addEventListener('click', () => {
      guides = !guides;
      localStorage.setItem('ym:guides', guides ? 'on' : 'off');
      $('optGuides').classList.toggle('is-on', guides);
      if (guideCanvas) guideCanvas.classList.toggle('is-on', guides);
      scheduleGuides();
    });
    $('optWrap').addEventListener('click', () => {
      const on = !cm.getOption('lineWrapping');
      cm.setOption('lineWrapping', on);
      localStorage.setItem('ym:wrap', on ? 'on' : 'off');
      $('optWrap').classList.toggle('is-on', on);
      updateGuides();
      setTimeout(scheduleGuides, 60);
    });
    $('fsPlus').addEventListener('click', () => setFontSize(fs + 1, true));
    $('fsMinus').addEventListener('click', () => setFontSize(fs - 1, true));
    $('btnAside').addEventListener('click', () => setAside(false));
    $('asideOpen').addEventListener('click', () => setAside(true));
    document.querySelectorAll('#asideTabs .seg__btn').forEach((b) => b.addEventListener('click', () => {
      document.querySelectorAll('#asideTabs .seg__btn').forEach((x) => x.classList.toggle('is-on', x === b));
      const isOut = b.dataset.tab === 'outline';
      $('outline').classList.toggle('hide', !isOut);
      $('history').classList.toggle('hide', isOut);
      $('asideFoot').textContent = isOut ? '点击大纲条目可跳转到对应行' : '点击快照可回滚';
      if (!isOut) loadHistory();
    }));
    $('docTitle').addEventListener('change', saveTitle);
    $('docTitle').addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); e.target.blur(); } });
  }

  function setAside(show) {
    document.querySelector('.ework').classList.toggle('no-aside', !show);
    localStorage.setItem('ym:aside', show ? 'on' : 'off');
    setTimeout(() => { if (cm) { cm.refresh(); scheduleGuides(); } }, 60);
  }

  const saveTitle = debounce(async () => {
    if (!docId) return;
    const title = currentTitle();
    try {
      const r = await api('/api/docs/' + docId, { method: 'PUT', body: { title, snapshot: false } });
      revision = r.doc.revision || revision;
      $('revChip').textContent = 'v' + revision;
      document.title = title + ' · YAML 编辑器';
      toast('名称已更新', { kind: 'ok' });
      validate(true);
    } catch (e) { toast(e.message, { kind: 'danger', title: '改名失败' }); }
  }, 700);

  function openMap() {
    if (!docId) { toast('请先保存当前文档', { kind: 'warn' }); return; }
    doSave(false).then(() => {
      window.open('/mindmap?id=' + encodeURIComponent(docId), '_blank', 'noopener');
    });
  }

  /* -------------------------------------------------------------- lifecycle */
  window.addEventListener('keydown', (e) => {
    if (!(e.ctrlKey || e.metaKey)) return;
    const k = e.key;
    if (k === 's' || k === 'S') { e.preventDefault(); saveNow(); }
    else if (k === '=' || k === '+') { e.preventDefault(); setFontSize(fs + 1, true); }
    else if (k === '-' || k === '_') { e.preventDefault(); setFontSize(fs - 1, true); }
    else if (k === '0') { e.preventDefault(); setFontSize(14, true); }
  });
  window.addEventListener('beforeunload', () => { flushBeacon(); });
  document.addEventListener('visibilitychange', () => { if (document.hidden) flushBeacon(); });
  window.addEventListener('resize', debounce(() => updateGuides(), 120));
  document.addEventListener('DOMContentLoaded', async () => {
    await init();
    if (localStorage.getItem('ym:aside') === 'off') setAside(false);
    // keep the font-size setting from the settings page in sync across tabs
    window.addEventListener('storage', (e) => {
      if (e.key === 'ym:theme') { /* theme handled in common.js */ updateGuides(); }
      if (e.key === 'ym:fontsize') { document.documentElement.style.setProperty('--ed-fs', e.newValue + 'px'); updateGuides(); }
    });
  });
})();
