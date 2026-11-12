/* ==========================================================================
   Home — document manager
   ========================================================================== */
(function () {
  'use strict';
  const { api, icon, esc, toast, modal, bytes, ago, debounce, state } = YM;

  const HUES = ['--b1', '--b2', '--b3', '--b4', '--b5', '--b6', '--b7', '--b8'];
  const CN = ['零', '一', '二', '三', '四', '五', '六', '七', '八', '九'];

  function cnNum(n) {
    if (n < 10) return CN[n] || String(n);
    if (n < 20) return '十' + (n % 10 ? CN[n % 10] : '');
    if (n < 100) return CN[Math.floor(n / 10)] + '十' + (n % 10 ? CN[n % 10] : '');
    return String(n);
  }
  function hueOf(id, title) {
    let h = 0;
    const s = (id || '') + (title || '');
    for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
    return 'var(' + HUES[h % HUES.length] + ')';
  }
  const $ = (id) => document.getElementById(id);
  const lookup = (id) => docs.find((d) => d.id === id);

  /* ------------------------------------------------------------ preferences */
  const pref = {
    get view() { return localStorage.getItem('ym:view') || 'grid'; },
    set view(v) { localStorage.setItem('ym:view', v); },
    get sort() { return localStorage.getItem('ym:sort') || 'updated'; },
    set sort(v) { localStorage.setItem('ym:sort', v); },
  };

  let docs = [];
  let total = 0;
  let view = pref.view;
  let sort = pref.sort;
  let search = '';
  let offset = 0;
  let loading = true;
  const LIMIT = 24;

  const elDocs = $('docs'), elPager = $('pager'), elSearch = $('search'), elSearchBox = $('searchBox');

  /* ------------------------------------------------------------------ boot */
  async function init() {
    mountChrome();
    bind();
    elDocs.className = 'docs';
    elDocs.innerHTML = '<div class="sk-grid">' + new Array(6).fill('<div class="sk sk-card"></div>').join('') + '</div>';
    try {
      await YM.bootstrap();
    } catch (e) {
      toast('无法加载服务状态：' + e.message, { kind: 'danger', title: '服务异常' });
    }
    paintLock();
    paintReadout();
    $('sort').value = sort;
    document.querySelectorAll('#viewSeg .seg__btn').forEach((b) => b.classList.toggle('is-on', b.dataset.view === view));
    await reload();
    loading = false;
    if (!state.templates || !state.templates.length) fallbackTemplates();
  }

  function mountChrome() {
    $('icNew').innerHTML = icon('plus');
    $('icGear').innerHTML = icon('gear');
    $('icRefresh').innerHTML = icon('refresh');
    $('searchIcon').innerHTML = icon('search');
    $('searchClear').innerHTML = icon('x');
    $('vi1').innerHTML = icon('grid');
    $('vi2').innerHTML = icon('list');
    YM.mountTopbar('home', '<span id="topLock" role="status"></span>' +
      '<a class="btn btn--ghost btn--sm" href="/settings">' + icon('gear') + '设置</a>');
  }

  function fallbackTemplates() {
    state.templates = [
      { id: 'blank', title: '空白导图' }, { id: 'outline', title: '项目大纲' }, { id: 'demo', title: '示例文档' },
    ];
  }

  function paintLock() {
    const on = state.lock;
    const chipHtml = '<span class="chip ' + (on ? 'chip--lock' : 'chip--ok') + '" style="cursor:pointer" ' +
      'title="' + (on ? '锁定模式开启：只允许新增与修改' : '锁定模式已关闭：可删除与导入') + '">' +
      icon(on ? 'lock' : 'unlock') + (on ? '锁定模式' : '已解锁') + '</span>';
    const foot = $('roLock');
    foot.innerHTML = chipHtml + '<span>删除与导入' + (on ? '已禁用' : '可用') + '</span>';
    foot.querySelector('.chip').addEventListener('click', () => { location.href = '/settings'; });
    $('topLock').innerHTML = chipHtml;
    $('roSchedule').innerHTML = icon('clock') + '<span>自动优化 ' +
      esc(((state.schedule || {}).optimize_schedule) || '01:30') + ' · 下次 ' +
      esc(shortDate((state.schedule || {}).optimize_at)) + '</span>';
  }

  function shortDate(s) {
    if (!s) return '—';
    return String(s).slice(5, 16);
  }

  function paintReadout() {
    const s = state.stats || {};
    $('roDocs').textContent = total || s.doc_count || 0;
    $('roVers').textContent = s.version_count != null ? s.version_count : '—';
    $('roSize').textContent = s.file_bytes != null ? bytes(s.file_bytes) : '—';
  }

  /* ------------------------------------------------------------------ data */
  async function reload(quiet) {
    if (!quiet) {
      elDocs.className = 'docs';
      elDocs.innerHTML = '<div class="sk-grid">' + new Array(Math.min(8, LIMIT)).fill('<div class="sk sk-card"></div>').join('') + '</div>';
    }
    try {
      const data = await api('/api/docs?' + new URLSearchParams({
        sort, search, limit: String(LIMIT), offset: String(offset),
      }));
      docs = data.docs || [];
      total = data.total || docs.length;
      render();
      paintReadout();
      $('roNodes').textContent = docs.reduce((a, d) => a + (d.node_count || 0), 0);
    } catch (e) {
      elDocs.innerHTML = emptyState('加载失败', e.message, 'alert');
      toast(e.message, { kind: 'danger', title: '加载失败' });
    }
  }

  /* ---------------------------------------------------------------- render */
  function render() {
    if (!docs.length) {
      elDocs.className = 'docs';
      elDocs.innerHTML = search
        ? emptyState('没有匹配的导图', '试试更短的关键词，或清除搜索条件。', 'search')
        : emptyState('还没有任何导图', '新建一个 YAML 文档，编辑器会在新标签页打开；写好后可随时在新标签页展示思维导图。', 'doc');
      elPager.classList.remove('is-on');
      const c = elDocs.querySelector('[data-act="clear"]');
      if (c) c.addEventListener('click', () => { elSearch.value = ''; search = ''; offset = 0; syncSearch(); reload(); });
      const n = elDocs.querySelector('[data-act="new"]');
      if (n) n.addEventListener('click', () => openCreate());
      return;
    }
    elDocs.className = 'docs ' + (view === 'grid' ? 'docs--grid' : 'docs--list');
    elDocs.innerHTML = view === 'grid' ? docs.map(card).join('') : table();
    bindDocEvents();
    renderPager();
  }

  function outlineLines(d) {
    const parts = (d.preview || '').split(' · ').filter(Boolean);
    const shown = parts.slice(0, 3);
    if (!shown.length) {
      return '<div class="doc__line muted" style="font-family:var(--font-ui);font-size:12.5px">空文档 · 等待编写内容</div>';
    }
    const html = shown.map((p, i) =>
      '<div class="doc__line"><b>' + cnNum(i + 1) + '、</b>' + esc(p) + '</div>').join('');
    return html + (parts.length > 3 ? '<div class="doc__more">另有 ' + (parts.length - 3) + ' 个一级分支…</div>' : '');
  }

  function card(d) {
    const bad = !d.valid;
    const err = bad ? (d.error || {}) : null;
    return '<article class="doc' + (bad ? ' is-bad' : '') + '" data-id="' + d.id + '" ' +
      'style="--doc-hue:' + hueOf(d.id, d.title) + '" tabindex="0">' +
      '<header class="doc__head">' +
      '<span class="doc__badge" title="' + esc((d.branch_count || 0) + ' 个一级分支') + '">' +
      esc((d.title || '·').trim().charAt(0)) + '</span>' +
      '<h3 class="doc__title" data-act="open" title="' + esc(d.title) + '">' + esc(d.title) + '</h3>' +
      '<div class="doc__acts">' +
      act('open', 'edit', '编辑 YAML (新标签页)') +
      act('map', 'map', '展示思维导图 (新标签页)') +
      act('menu', 'list', '更多操作…') +
      '</div></header>' +
      (bad
        ? '<div class="doc__err">' + icon('alert') + '<span>YAML 语法错误' +
        (err.line ? ' · 第 ' + err.line + ' 行' : '') + '：' + esc(err.message || '无法解析') + '</span></div>'
        : '<div class="doc__outline">' + outlineLines(d) + '</div>') +
      '<footer class="doc__meta">' +
      chip(d.node_count + ' 节点') + chip('深度 ' + d.max_depth) + chip(bytes(d.bytes)) +
      '<span class="doc__when">' + esc(ago(d.updated_at)) + '</span></footer></article>';
  }

  function act(name, ico, title, disabled) {
    return '<button class="icon-btn' + (name === 'del' ? ' is-danger' : '') + '" data-act="' + name +
      '" title="' + esc(title) + '"' + (disabled ? ' disabled' : '') + '>' + icon(ico) + '</button>';
  }
  const chip = (t) => '<span class="chip">' + esc(t) + '</span>';

  function table() {
    return '<table class="table"><thead><tr>' +
      '<th>标题</th><th>大纲预览</th><th class="num">节点</th><th class="num">深度</th>' +
      '<th class="num">体积</th><th>更新</th><th style="text-align:right">操作</th>' +
      '</tr></thead><tbody>' +
      docs.map((d) =>
        '<tr data-id="' + d.id + '">' +
        '<td><div class="row-title"><span class="doc__badge" style="--doc-hue:' + hueOf(d.id, d.title) + '">' +
        esc((d.title || '·').trim().charAt(0)) + '</span>' +
        '<a href="#" data-act="open">' + esc(d.title) + '</a>' +
        (d.valid ? '' : '<span class="chip chip--danger">语法错误</span>') + '</div></td>' +
        '<td><div class="row-outline">' + esc((d.preview || '').split(' · ').slice(0, 4)
          .map((t, i) => cnNum(i + 1) + '、' + t).join(' / ')) + '</div></td>' +
        '<td class="num">' + d.node_count + '</td><td class="num">' + d.max_depth + '</td>' +
        '<td class="num">' + bytes(d.bytes) + '</td>' +
        '<td class="muted small">' + esc(ago(d.updated_at)) + '</td>' +
        '<td><div class="row-acts">' + act('open', 'edit', '编辑') + act('map', 'map', '思维导图') +
        act('rename', 'code', '重命名') + act('dup', 'copy', '创建副本') +
        act('del', 'trash', state.lock ? '锁定模式开启，禁止删除（可到设置页关闭）' : '删除', state.lock) +
        '</div></td></tr>').join('') + '</tbody></table>';
  }

  function emptyState(title, text, ico) {
    return '<div class="empty"><div class="empty__icon">' + icon(ico || 'doc') + '</div>' +
      '<div class="empty__title">' + esc(title) + '</div>' +
      '<p class="empty__text">' + esc(text) + '</p>' +
      (search
        ? '<button class="btn btn--ghost" data-act="clear">清除搜索</button>'
        : '<button class="btn btn--primary btn--lg" data-act="new">' + icon('plus') + '新建导图</button>') +
      '</div>';
  }

  function renderPager() {
    const pages = Math.max(1, Math.ceil(total / LIMIT));
    const page = Math.floor(offset / LIMIT) + 1;
    if (pages < 2) { elPager.classList.remove('is-on'); elPager.innerHTML = ''; return; }
    elPager.classList.add('is-on');
    elPager.innerHTML = '<span class="pager__info">第 ' + page + ' / ' + pages + ' 页 · 共 ' + total + ' 份</span>' +
      '<button class="btn btn--ghost btn--sm" data-page="-1"' + (offset === 0 ? ' disabled' : '') + '>上一页</button>' +
      '<button class="btn btn--ghost btn--sm" data-page="1"' + (page >= pages ? ' disabled' : '') + '>下一页</button>';
    elPager.querySelectorAll('[data-page]').forEach((b) => b.addEventListener('click', () => {
      offset = Math.max(0, offset + Number(b.dataset.page) * LIMIT);
      reload(true);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    }));
  }

  function bindDocEvents() {
    elDocs.querySelectorAll('[data-id]').forEach((el) => {
      const id = el.dataset.id;
      el.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-act]');
        if (!btn || btn.disabled) return;
        const doc = lookup(id);
        if (!doc) return;
        switch (btn.dataset.act) {
          case 'open': e.preventDefault(); openEditor(doc.id); break;
          case 'map': openMap(doc.id); break;
          case 'dup': duplicate(doc); break;
          case 'del': remove(doc); break;
          case 'rename': rename(doc); break;
          case 'menu': cardMenu(btn, doc); break;
        }
      });
      el.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') { e.preventDefault(); openEditor(id); }
      });
      if (el.tagName === 'ARTICLE') {
        el.addEventListener('dblclick', (e) => { if (!e.target.closest('button')) openMap(id); });
      }
    });
  }

  /* ------------------------------------------------------------- card menu */
  function cardMenu(anchor, doc) {
    closeMenu();
    const items = [
      { act: 'rename', ico: 'code', label: '重命名' },
      { act: 'dup', ico: 'copy', label: '创建副本' },
      { act: 'export', ico: 'down', label: '导出 YAML' },
      { act: 'del', ico: 'trash', label: state.lock ? '删除（锁定中）' : '删除', danger: true, disabled: state.lock },
    ];
    const menu = document.createElement('div');
    menu.className = 'menu';
    menu.innerHTML = items.map((i) =>
      '<button class="menu__item' + (i.danger ? ' menu__item--danger' : '') + '" data-act="' + i.act + '"' +
      (i.disabled ? ' disabled' : '') + '>' + icon(i.ico) + '<span>' + esc(i.label) + '</span></button>').join('');
    document.body.appendChild(menu);
    const r = anchor.getBoundingClientRect();
    menu.style.top = Math.min(r.bottom + 6, window.innerHeight - menu.offsetHeight - 10) + 'px';
    menu.style.left = Math.max(10, Math.min(r.right - menu.offsetWidth, window.innerWidth - menu.offsetWidth - 10)) + 'px';
    menu.addEventListener('click', (e) => {
      const b = e.target.closest('[data-act]');
      if (!b) return;
      closeMenu();
      const a = b.dataset.act;
      if (a === 'rename') rename(doc);
      if (a === 'dup') duplicate(doc);
      if (a === 'del') remove(doc);
      if (a === 'export') exportYaml(doc);
    });
    setTimeout(() => document.addEventListener('click', closeMenu, { once: true }), 0);
  }
  function closeMenu() {
    document.querySelectorAll('.menu').forEach((m) => m.remove());
  }

  async function exportYaml(doc) {
    try {
      const d = await api('/api/docs/' + doc.id);
      const blob = new Blob([d.doc.content || ''], { type: 'text/yaml;charset=utf-8' });
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = (doc.title || 'mindmap').replace(/[\\/:*?"<>|]/g, '_') + '.yaml';
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(a.href), 4000);
      toast('已导出 ' + a.download, { kind: 'ok', title: '导出完成' });
    } catch (e) { toast(e.message, { kind: 'danger', title: '导出失败' }); }
  }

  /* --------------------------------------------------------------- actions */
  function openEditor(id) { window.open('/editor?id=' + encodeURIComponent(id), '_blank', 'noopener'); }
  function openMap(id) { window.open('/mindmap?id=' + encodeURIComponent(id), '_blank', 'noopener'); }

  function openCreate() {
    const list = (state.templates && state.templates.length) ? state.templates : null;
    if (!list) return;
    modal({
      title: '新建 YAML 导图',
      subtitle: '编辑器会在新的浏览器标签页中打开',
      wide: true, okText: '创建并打开编辑器', okIcon: 'edit',
      body: $('tplCreate').innerHTML,
      onMount(overlay) {
        const grid = overlay.querySelector('#tplGrid');
        const dz = overlay.querySelector('#dropZone');
        const dzIco = overlay.querySelector('#dzIco');
        const dzHint = overlay.querySelector('#dzHint');
        const fileInput = overlay.querySelector('#fileYaml');
        const titleInput = overlay.querySelector('#newTitle');

        let chosen = list[0].id;
        let imported = null;          // { name, bytes, content }
        let titleTouched = false;

        dzIco.innerHTML = icon('up');
        titleInput.value = suggestTitle();
        titleInput.focus(); titleInput.select();
        titleInput.addEventListener('input', () => { titleTouched = true; });

        function paintTemplates() {
          grid.innerHTML = list.map((t) =>
            '<button type="button" class="tpl' + (t.id === chosen && !imported ? ' is-on' : '') +
            '" data-tpl="' + t.id + '"' + (imported ? ' disabled' : '') + '>' +
            '<span class="tpl__name">' + esc(t.title) + '</span>' +
            '<span class="tpl__hint">' + esc(tplHint(t.id)) + '</span>' +
            '<span class="tpl__check">' + icon('check') + '</span></button>').join('');
          grid.querySelectorAll('.tpl').forEach((b) => b.addEventListener('click', () => {
            if (imported) return;
            chosen = b.dataset.tpl;
            paintTemplates();
          }));
        }

        function paintDropzone() {
          if (imported) {
            dz.classList.add('is-set');
            dz.querySelector('.dropzone__t').textContent = imported.name;
            dzHint.textContent = bytes(imported.bytes) + ' · ' + imported.content.split('\n').length +
              ' 行 · 点击右侧可清除';
            const btn = dz.querySelector('#dzBtn');
            btn.textContent = '清除';
            btn.classList.add('dropzone__clear');
          } else {
            dz.classList.remove('is-set');
            dz.querySelector('.dropzone__t').textContent = '选择或拖入 .yaml / .yml 文件';
            dzHint.textContent = '以文件内容创建新导图，文件名自动作为名称';
            const btn = dz.querySelector('#dzBtn');
            btn.textContent = '选择文件';
          }
        }

        async function takeFile(file) {
          if (!file) return;
          const ok = /\.(yaml|yml)$/i.test(file.name);
          if (!ok) toast('已选择非 .yaml/.yml 文件，仍会按 YAML 文本尝试解析', { kind: 'warn', title: file.name });
          if (file.size > 8 * 1024 * 1024) {
            toast('文件超过 8 MB，请拆分后再导入', { kind: 'danger', title: '文件过大' });
            return;
          }
          try {
            const text = await file.text();
            imported = { name: file.name, bytes: file.size, content: text };
            if (!titleTouched) {
              titleInput.value = file.name.replace(/\.(yaml|yml|txt)$/i, '') || suggestTitle();
            }
            paintDropzone();
            paintTemplates();
          } catch (e) {
            toast('读取文件失败：' + e.message, { kind: 'danger', title: '导入失败' });
          }
        }

        dz.addEventListener('click', (e) => {
          if (e.target.closest('#dzBtn')) {
            if (imported) { imported = null; paintDropzone(); paintTemplates(); return; }
            fileInput.click();
            return;
          }
          if (!imported) fileInput.click();
        });
        fileInput.addEventListener('change', () => { takeFile(fileInput.files && fileInput.files[0]); fileInput.value = ''; });
        ['dragenter', 'dragover'].forEach((ev) => dz.addEventListener(ev, (e) => {
          e.preventDefault(); dz.classList.add('is-over');
        }));
        ['dragleave', 'drop'].forEach((ev) => dz.addEventListener(ev, () => dz.classList.remove('is-over')));
        dz.addEventListener('drop', (e) => {
          e.preventDefault();
          const f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
          if (f) takeFile(f);
        });

        paintTemplates();
        paintDropzone();
        overlay.__create = () => ({
          title: (titleInput.value || '').trim(),
          tpl: imported ? null : chosen,
          content: imported ? imported.content : null,
          fileName: imported ? imported.name : null,
        });
      },
      onOk(overlay) {
        const spec = overlay.__create();
        if (!spec.title) { toast('请先填写导图名称', { kind: 'warn', title: '名称必填' }); return false; }

        if (spec.content !== null) {
          // open the tab inside the click handler, then point it at the new document
          const win = window.open('/editor?creating=1', '_blank');
          api('/api/docs', { method: 'POST', body: { title: spec.title, content: spec.content } })
            .then((r) => {
              if (win) win.location.replace('/editor?id=' + encodeURIComponent(r.doc.id));
              toast(r.doc.valid
                ? '已导入 ' + spec.fileName + '（' + r.doc.node_count + ' 个节点）'
                : '已导入，但 YAML 有语法错误，请在编辑器中修复：第 ' + ((r.doc.error || {}).line || '?') + ' 行',
                { kind: r.doc.valid ? 'ok' : 'warn', title: spec.title });
              reload(true);
            })
            .catch((e) => {
              if (win) win.close();
              toast(e.message, { kind: 'danger', title: '导入失败' });
            });
          return true;
        }

        window.open('/editor?new=1&title=' + encodeURIComponent(spec.title) +
          '&tpl=' + encodeURIComponent(spec.tpl || 'blank'), '_blank', 'noopener');
        toast('已在新标签页打开编辑器', { kind: 'ok', title: spec.title });
        setTimeout(() => reload(true), 900);
        return true;
      },
    });
  }

  function tplHint(id) {
    return ({
      blank: '最小骨架，两三个分支起步',
      outline: '背景 / 方案 / 验收 三段式',
      demo: '完整示例，演示层级编号',
    })[id] || '自定义内容';
  }

  function suggestTitle() {
    const d = new Date(), p = (n) => (n < 10 ? '0' + n : '' + n);
    return '未命名导图 ' + (d.getMonth() + 1) + p(d.getDate());
  }

  async function duplicate(doc) {
    try {
      const r = await api('/api/docs/' + doc.id + '/duplicate', { method: 'POST' });
      toast('副本已创建：' + r.doc.title, { kind: 'ok', title: '已复制' });
      await reload(true);
    } catch (e) { toast(e.message, { kind: 'danger', title: '复制失败' }); }
  }

  async function remove(doc) {
    if (state.lock) {
      toast('锁定模式开启中，禁止删除。如需删除请先到「设置」页关闭锁定模式。', { kind: 'warn', title: '操作被拦截' });
      return;
    }
    const ok = await modal({
      title: '删除导图', danger: true, okText: '永久删除', okIcon: 'trash',
      body: '<p class="dim" style="line-height:1.75">即将删除 <b style="color:var(--text)">' + esc(doc.title) +
        '</b>（' + doc.node_count + ' 个节点）。此操作不可撤销，建议先在「设置」页导出 JSON 备份。</p>',
    });
    if (!ok) return;
    try {
      await api('/api/docs/' + doc.id, { method: 'DELETE' });
      toast(doc.title + ' 已删除', { kind: 'ok', title: '删除成功' });
      await reload(true);
    } catch (e) { toast(e.message, { kind: 'danger', title: '删除失败' }); }
  }

  async function rename(doc) {
    const ok = await modal({
      title: '重命名', okText: '保存名称', okIcon: 'check',
      body: '<div class="field"><label class="label" for="rn">导图名称</label>' +
        '<input class="input" id="rn" maxlength="120" value="' + esc(doc.title) + '"></div>',
      onMount(o) { const i = o.querySelector('#rn'); i.focus(); i.select(); },
      onOk(o) { return (o.querySelector('#rn').value || '').trim() || false; },
    });
    if (!ok) return;
    try {
      await api('/api/docs/' + doc.id, { method: 'PUT', body: { title: ok, snapshot: false } });
      toast('已重命名为「' + ok + '」', { kind: 'ok' });
      await reload(true);
    } catch (e) { toast(e.message, { kind: 'danger', title: '重命名失败' }); }
  }

  /* ---------------------------------------------------------------- events */
  function syncSearch() { elSearchBox.classList.toggle('is-empty', !elSearch.value); }

  function bind() {
    $('btnNew').addEventListener('click', () => openCreate());
    $('btnRefresh').addEventListener('click', async () => {
      try { await YM.bootstrap(); } catch (e) { /* keep list working offline */ }
      paintLock(); paintReadout(); reload(true);
    });

    elSearch.addEventListener('input', debounce(() => {
      search = elSearch.value.trim(); offset = 0; syncSearch(); reload(true);
    }, 280));
    elSearch.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { elSearch.value = ''; search = ''; offset = 0; syncSearch(); reload(true); elSearch.blur(); }
    });
    $('searchClear').addEventListener('click', () => {
      elSearch.value = ''; search = ''; offset = 0; syncSearch(); reload(true); elSearch.focus();
    });
    $('sort').addEventListener('change', (e) => { sort = e.target.value; pref.sort = sort; offset = 0; reload(); });

    document.querySelectorAll('#viewSeg .seg__btn').forEach((b) => b.addEventListener('click', () => {
      view = b.dataset.view; pref.view = view;
      document.querySelectorAll('#viewSeg .seg__btn').forEach((x) => x.classList.toggle('is-on', x === b));
      render();
    }));

    document.addEventListener('keydown', (e) => {
      const tag = (e.target.tagName || '').toLowerCase();
      if ((tag === 'input' || tag === 'textarea' || tag === 'select') && !(e.metaKey || e.ctrlKey)) return;
      if (e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.key === '/') { e.preventDefault(); elSearch.focus(); }
      else if (e.key === 'n' || e.key === 'N') { e.preventDefault(); openCreate(); }
      else if (e.key === 'r' || e.key === 'R') { e.preventDefault(); reload(true); }
      else if (e.key === 'Escape') closeMenu();
    });

    document.addEventListener('visibilitychange', () => {
      if (document.hidden || loading) return;
      YM.bootstrap().then(() => { paintLock(); paintReadout(); }).catch(() => {});
      reload(true);
    });
    window.addEventListener('scroll', closeMenu, { passive: true });
  }

  document.addEventListener('DOMContentLoaded', init);
})();
