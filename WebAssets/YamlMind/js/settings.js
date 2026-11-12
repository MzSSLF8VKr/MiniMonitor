/* ==========================================================================
   Settings — lock mode, theme, editor prefs, backup, maintenance
   ========================================================================== */
(function () {
  'use strict';
  const { api, icon, esc, toast, modal, bytes, stamp, state, debounce } = YM;
  const $ = (id) => document.getElementById(id);
  const CAP = 50 * 1024 * 1024 * 1024;          // design target: 50 GB

  let schedule = {};

  /* ------------------------------------------------------------------ boot */
  async function init() {
    YM.mountTopbar('settings', '<a class="btn btn--ghost btn--sm" href="/">' + icon('folder') + '导图管理</a>');
    paintIcons();
    bind();
    try {
      await YM.bootstrap();
    } catch (e) {
      toast('无法加载设置：' + e.message, { kind: 'danger' });
      return;
    }
    schedule = state.schedule || {};
    paintAll();
    scrollSpy();
  }

  function paintIcons() {
    document.querySelectorAll('[data-ico]').forEach((el) => { el.innerHTML = icon(el.dataset.ico); });
    $('icoLock').innerHTML = icon('shield');
    $('icoTheme').innerHTML = icon('sun');
    $('icoEdit').innerHTML = icon('edit');
    $('icoDb').innerHTML = icon('db');
    $('icoMaint').innerHTML = icon('bolt');
  }

  function paintAll() {
    const s = state.settings || {};
    paintLock(s.lock_mode === 'on');
    paintTheme(s.theme || 'dark');
    paintEditor(s);
    paintStats(state.stats || {});
    paintJobs();
    paintImportGate(s.lock_mode === 'on');
  }

  /* ------------------------------------------------------------------ lock */
  function paintLock(on) {
    $('lockSwitch').setAttribute('aria-checked', on ? 'true' : 'false');
    $('lockTitle').textContent = on ? '锁定中' : '已解锁';
    $('lockDesc').textContent = on
      ? '删除与导入功能已被禁用，只能新增与修改；这是默认且推荐的状态。'
      : '锁定模式已手动关闭，现在可以删除导图与导入备份。系统会在每天 00:10 自动重新上锁。';
    document.querySelector('.locktoggle').classList.toggle('is-off', !on);
    $('lockChip').innerHTML = '<span class="chip ' + (on ? 'chip--lock' : 'chip--ok') + '">' +
      icon(on ? 'lock' : 'unlock') + (on ? 'LOCKED' : 'UNLOCKED') + '</span>';
    const fact = (ok, label) => '<span class="fact ' + (ok ? 'is-allow' : 'is-block') + '">' +
      icon(ok ? 'check' : 'x') + label + '</span>';
    $('lockFacts').innerHTML =
      fact(true, '新建导图') + fact(true, '修改内容') + fact(true, '重命名 / 副本') +
      fact(!on, '删除导图') + fact(!on, '导入 JSON') + fact(on, '自动回锁 00:10');
    $('relockNote').innerHTML = on
      ? '当前状态：<b>锁定</b>。所有破坏性操作被服务端一并拦截（不只是前端隐藏按钮）。'
      : '当前状态：<b>已解锁</b>，将于 <b class="mono">' + esc(schedule.relock_at || '今日 00:10') +
        '</b> 自动重新上锁。';
  }

  /* ----------------------------------------------------------------- theme */
  function paintTheme(theme) {
    document.querySelectorAll('.theme-card').forEach((b) =>
      b.classList.toggle('is-on', b.dataset.themeSet === theme));
  }

  /* ---------------------------------------------------------------- editor */
  function paintEditor(s) {
    $('autoSaveSwitch').setAttribute('aria-checked', s.auto_save !== 'off' ? 'true' : 'false');
    $('guideSwitch').setAttribute('aria-checked', s.guide_lines !== 'off' ? 'true' : 'false');
    const fs = Number(s.font_size || 14);
    $('fontRange').value = fs;
    $('fontVal').textContent = fs + 'px';
    document.querySelectorAll('#layoutSeg .seg__btn').forEach((b) =>
      b.classList.toggle('is-on', b.dataset.layout === (s.layout === 'tree' ? 'tree' : 'radial')));
  }

  /* ----------------------------------------------------------------- stats */
  function paintStats(st) {
    const pct = Math.min(100, (st.file_bytes || 0) / CAP * 100);
    const cells = [
      ['文档数量', String(st.doc_count || 0), false],
      ['正文体积', bytes(st.content_bytes || 0), false],
      ['库文件', bytes(st.file_bytes || 0), true],
      ['WAL 日志', bytes(st.wal_bytes || 0), false],
      ['历史快照', (st.version_count || 0) + ' 份 / ' + bytes(st.version_bytes || 0), false],
      ['索引文档', (st.fts_docs || 0) + ' (trigram) · ' + (st.big_docs || 0) + ' (bigram)', false],
      ['页大小', ((st.page_size || 0) / 1024) + ' KB', false],
      ['日志模式', String(st.journal_mode || '').toUpperCase(), false],
      ['空闲页', String(st.freelist_pages || 0), false],
      ['SQLite', String(st.sqlite || ''), false],
    ];
    $('stats').innerHTML = cells.map((c) =>
      '<div class="stat"><div class="stat__k">' + c[0] + '</div>' +
      '<div class="stat__v' + (c[2] ? ' is-accent' : '') + (String(c[1]).length > 16 ? ' is-small' : '') + '">' +
      esc(c[1]) + '</div></div>').join('') +
      '<div class="stat" style="grid-column:1/-1"><div class="stat__k">50 GB 容量目标</div>' +
      '<div class="stat__v is-accent">' + (pct < 0.01 && pct > 0 ? '&lt;0.01%' : pct.toFixed(2) + '%') +
      ' <span style="font-size:11px;color:var(--text-3)">已用 ' + bytes(st.file_bytes || 0) + ' / 50 GB</span></div>' +
      '<div class="bar"><i style="width:' + Math.max(0.6, pct) + '%"></i></div></div>';
    $('chipMode').innerHTML = icon('db') + 'WAL · ' + esc((st.journal_mode || '').toUpperCase());
    paintBackups(st.backups || []);
  }

  function paintBackups(list) {
    $('autobackCount').textContent = list.length + ' 份';
    if (!list.length) {
      $('autobackList').innerHTML = '<div class="autoback__empty">还没有自动备份：可点击下方「立即优化数据库」手动触发一次。</div>';
      return;
    }
    $('autobackList').innerHTML = list.map((b, i) =>
      '<div class="ab">' + icon(i === 0 ? 'sparkle' : 'db') +
      '<span class="ab__name">' + esc(b.name) + '</span>' +
      '<span class="ab__meta">' + bytes(b.bytes) + ' · ' + esc(YM.stamp(b.mtime)) +
      (i === 0 ? ' · 最新' : '') + '</span>' +
      '<a class="ab__dl" href="/api/backups/' + encodeURIComponent(b.name) + '" download title="下载">' +
      icon('down') + '</a></div>').join('');
  }

  /* ------------------------------------------------------------------ jobs */
  function paintJobs() {
    const jobRow = (cls, ico, t, d, whenLabel, when, lastHtml) =>
      '<div class="job ' + cls + '"><span class="job__ico">' + icon(ico) + '</span>' +
      '<div><div class="job__t">' + t + '</div><div class="job__d">' + d + '</div>' +
      (lastHtml ? '<div class="job__last">' + lastHtml + '</div>' : '') + '</div>' +
      '<div class="job__when"><span>' + whenLabel + '</span><b>' + esc(when || '—') + '</b></div></div>';

    const st = state.stats || {};
    const lo = st.last_optimize;
    const optimizeLast = lo
      ? '上次 <b class="' + (lo.status === 'ok' ? '' : 'is-bad') + '">' + (lo.status === 'ok' ? '成功' : '失败') +
        '</b> ' + esc(stamp(lo.finished_at || lo.started_at)) + detailText(lo.detail)
      : '尚未执行过（服务启动时会自动自检一次）';
    const rl = state.lastRelock;
    const relockLast = rl ? '上次 <b>成功</b> ' + esc(stamp(rl.finished_at || rl.started_at)) : '尚未执行过';

    const ver = state.version || '';
    if (ver) {
      const sub = document.querySelector('#sec-maint .set-card__sub');
      if (sub && !sub.dataset.ver) { sub.dataset.ver = ver; sub.innerHTML += ' · 当前版本 <b class="mono" style="color:var(--accent)">v' + esc(ver) + '</b>'; }
    }
    $('jobs').innerHTML =
      jobRow('', 'lock', '自动重新上锁', '每天 ' + (schedule.relock_schedule || '00:10') +
        ' · 手动关闭锁定模式后的兜底保护', '下次', schedule.relock_at, relockLast) +
      jobRow('job--alt', 'bolt', '数据库自动优化', '每天 ' + (schedule.optimize_schedule || '01:30') +
        ' · 检查点 / 回收空闲页 / 轮换快照 / 重建索引 / ANALYZE', '下次', schedule.optimize_at, optimizeLast) +
      jobRow('', 'map', '导图缓存预热', '每次保存立刻写入缓存树 JSON · 启动时补齐缺失部分', '策略', 'always',
        '打开思维导图无需重新解析 YAML');
  }

  function detailText(raw) {
    if (!raw) return '';
    try {
      const d = JSON.parse(raw);
      const bits = [];
      if (d.bytes_reclaimed != null) bits.push('回收 ' + bytes(d.bytes_reclaimed));
      if (d.trees_built) bits.push('补齐 ' + d.trees_built + ' 份导图缓存');
      if (d.versions_pruned) bits.push('清理 ' + d.versions_pruned + ' 份快照');
      if (d.bodies_repaired) bits.push('修复 ' + d.bodies_repaired + ' 份正文');
      if (d.fts) bits.push('索引已重建');
      return bits.length ? ' · ' + bits.join(' · ') : '';
    } catch (e) { return ''; }
  }

  /* ------------------------------------------------------------ import gate */
  function paintImportGate(locked) {
    $('importGate').innerHTML = locked
      ? '<div class="gate gate--locked">' + icon('lock') +
        '<span>锁定模式开启中，导入功能已禁用。请先在上方关闭锁定模式。</span></div>'
      : '<div class="gate gate--open">' + icon('unlock') +
        '<span>锁定模式已关闭，导入功能可用。导入按 ID 合并，不会清空现有数据。</span></div>';
    $('btnImport').disabled = locked;
    $('btnImport').classList.toggle('is-disabled', locked);
  }

  /* ----------------------------------------------------------------- events */
  function bind() {
    $('lockSwitch').addEventListener('click', async () => {
      const on = $('lockSwitch').getAttribute('aria-checked') === 'true';
      if (on) {
        const ok = await modal({
          title: '关闭锁定模式？', danger: true, okText: '我已了解，解锁', okIcon: 'unlock',
          body: '<p class="dim" style="line-height:1.8">解锁后可以<b style="color:var(--danger)">删除思维导图</b>与' +
            '<b style="color:var(--danger)">导入覆盖数据</b>。为防忘记，系统会在每天 ' +
            '<b class="mono">' + esc(schedule.relock_schedule || '00:10') + '</b> 自动重新上锁。</p>' +
            '<p class="dim" style="line-height:1.8;margin-top:10px">建议：操作前先导出一份 JSON 备份。</p>',
        });
        if (!ok) return;
        await put({ lock_mode: 'off' }, '锁定模式已关闭');
      } else {
        await put({ lock_mode: 'on' }, '锁定模式已开启');
      }
    });

    $('btnRelockNow').addEventListener('click', async () => {
      try {
        await api('/api/settings/relock-now', { method: 'POST' });
        await refresh();
        toast('已立即上锁', { kind: 'ok', title: '锁定模式' });
      } catch (e) { toast(e.message, { kind: 'danger' }); }
    });

    document.querySelectorAll('.theme-card').forEach((b) => b.addEventListener('click', () => {
      const t = b.dataset.themeSet;
      YM.applyTheme(t, true);
      paintTheme(t);
      document.querySelectorAll('#top [data-theme-set]').forEach((x) =>
        x.classList.toggle('is-on', x.dataset.themeSet === t));
      put({ theme: t }, '主题已切换为「' +
        { dark: '深色', light: '浅色', auto: '跟随系统' }[t] + '」', true);
    }));

    $('autoSaveSwitch').addEventListener('click', () => toggle('autoSaveSwitch', 'auto_save', 'ym:autosave', '自动保存'));
    $('guideSwitch').addEventListener('click', () => toggle('guideSwitch', 'guide_lines', 'ym:guides', '对齐虚线'));

    $('fontRange').addEventListener('input', (e) => {
      $('fontVal').textContent = e.target.value + 'px';
      document.documentElement.style.setProperty('--ed-fs', e.target.value + 'px');
      localStorage.setItem('ym:fontsize', e.target.value);
    });
    $('fontRange').addEventListener('change', () =>
      put({ font_size: $('fontRange').value }, '编辑器字号已设为 ' + $('fontRange').value + 'px'));

    $('layoutSeg').addEventListener('click', (e) => {
      const b = e.target.closest('[data-layout]');
      if (!b) return;
      document.querySelectorAll('#layoutSeg .seg__btn').forEach((x) => x.classList.toggle('is-on', x === b));
      localStorage.setItem('ym:layout', b.dataset.layout);
      put({ layout: b.dataset.layout }, '默认布局已设为「' +
        (b.dataset.layout === 'tree' ? '树形（向右展开）' : '放射（左右均衡）') + '」');
    });

    $('btnExport').addEventListener('click', () => {
      const v = $('withVersions').checked ? '1' : '0';
      location.href = '/api/backup?versions=' + v;
      toast('浏览器将开始下载备份文件', { kind: 'ok', title: '导出中' });
    });
    $('btnExportCopy').addEventListener('click', copyBackup);
    $('btnRefreshStats').addEventListener('click', async () => { await refresh(); toast('状态已刷新', { kind: 'ok' }); });

    $('btnImport').addEventListener('click', () => { if (!$('btnImport').disabled) $('fileImport').click(); });
    $('fileImport').addEventListener('change', onPickFile);

    $('btnOptimize').addEventListener('click', async (e) => {
      await runJob(e.currentTarget, '/api/maintenance/optimize', '立即优化', '数据库优化完成');
    });
    $('btnVacuum').addEventListener('click', async (e) => {
      await runJob(e.currentTarget, '/api/maintenance/vacuum', '深度压缩', 'VACUUM 完成');
    });

    $('setNav').addEventListener('click', (e) => {
      const a = e.target.closest('a');
      if (!a) return;
      e.preventDefault();
      const el = document.querySelector(a.getAttribute('href'));
      if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  }

  async function toggle(elId, key, lsKey, label) {
    const el = $(elId);
    const next = el.getAttribute('aria-checked') !== 'true';
    el.setAttribute('aria-checked', next ? 'true' : 'false');
    if (lsKey) localStorage.setItem(lsKey, next ? 'on' : 'off');
    await put({ [key]: next ? 'on' : 'off' }, label + (next ? '已开启' : '已关闭'));
  }

  async function put(patch, msg, silentTheme) {
    try {
      const r = await api('/api/settings', { method: 'PUT', body: patch });
      state.settings = r.settings;
      state.lock = (r.settings.lock_mode || 'on') === 'on';
      schedule = r.schedule || schedule;
      paintAll();
      if (msg) toast(msg, { kind: 'ok' });
      return true;
    } catch (e) {
      toast(e.message, { kind: 'danger', title: '设置保存失败' });
      return false;
    }
  }

  async function refresh() {
    try {
      const s = await api('/api/status');
      state.settings = s.settings; state.stats = s.stats; schedule = s.schedule;
      state.lock = (s.settings.lock_mode || 'on') === 'on';
      state.lastOptimize = s.last_optimize; state.lastRelock = s.last_relock;
      paintAll();
    } catch (e) { toast(e.message, { kind: 'danger' }); }
  }

  async function runJob(btn, url, busyText, okText) {
    const html = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<span class="dot dot--live"></span>' + busyText + '中…';
    try {
      const r = await api(url, { method: 'POST' });
      await refresh();
      const d = r.detail || {};
      toast((okText) + (d.bytes_reclaimed != null ? '，回收 ' + bytes(d.bytes_reclaimed) : ''), { kind: 'ok', title: '维护任务' });
    } catch (e) {
      toast(e.message, { kind: 'danger', title: '任务失败' });
    } finally {
      btn.disabled = false; btn.innerHTML = html;
    }
  }

  async function copyBackup() {
    try {
      const r = await api('/api/backup?versions=' + ($('withVersions').checked ? '1' : '0') + '&download=0');
      const text = JSON.stringify(r, null, 1);
      await navigator.clipboard.writeText(text);
      toast('备份 JSON 已复制到剪贴板（' + bytes(new Blob([text]).size) + '）', { kind: 'ok', title: '已复制' });
    } catch (e) {
      toast(e.message, { kind: 'danger', title: '复制失败' });
    }
  }

  async function onPickFile(e) {
    const file = e.target.files && e.target.files[0];
    e.target.value = '';
    if (!file) return;
    if (state.lock) {
      toast('锁定模式开启中，禁止导入。请先关闭锁定模式。', { kind: 'warn', title: '操作被拦截' });
      return;
    }
    let payload = null;
    try {
      payload = JSON.parse(await file.text());
    } catch (err) {
      toast('文件不是合法 JSON：' + err.message, { kind: 'danger', title: '导入失败' });
      return;
    }
    if (!payload || payload.app !== 'yamlmind' || !Array.isArray(payload.docs)) {
      toast('这不是 YAML Mind 的备份文件（缺少 app/docs 字段）', { kind: 'danger', title: '导入失败' });
      return;
    }
    const ok = await modal({
      title: '确认导入备份', okText: '开始导入', okIcon: 'up',
      body: '<div class="kv" style="margin-bottom:10px">' +
        '<dt>文件</dt><dd>' + esc(file.name) + ' · ' + bytes(file.size) + '</dd>' +
        '<dt>导图数量</dt><dd>' + (payload.docs ? payload.docs.length : 0) + '</dd>' +
        '<dt>导出时间</dt><dd>' + esc(stamp(payload.exported_at)) + '</dd>' +
        '<dt>模式</dt><dd>按 ID 合并（较新版本覆盖，其余跳过）</dd></div>' +
        '<p class="dim small">现有数据不会被清空；相同 ID 且版本更新的文档会被覆盖。</p>',
    });
    if (!ok) return;
    const btn = $('btnImport');
    btn.disabled = true;
    btn.innerHTML = '<span class="dot dot--live"></span>导入中…';
    try {
      const r = await api('/api/import?mode=merge', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      toast('新增 ' + r.created + ' · 覆盖 ' + r.updated + ' · 跳过 ' + r.skipped,
        { kind: 'ok', title: '导入完成' });
      await refresh();
    } catch (err) {
      toast(err.message, { kind: 'danger', title: '导入失败' });
      if (err.status === 423) paintImportGate(true);
    } finally {
      btn.disabled = false;
      btn.innerHTML = icon('up') + '选择文件并导入';
      paintImportGate(state.lock);
    }
  }

  /* ------------------------------------------------------------- scroll spy */
  function scrollSpy() {
    const links = [...document.querySelectorAll('#setNav a')];
    const secs = links.map((a) => document.querySelector(a.getAttribute('href'))).filter(Boolean);
    if (!('IntersectionObserver' in window)) return;
    const io = new IntersectionObserver((entries) => {
      entries.forEach((en) => {
        if (!en.isIntersecting) return;
        links.forEach((a) => a.classList.toggle('is-on', a.getAttribute('href') === '#' + en.target.id));
      });
    }, { rootMargin: '-80px 0px -60% 0px', threshold: 0 });
    secs.forEach((s) => io.observe(s));
  }

  const auto = debounce(() => refresh(), 900);
  document.addEventListener('DOMContentLoaded', async () => { await init(); auto(); });
})();
