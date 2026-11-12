/* #/tasks —— 左侧项目栏 + 右侧任务列表（主视图） */
import { api, reportError } from '../api.js';
import { store, defaultSettings, on } from '../store.js';
import { track } from '../track.js';
import {
  esc, fmtDateTime, fmtExpiry, fmtNumber, isOverdue, preview,
  toInputValue, fromInputValue, dayDelta
} from '../format.js';
import { icon } from '../icons.js';
import { openModal, confirmModal } from '../modal.js';
import { toastOk, toastInfo } from '../toast.js';
import {
  PALETTE, PRIORITIES, PROJECT_ICON_NAMES, projectDot, priorityLabel, normalizePriority,
  delegate, debounce, stateBlock, offlineStateBlock, skeletonRows, selectHtml
} from '../ui.js';

export const meta = { id: 'tasks', title: '任务', hash: '#/tasks' };

/** 跨视图切换记住的选择（模块级，会话内有效）。 */
const remembered = {
  projectId: null,
  tab: 'all',
  includeArchived: false,
  doneCollapsed: true
};

const TABS = [
  { key: 'all', label: '全部', view: 'all', status: 'open' },
  { key: 'today', label: '今天', view: 'today', status: 'open' },
  { key: 'planned', label: '计划', view: 'upcoming', status: 'open' },
  { key: 'done', label: '已完成', view: 'all', status: 'done' }
];

const TASK_LIMIT = 500;

export function render(root, params = {}) {
  let disposed = false;
  const cleanups = [];

  const ui = {
    projectId: params.project || remembered.projectId || null,
    tab: TABS.some((t) => t.key === remembered.tab) ? remembered.tab : 'all',
    projects: [],
    projectTotal: 0,
    selectedProject: null,   // 当前项目不在（被搜索/归档过滤后的）列表里时的兜底
    tasks: [],
    taskTotal: 0,
    done: [],
    doneTotal: 0,
    offset: 0,
    loadingProjects: true,
    loadingTasks: true,
    projectError: null,
    taskError: null,
    taskQ: '',
    includeArchived: remembered.includeArchived,
    selectMode: false,
    selected: new Set(),
    quickDue: null,
    settings: store.settings || defaultSettings()
  };

  root.innerHTML = `
    <div class="work">
      <aside class="projects" id="pPanel" aria-label="项目创建">
        <div class="projects-head">
          <h2>项目</h2>
          <span class="projects-count" data-slot="p-count"></span>
          <span style="flex:1"></span>
          <button class="icon-btn tiny" type="button" data-act="new-project" data-track="ui.project.create_open"
            aria-label="新建项目（颜色 / 炫彩 / 图标 / 过期时间）" title="完整新建：颜色 / 炫彩 / 图标 / 过期时间">${icon('plus', { cls: 'ico-xs' })}</button>
        </div>
        <div class="projects-new">
          <div class="new-row">
            <span data-slot="new-dot">${projectDot({ color: PALETTE[0], colorMode: 'solid' })}</span>
            <input type="text" data-slot="project-new" placeholder="项目名称，按 Enter 创建" aria-label="新建项目名称" autocomplete="off" maxlength="60">
          </div>
        </div>
        <div class="projects-list" data-slot="p-list" role="list"></div>
        <div class="projects-foot">
          <label class="switch" title="是否在列表中显示已归档项目">
            <input type="checkbox" data-act="toggle-archived" ${ui.includeArchived ? 'checked' : ''}>
            <span class="switch-track" aria-hidden="true"></span>
            <span class="switch-text">显示已归档</span>
          </label>
          <button class="btn btn-sm btn-ghost only-narrow" type="button" data-act="close-drawer">收起</button>
        </div>
      </aside>

      <section class="tasks-pane" id="tPane">
        <div class="pane-head">
          <button class="btn btn-sm btn-ghost only-narrow" type="button" data-act="open-drawer" aria-label="打开项目栏">${icon('menu', { cls: 'ico-sm' })}项目</button>
          <div class="pane-title" data-slot="pane-title"></div>
          <span style="flex:1 1 auto"></span>
          <div class="search-box" style="max-width:190px">
            ${icon('search', { cls: 'ico ico-sm' })}
            <input type="search" data-slot="task-search" placeholder="搜索任务" aria-label="搜索任务关键词" autocomplete="off">
          </div>
          <button class="btn btn-sm btn-ghost" type="button" data-act="new-task" title="新建任务">${icon('plus', { cls: 'ico-sm' })}详细新建</button>
          <button class="btn btn-sm btn-ghost" type="button" data-act="toggle-select" aria-pressed="false">${icon('listChecks', { cls: 'ico-sm' })}多选</button>
        </div>

        <div class="tabs" role="tablist" aria-label="任务视图切换" data-slot="tabs"></div>

        <div class="quickadd">
          <div class="quickadd-row">
            <div class="quickadd-wrap">
              ${icon('plus', { cls: 'ico qa-icon' })}
              <input type="text" data-slot="quick-add" placeholder="添加任务，按 Enter 保存" aria-label="快速添加任务" autocomplete="off">
            </div>
            <button class="btn btn-primary" type="button" data-act="quick-add">${icon('plus', { cls: 'ico-sm' })}添加</button>
          </div>
          <div class="quickadd-extra" data-slot="quick-extra"></div>
        </div>

        <div class="task-list" data-slot="task-list"></div>
        <div data-slot="batch-bar"></div>
      </section>
    </div>`;

  const $ = (sel) => root.querySelector(sel);
  const slots = {
    pCount: $('[data-slot="p-count"]'),
    pList: $('[data-slot="p-list"]'),
    pNew: $('[data-slot="project-new"]'),
    pNewDot: $('[data-slot="new-dot"]'),
    paneTitle: $('[data-slot="pane-title"]'),
    tabs: $('[data-slot="tabs"]'),
    taskList: $('[data-slot="task-list"]'),
    quickAdd: $('[data-slot="quick-add"]'),
    quickExtra: $('[data-slot="quick-extra"]'),
    batchBar: $('[data-slot="batch-bar"]'),
    taskSearch: $('[data-slot="task-search"]')
  };

  /* ---------------- 数据加载 ---------------- */

  async function loadProjects() {
    ui.loadingProjects = true;
    ui.projectError = null;
    paintProjects();
    try {
      const data = await api.listProjects({ includeArchived: ui.includeArchived });
      if (disposed) return;
      const items = Array.isArray(data && data.items) ? data.items : (Array.isArray(data) ? data : []);
      ui.projects = items;
      ui.projectTotal = Number(data && data.total) || items.length;

      if (!ui.projectId) {
        const first = ui.projects.find((p) => !p.archived) || ui.projects[0] || null;
        ui.projectId = first ? first.id : null;
      }
      remembered.projectId = ui.projectId;

      if (ui.projectId && !ui.projects.some((p) => p.id === ui.projectId)) {
        try {
          const single = await api.getProject(ui.projectId);
          if (!disposed && single && single.id) ui.selectedProject = single;
        } catch { ui.selectedProject = null; }
      } else {
        ui.selectedProject = null;
      }
    } catch (err) {
      if (disposed) return;
      ui.projectError = err && err.isNetwork ? 'offline' : (err && err.message) || '项目加载失败';
    } finally {
      ui.loadingProjects = false;
      if (!disposed) { paintProjects(); paintPane(); }
    }
  }

  async function loadTasks() {
    ui.loadingTasks = true;
    ui.taskError = null;
    ui.offset = 0;
    paintTasks();
    const tab = TABS.find((t) => t.key === ui.tab) || TABS[0];
    const scoped = ui.projectId || '';
    try {
      const [main, doneGroup] = await Promise.all([
        api.listTasks({ projectId: scoped, status: tab.status, view: tab.view, q: ui.taskQ, limit: TASK_LIMIT, offset: 0 }),
        tab.status === 'done'
          ? Promise.resolve(null)
          : api.listTasks({ projectId: scoped, status: 'done', view: 'all', q: ui.taskQ, limit: 100, offset: 0 })
      ]);
      if (disposed) return;
      const items = main && Array.isArray(main.items) ? main.items : (Array.isArray(main) ? main : []);
      ui.tasks = items;
      ui.taskTotal = Number.isFinite(Number(main && main.total)) ? Number(main.total) : items.length;
      ui.offset = items.length;

      const d = doneGroup && Array.isArray(doneGroup.items) ? doneGroup.items : (Array.isArray(doneGroup) ? doneGroup : []);
      ui.done = d;
      ui.doneTotal = Number.isFinite(Number(doneGroup && doneGroup.total)) ? Number(doneGroup.total) : d.length;
      trimSelection();
    } catch (err) {
      if (disposed) return;
      ui.taskError = err && err.isNetwork ? 'offline' : (err && err.message) || '任务加载失败';
      ui.tasks = [];
      ui.taskTotal = 0;
      ui.done = [];
      ui.doneTotal = 0;
    } finally {
      ui.loadingTasks = false;
      if (!disposed) paintTasks();
    }
  }

  function findTask(id) {
    return ui.tasks.find((t) => t.id === id) || ui.done.find((t) => t.id === id) || null;
  }

  function trimSelection() {
    for (const id of [...ui.selected]) {
      if (!findTask(id)) ui.selected.delete(id);
    }
  }

  function currentProject() {
    const inList = ui.projects.find((p) => p.id === ui.projectId);
    if (inList) return inList;
    if (ui.selectedProject && ui.selectedProject.id === ui.projectId) return ui.selectedProject;
    return null;
  }

  /* ---------------- 渲染：项目栏 ---------------- */

  function sortedProjects() {
    return [...ui.projects].sort((a, b) => {
      const pin = Number(!!b.pinned) - Number(!!a.pinned);
      if (pin) return pin;
      const an = Number(a.sortOrder);
      const bn = Number(b.sortOrder);
      if (Number.isFinite(an) && Number.isFinite(bn) && an !== bn) return an - bn;
      return String(a.name || '').localeCompare(String(b.name || ''), 'zh-CN');
    });
  }

  function paintProjects() {
    const host = slots.pList;
    slots.pCount.textContent = ui.projects.length ? `${ui.projects.length}${ui.projectTotal > ui.projects.length ? `/${ui.projectTotal}` : ''}` : '';
    paintNewDot();

    if (ui.projectError === 'offline') {
      host.innerHTML = '<div class="state" style="padding:20px 10px"><p style="font-size:12px">离线：项目列表不可用。</p></div>';
      return;
    }
    if (ui.projectError) {
      host.innerHTML = `<div class="state err" style="padding:18px 8px"><h3 style="font-size:13px">项目加载失败</h3>
        <p style="font-size:12px">${esc(ui.projectError)}</p>
        <div class="state-actions"><button class="btn btn-sm btn-ghost" type="button" data-act="retry-projects">${icon('refresh', { cls: 'ico-sm' })}重试</button></div></div>`;
      return;
    }
    if (ui.loadingProjects) {
      host.innerHTML = `<div style="padding:6px 4px">${skeletonRows(5, 'sk-compact')}</div>`;
      return;
    }
    if (!ui.projects.length) {
      host.innerHTML = `<div class="state" style="padding:24px 10px">
        <div class="state-ico" style="width:40px;height:40px">${icon('folder', { cls: 'ico' })}</div>
        <h3 style="font-size:13px">${ui.includeArchived ? '没有项目' : '还没有项目'}</h3>
        <p style="font-size:11.5px">在上方输入项目名称，按 Enter 即可创建（自动配色）。</p>
      </div>`;
      return;
    }

    const arr = sortedProjects();
    host.innerHTML = arr.map((p, idx) => projectRowHtml(p, idx, arr)).join('');
  }

  function projectRowHtml(p, idx, arr) {
    const selected = p.id === ui.projectId;
    const open = Number(p.openCount) || 0;
    const total = Number(p.taskCount) || 0;
    const expiry = p.expireAt ? fmtExpiry(p.expireAt) : null;
    const atTop = idx === 0 || !!arr[idx - 1].pinned !== !!p.pinned;
    const atBottom = idx === arr.length - 1 || !!arr[idx + 1].pinned !== !!p.pinned;
    const cls = ['p-row', selected ? 'is-selected' : '', p.colorMode === 'rainbow' ? 'rainbow' : '', p.archived ? 'is-archived' : '']
      .filter(Boolean).join(' ');

    /* 行高恒定：状态标记与操作按钮都在同一行内，选中/悬停只改透明度，不撑高（避免闪动）。 */
    return `<div class="${cls}" role="listitem" tabindex="0" data-act="project-select" data-id="${esc(p.id)}" aria-current="${selected ? 'true' : 'false'}">
      <div class="p-row-main">
        ${projectDot(p)}
        <span class="p-name" title="${esc(p.name)}${p.description ? ` · ${esc(p.description)}` : ''}">${esc(p.name || '(未命名)')}</span>
        ${p.pinned ? `<span class="p-flag p-pin" title="已置顶">${icon('pin', { cls: 'ico-xs' })}</span>` : ''}
        ${expiry ? `<span class="p-flag p-expire ${expiry.tone}" title="${esc(expiry.text)}">${icon('clock', { cls: 'ico-xs' })}</span>` : ''}
        <span class="p-counts" title="未完成 / 总数"><b>${open}</b>/${total}</span>
        <span class="p-tools" role="group" aria-label="项目操作">
          <button class="icon-btn tiny" type="button" data-act="project-move" data-dir="-1" aria-label="上移项目" title="上移" ${atTop ? 'disabled' : ''}>${icon('arrowUp', { cls: 'ico-xs' })}</button>
          <button class="icon-btn tiny" type="button" data-act="project-move" data-dir="1" aria-label="下移项目" title="下移" ${atBottom ? 'disabled' : ''}>${icon('arrowDown', { cls: 'ico-xs' })}</button>
          <button class="icon-btn tiny" type="button" data-act="project-edit" aria-label="项目设置" title="项目设置（颜色 / 炫彩 / 图标 / 过期 / 置顶 / 归档 / 删除）">${icon('settings', { cls: 'ico-xs' })}</button>
        </span>
      </div>
    </div>`;
  }

  /* ---------------- 渲染：右侧 ---------------- */

  function paintTabs() {
    const project = currentProject();
    slots.paneTitle.innerHTML = project
      ? `${projectDot(project)}<span class="t" title="${esc(project.name)}">${esc(project.name || '')}</span>`
        + `<span class="pane-desc">${esc(preview(project.description, 34) || `${fmtNumber(project.openCount || 0)} 个未完成`)}</span>`
      : `<span class="t">全部任务</span><span class="pane-desc">跨项目</span>`;

    slots.tabs.innerHTML = TABS.map((t) => {
      const active = t.key === ui.tab;
      let n = '';
      if (active) n = fmtNumber(ui.taskTotal);
      else if (t.key === 'done' && ui.doneTotal) n = fmtNumber(ui.doneTotal);
      return `<button class="tab" role="tab" type="button" data-act="tab" data-key="${t.key}" aria-selected="${active}">${iconForTab(t.key)}${esc(t.label)}${n ? `<span class="tab-n">${n}</span>` : ''}</button>`;
    }).join('');
  }

  function iconForTab(key) {
    const map = { all: 'inbox', today: 'sun', planned: 'calendar', done: 'check' };
    return icon(map[key] || 'dot', { cls: 'ico-xs' });
  }

  function paintQuickExtra() {
    const d = ui.settings.defaults || {};
    const chips = [
      { key: '', label: '无截止' },
      { key: 'today', label: '今天 18:00' },
      { key: 'tomorrow', label: '明天 09:00' },
      { key: 'nextweek', label: '下周一 09:00' }
    ];
    slots.quickExtra.innerHTML = chips.map((c) => `<button class="chip chip-btn ${ui.quickDue === c.key && c.key ? 'is-active' : ''}"
        type="button" data-act="quick-due" data-key="${c.key}">${icon('calendar', { cls: 'ico-xs' })}${esc(c.label)}</button>`).join('')
      + `<span class="field-hint">默认优先级 <b>${esc(priorityLabel(d.priority))}</b></span>`;
    const project = currentProject();
    slots.quickAdd.placeholder = project ? `添加到「${project.name}」，按 Enter 保存` : '添加任务，按 Enter 保存';
  }

  function paintPane() {
    paintTabs();
    paintQuickExtra();
    paintTasks();
  }

  function taskRowHtml(task) {
    const done = task.status === 'done';
    const canceled = task.status === 'canceled';
    const settled = done || canceled;
    const overdue = !settled && !!task.dueAt && isOverdue(task.dueAt);
    const dueToday = !settled && !!task.dueAt && dayDelta(task.dueAt) === 0;
    const project = ui.projects.find((p) => p.id === task.projectId);
    const showProject = !ui.projectId || ui.tab === 'all';
    const priority = normalizePriority(task.priority);
    const selected = ui.selected.has(task.id);

    const meta = [];
    if (showProject && project) {
      meta.push(`<span class="meta-chip project-chip">${projectDot(project, 'pdot')}${esc(project.name)}</span>`);
    }
    if (task.dueAt) {
      const cls = ['meta-chip', overdue ? 'is-overdue' : '', dueToday ? 'is-today' : ''].filter(Boolean).join(' ');
      meta.push(`<span class="${cls}" title="截止时间">${icon('calendar', { cls: 'ico-xs' })}${esc(fmtDateTime(task.dueAt))}${overdue ? ' · 已逾期' : ''}</span>`);
    }
    if (done && task.completedAt) {
      meta.push(`<span class="meta-chip">${icon('check', { cls: 'ico-xs' })}${esc(fmtDateTime(task.completedAt))} 完成</span>`);
    }
    if (canceled) meta.push('<span class="meta-chip">已取消</span>');

    const cls = ['t-row', settled ? 'is-done' : '', selected ? 'is-selected' : ''].filter(Boolean).join(' ');
    const boxCls = ['check', ui.selectMode ? 'select-box' : '', ui.selectMode ? (selected ? 'is-checked' : '') : (settled ? 'is-checked' : '')].filter(Boolean).join(' ');

    return `<div class="${cls}" data-id="${esc(task.id)}" data-act="${ui.selectMode ? 'task-select' : ''}">
      <button class="${boxCls}" type="button" data-act="${ui.selectMode ? 'task-select' : 'task-toggle'}"
        aria-label="${ui.selectMode ? (selected ? '取消选择该任务' : '选择该任务') : (settled ? '重新打开任务' : '完成任务')}" aria-pressed="${ui.selectMode ? selected : settled}">${icon('check')}</button>
      <button class="flag" type="button" data-act="task-flag" data-p="${priority}"
        aria-label="优先级 ${esc(priorityLabel(priority))}，点击切换" title="优先级：${esc(priorityLabel(priority))}（点击切换）">
        ${icon(priority ? 'flagFilled' : 'flag', { cls: 'ico-sm' })}</button>
      <div class="t-body">
        <div class="t-title">${esc(task.title || '(无标题)')}</div>
        ${task.notes ? `<div class="t-notes" title="${esc(task.notes)}">${esc(preview(task.notes, 120))}</div>` : ''}
        <div class="t-meta">${meta.join('')}</div>
      </div>
      <div class="t-actions" role="group" aria-label="任务操作">
        <button class="icon-btn tiny" type="button" data-act="task-edit" aria-label="编辑任务" title="编辑">${icon('pencil', { cls: 'ico-xs' })}</button>
        <button class="icon-btn tiny" type="button" data-act="task-delete" aria-label="删除任务" title="删除">${icon('trash', { cls: 'ico-xs' })}</button>
      </div>
    </div>`;
  }

  function paintTasks() {
    paintTabs();
    paintQuickExtra();
    paintBatchBar();
    const host = slots.taskList;

    if (ui.taskError === 'offline') { host.innerHTML = offlineStateBlock(); return; }
    if (ui.taskError) {
      host.innerHTML = stateBlock({
        iconName: 'alert', variant: 'err', title: '任务加载失败', text: esc(ui.taskError),
        actions: ['<button class="btn btn-primary" type="button" data-act="retry-tasks">重试</button>']
      });
      return;
    }
    if (ui.loadingProjects && !ui.projects.length) {
      host.innerHTML = `<div class="sk" style="height:34px;margin:6px 4px"></div>${skeletonRows(4)}`;
      return;
    }
    if (!ui.projectId && !ui.projects.length) {
      host.innerHTML = stateBlock({
        iconName: 'inbox', title: '从新建项目开始',
        text: '任务必须归属于一个项目。创建第一个项目后，就能在上方输入框快速添加任务。',
        actions: [`<button class="btn btn-primary" type="button" data-act="new-project">${icon('plus', { cls: 'ico-sm' })}新建项目</button>`]
      });
      return;
    }
    if (ui.loadingTasks) { host.innerHTML = skeletonRows(6); return; }

    const open = ui.tasks.filter((t) => t.status === 'open');
    const settled = ui.tasks.filter((t) => t.status !== 'open');
    let html = '';

    if (ui.tab === 'done') {
      html = ui.tasks.length
        ? ui.tasks.map((t) => taskRowHtml(t)).join('')
        : stateBlock({ iconName: 'check', title: '还没有已完成的任务', text: '在「全部」视图点击任务左侧圆形复选框即可完成。' });
    } else if (!ui.tasks.length) {
      const hints = {
        today: '今天没有需要处理的任务。',
        planned: '没有安排在未来截止日期的任务。',
        all: ui.taskQ ? `没有匹配「${esc(ui.taskQ)}」的任务。` : '这个视图是空的，试试在上方添加任务。'
      };
      html = stateBlock({ iconName: 'inbox', title: '空视图', text: hints[ui.tab] || hints.all });
    } else {
      html = `<div class="task-group">${open.map((t) => taskRowHtml(t)).join('')}</div>`;
      if (settled.length) html += `<div class="task-group">${settled.map((t) => taskRowHtml(t)).join('')}</div>`;
    }

    if (ui.tab !== 'done' && ui.doneTotal > 0) {
      const collapsed = remembered.doneCollapsed;
      html += `<div class="task-group">
        <button class="group-head" type="button" data-act="toggle-done" aria-expanded="${String(!collapsed)}">
          ${icon('chevronDown', { cls: 'ico-xs chev' })}已完成<span class="badge badge-no-dot badge-muted">${fmtNumber(ui.doneTotal)}</span><span class="line"></span>
        </button>
        ${collapsed ? '' : ui.done.map((t) => taskRowHtml(t)).join('')}
      </div>`;
    }

    if (ui.tasks.length && ui.taskTotal > ui.tasks.length) {
      html += `<div style="padding:12px 4px"><button class="btn btn-ghost btn-block" type="button" data-act="load-more">加载更多（还有 ${fmtNumber(ui.taskTotal - ui.tasks.length)} 条）</button></div>`;
    }

    host.innerHTML = html;
  }

  function paintBatchBar() {
    if (!ui.selectMode) { slots.batchBar.innerHTML = ''; return; }
    const n = ui.selected.size;
    slots.batchBar.innerHTML = `<div class="batch-bar" role="toolbar" aria-label="批量操作">
      <button class="btn btn-sm btn-ghost" type="button" data-act="select-all">${n && n === ui.tasks.length ? '取消全选' : `全选 ${fmtNumber(ui.tasks.length)}`}</button>
      <span class="count">已选 ${n}</span>
      <button class="btn btn-sm" type="button" data-act="batch-complete" ${n ? '' : 'disabled'}>${icon('check', { cls: 'ico-sm' })}批量完成</button>
      <span class="field-hint">移动到</span>
      <div style="width:150px">${selectHtml('batchMove', [{ value: '', label: '选择项目…' }].concat(ui.projects.map((p) => ({ value: p.id, label: p.name }))), '', 'data-slot="batch-move" aria-label="批量移动到的项目"')}</div>
      <button class="btn btn-sm" type="button" data-act="batch-move" ${n ? '' : 'disabled'}>${icon('archive', { cls: 'ico-sm' })}移动</button>
      <button class="btn btn-sm btn-danger" type="button" data-act="batch-delete" ${n ? '' : 'disabled'}>${icon('trash', { cls: 'ico-sm' })}批量删除</button>
      <span style="flex:1"></span>
      <button class="btn btn-sm btn-ghost" type="button" data-act="toggle-select">退出多选（Esc）</button>
    </div>`;
  }

  /* ---------------- 行为：项目 ---------------- */

  /** 自动配色：优先取色板里还没用过的颜色，色板用尽后按数量轮换，保证列表里颜色尽量不撞。 */
  function nextProjectColor() {
    const used = new Set(ui.projects.map((p) => String(p.color || '').toLowerCase()));
    const free = PALETTE.find((c) => !used.has(c.toLowerCase()));
    return free || PALETTE[ui.projects.length % PALETTE.length];
  }

  function nextProjectIcon() {
    const used = new Set(ui.projects.map((p) => String(p.icon || '')));
    return PROJECT_ICON_NAMES.find((n) => !used.has(n)) || 'dot';
  }

  function paintNewDot() {
    slots.pNewDot.innerHTML = projectDot({ color: nextProjectColor(), colorMode: 'solid' });
  }

  async function quickCreateProject() {
    const name = (slots.pNew.value || '').trim();
    if (!name) { slots.pNew.focus(); return; }
    const color = nextProjectColor();
    const payload = { name, color, colorMode: 'solid', icon: nextProjectIcon() };
    slots.pNew.disabled = true;
    try {
      const created = await api.createProject(payload);
      track('project.create', {
        targetType: 'project', targetId: created && created.id,
        summary: `新建项目「${name}」（自动配色 ${color}）`,
        detail: { ...payload, via: 'quick' }
      });
      slots.pNew.value = '';
      toastOk(`已创建项目「${name}」`);
      if (created && created.id) { ui.projectId = created.id; remembered.projectId = created.id; ui.selected.clear(); }
      await loadProjects();
      await loadTasks();
      syncHash();
    } catch (err) {
      reportError(err, '新建项目失败');
    } finally {
      if (!disposed) { slots.pNew.disabled = false; slots.pNew.focus(); }
    }
  }

  async function moveProject(id, dir) {
    const arr = sortedProjects();
    const i = arr.findIndex((p) => p.id === id);
    if (i < 0) return;
    const j = i + dir;
    if (j < 0 || j >= arr.length) return;
    if (!!arr[i].pinned !== !!arr[j].pinned) return;  // 不跨越置顶 / 未置顶分组边界
    const reordered = [...arr];
    const tmp = reordered[i];
    reordered[i] = reordered[j];
    reordered[j] = tmp;
    const ids = reordered.map((p) => p.id);
    try {
      await api.reorderProjects(ids);
      reordered.forEach((p, idx) => {
        const target = ui.projects.find((x) => x.id === p.id);
        if (target) target.sortOrder = (idx + 1) * 1000;
      });
      const name = (arr[i].name || id);
      track('project.update', {
        targetType: 'project', targetId: id,
        summary: `调整项目排序：「${name}」${dir < 0 ? '上移' : '下移'}`,
        detail: { action: 'reorder', direction: dir, ids }
      });
      paintProjects();
    } catch (err) {
      reportError(err, '排序失败');
    }
  }

  async function deleteProject(project) {
    const run = async (cascade) => {
      try {
        await api.deleteProject(project.id, cascade);
        track('project.delete', {
          targetType: 'project', targetId: project.id,
          summary: `删除项目「${project.name || project.id}」${cascade ? '（连带任务）' : ''}`,
          detail: { cascade, name: project.name }
        });
        toastOk(`已删除项目「${project.name || ''}」`);
        if (ui.projectId === project.id) { ui.projectId = null; ui.selectedProject = null; }
        await loadProjects();
        await loadTasks();
        return true;
      } catch (err) {
        reportError(err, '删除项目失败');
        return false;
      }
    };

    const taskCount = Number(project.taskCount) || 0;
    if (taskCount === 0) {
      const ok = await confirmModal({
        title: '删除项目',
        message: `确定删除项目「${project.name || ''}」？`,
        detail: '该操作不可撤销，但项目下的任务本来为空。',
        confirmLabel: '删除项目'
      });
      if (ok) await run(false);
      return;
    }
    const cascade = await confirmModal({
      title: '该项目还有任务',
      message: `「${project.name || ''}」还有 ${fmtNumber(taskCount)} 个任务。`,
      detail: '确认 = 连带删除全部任务（cascade=1）；取消 = 保留。若只删项目不删任务，后端会返回 409 冲突。',
      confirmLabel: '连带删除全部任务'
    });
    if (cascade) await run(true);
  }

  /* ---------------- 项目编辑器 ---------------- */

  function projectModal(project = null) {
    const isNew = !project;
    const autoColor = nextProjectColor();
    const autoIcon = nextProjectIcon();
    const p = project || {
      name: '', description: '', color: autoColor, colorMode: 'solid',
      icon: autoIcon, expireAt: null, pinned: false, archived: false
    };
    let color = p.color || autoColor;
    let colorMode = p.colorMode === 'rainbow' ? 'rainbow' : 'solid';
    let iconName = p.icon || autoIcon;

    const html = `
      <div class="field"><label class="field-label" for="pm-name">名称</label>
        <input id="pm-name" type="text" maxlength="60" value="${esc(p.name || '')}" placeholder="例如：工作 / 生活 / 学习" data-autofocus>
        <p class="field-error pm-err" hidden>请填写项目名称。</p></div>
      <div class="field"><label class="field-label" for="pm-desc">描述</label>
        <input id="pm-desc" type="text" maxlength="200" value="${esc(p.description || '')}" placeholder="可选，显示在右侧任务区标题下"></div>
      <div class="field"><span class="field-label">颜色</span>
        <div class="color-swatches" role="group" aria-label="选择项目颜色">
          ${PALETTE.map((c) => `<button class="swatch" type="button" data-act="pick-color" data-color="${c}" style="background:${c}" aria-label="颜色 ${c}" aria-pressed="${String(c === color)}"></button>`).join('')}
        </div>
        <p class="field-hint">${isNew ? `已自动选用未占用的颜色 <code>${esc(color)}</code>，让每个项目都好区分。` : '换一个颜色即可和别的项目区分开。'}</p></div>
      <div class="field"><span class="field-label">${icon('sparkles', { cls: 'ico-sm' })}炫彩（渐变动画）</span>
        <label class="switch-row"><span class="sr-text"><span class="sr-title">炫彩项目（colorMode = rainbow）</span>
          <span class="sr-hint">开启后：圆点为旋转 conic 渐变，列表行底部出现动画发丝线；关闭则保持实色平面。</span></span>
          <span class="switch"><input type="checkbox" data-act="pick-rainbow" ${colorMode === 'rainbow' ? 'checked' : ''}><span class="switch-track" aria-hidden="true"></span></span>
        </label></div>
      <div class="field"><span class="field-label">图标</span>
        <div class="icon-picker" role="group" aria-label="选择项目图标">
          ${PROJECT_ICON_NAMES.map((n) => `<button class="icon-pick" type="button" data-act="pick-icon" data-icon="${n}" aria-label="图标 ${n}" aria-pressed="${String(n === iconName)}">${icon(n, { cls: 'ico ico-sm' })}</button>`).join('')}
        </div></div>
      <div class="field-row">
        <div class="field"><label class="field-label" for="pm-expire">过期时间</label>
          <input id="pm-expire" type="datetime-local" value="${esc(toInputValue(p.expireAt))}">
          <p class="field-hint">留空 = 永不过期。到期后项目列表会显示过期标记。</p></div>
        <div class="field" style="flex:0 0 auto;justify-content:flex-end">
          <button class="btn btn-ghost btn-sm" type="button" data-act="clear-expire">${icon('x', { cls: 'ico-sm' })}清除过期时间</button></div>
      </div>
      <div class="field-row">
        <label class="switch-row" style="flex:1 1 150px"><span class="sr-text"><span class="sr-title">置顶</span></span>
          <span class="switch"><input type="checkbox" data-slot="pm-pin" ${p.pinned ? 'checked' : ''}><span class="switch-track" aria-hidden="true"></span></span></label>
        <label class="switch-row" style="flex:1 1 150px"><span class="sr-text"><span class="sr-title">归档</span><span class="sr-hint">归档后默认不在项目列表中显示</span></span>
          <span class="switch"><input type="checkbox" data-slot="pm-arch" ${p.archived ? 'checked' : ''}><span class="switch-track" aria-hidden="true"></span></span></label>
      </div>
      <div class="note-box"><span data-slot="pm-preview">${projectDot({ color, colorMode })}</span>
        <span>预览：<b data-slot="pm-preview-name">${esc(p.name || '项目名称')}</b> · <span data-slot="pm-preview-mode">${colorMode === 'rainbow' ? '炫彩' : '实色'}</span></span></div>`;

    openModal({
      title: isNew ? '新建项目' : `编辑项目 · ${p.name || ''}`,
      html,
      wide: true,
      onMount: (body) => {
        const nameInput = body.querySelector('#pm-name');
        const refreshPreview = () => {
          const holder = body.querySelector('[data-slot="pm-preview"]');
          holder.innerHTML = projectDot({ color, colorMode });
          body.querySelector('[data-slot="pm-preview-name"]').textContent = nameInput.value.trim() || '项目名称';
          body.querySelector('[data-slot="pm-preview-mode"]').textContent = colorMode === 'rainbow' ? '炫彩' : '实色';
        };
        body.addEventListener('click', (e) => {
          const sw = e.target.closest('[data-act="pick-color"]');
          if (sw) {
            color = sw.dataset.color;
            body.querySelectorAll('[data-act="pick-color"]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.color === color)));
            refreshPreview();
            return;
          }
          const ic = e.target.closest('[data-act="pick-icon"]');
          if (ic) {
            iconName = ic.dataset.icon;
            body.querySelectorAll('[data-act="pick-icon"]').forEach((b) => b.setAttribute('aria-pressed', String(b.dataset.icon === iconName)));
            return;
          }
          if (e.target.closest('[data-act="clear-expire"]')) {
            body.querySelector('#pm-expire').value = '';
            return;
          }
          if (e.target.closest('[data-act="pick-rainbow"]')) {
            const box = body.querySelector('[data-act="pick-rainbow"]');
            colorMode = box.checked ? 'rainbow' : 'solid';
            refreshPreview();
          }
        });
        nameInput.addEventListener('input', refreshPreview);
        refreshPreview();
      },
      actions: [
        ...(isNew ? [] : [{ label: '删除项目', kind: 'danger', onClick: () => { deleteProject(project); } }]),
        { label: '取消', kind: 'ghost' },
        {
          label: isNew ? '创建项目' : '保存修改',
          kind: 'primary',
          onClick: async (h) => {
            const name = h.body.querySelector('#pm-name').value.trim();
            if (!name) {
              h.body.querySelector('.pm-err').hidden = false;
              h.body.querySelector('#pm-name').focus();
              return true;
            }
            const expireRaw = fromInputValue(h.body.querySelector('#pm-expire').value);
            const payload = {
              name,
              description: h.body.querySelector('#pm-desc').value.trim(),
              color,
              colorMode,
              icon: iconName,
              expireAt: expireRaw,
              pinned: h.body.querySelector('[data-slot="pm-pin"]').checked,
              archived: h.body.querySelector('[data-slot="pm-arch"]').checked
            };
            h.setFooterError('');
            try {
              if (isNew) {
                const created = await api.createProject(payload);
                track('project.create', {
                  targetType: 'project', targetId: created && created.id,
                  summary: `新建项目「${name}」`,
                  detail: { color, colorMode, expireAt: expireRaw, icon: iconName }
                });
                toastOk(`已创建项目「${name}」`);
                if (created && created.id) ui.projectId = created.id;
              } else {
                await api.updateProject(project.id, payload);
                track('project.update', { targetType: 'project', targetId: project.id, summary: `更新项目「${name}」`, detail: payload });
                if ((project.color || '') !== color) {
                  track('project.color.set', { targetType: 'project', targetId: project.id, summary: `项目「${name}」颜色设为 ${color}`, detail: { color } });
                }
                if ((project.colorMode || 'solid') !== colorMode) {
                  track('project.rainbow.toggle', { targetType: 'project', targetId: project.id, summary: `项目「${name}」炫彩${colorMode === 'rainbow' ? '开启' : '关闭'}`, detail: { colorMode } });
                }
                if ((project.expireAt || null) !== expireRaw) {
                  track('project.expire.set', {
                    targetType: 'project', targetId: project.id,
                    summary: expireRaw ? `项目「${name}」过期时间设为 ${fmtDateTime(expireRaw)}` : `项目「${name}」取消过期时间`,
                    detail: { expireAt: expireRaw }
                  });
                }
                toastOk('项目已保存');
              }
              await loadProjects();
              await loadTasks();
              return false;
            } catch (err) {
              h.setFooterError(err && err.message ? err.message : '保存失败');
              return true;
            }
          }
        }
      ]
    });
  }

  /* ---------------- 行为：任务 ---------------- */

  function quickDueValue() {
    if (!ui.quickDue) return null;
    const now = new Date();
    const mk = (d, hh, mm) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
      + `T${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}:00`;
    if (ui.quickDue === 'today') return mk(now, 18, 0);
    const d = new Date(now);
    if (ui.quickDue === 'tomorrow') { d.setDate(d.getDate() + 1); return mk(d, 9, 0); }
    if (ui.quickDue === 'nextweek') {
      const delta = (8 - d.getDay()) % 7 || 7;
      d.setDate(d.getDate() + delta);
      return mk(d, 9, 0);
    }
    return null;
  }

  async function quickAdd() {
    const input = slots.quickAdd;
    const title = input.value.trim();
    if (!title) { input.focus(); return; }
    if (!ui.projectId) { toastInfo('请先新建一个项目'); projectModal(null); return; }
    const d = ui.settings.defaults || {};
    const payload = {
      projectId: ui.projectId,
      title,
      priority: normalizePriority(d.priority),
      dueAt: quickDueValue()
    };
    input.value = '';
    try {
      const created = await api.createTask(payload);
      track('task.create', {
        targetType: 'task', targetId: created && created.id,
        summary: `快速新建任务「${title}」`,
        detail: payload
      });
      if (payload.dueAt) toastOk(`已添加「${title}」，截止 ${fmtDateTime(payload.dueAt)}`);
      else toastOk(`已添加「${title}」`);
      await Promise.all([loadTasks(), refreshProjectCounts()]);
      slots.quickAdd.focus();
    } catch (err) {
      input.value = title;
      reportError(err, '创建任务失败');
    }
  }

  async function refreshProjectCounts() {
    if (!ui.projectId) return;
    try {
      const fresh = await api.getProject(ui.projectId);
      const idx = ui.projects.findIndex((p) => p.id === ui.projectId);
      if (idx >= 0 && fresh) ui.projects[idx] = { ...ui.projects[idx], ...fresh };
      if (fresh && (!ui.selectedProject || ui.selectedProject.id !== fresh.id)) ui.selectedProject = null;
      paintProjects();
      paintTabs();
    } catch { /* 计数刷新失败不影响主流程 */ }
  }

  async function toggleComplete(task) {
    const wasDone = task.status === 'done' || task.status === 'canceled';
    try {
      if (wasDone) {
        await api.reopenTask(task.id);
        track('task.reopen', { targetType: 'task', targetId: task.id, summary: `重新打开任务「${task.title}」`, detail: { id: task.id } });
      } else {
        await api.completeTask(task.id);
        track('task.complete', { targetType: 'task', targetId: task.id, summary: `完成任务「${task.title}」`, detail: { id: task.id } });
      }
      await Promise.all([loadTasks(), refreshProjectCounts()]);
    } catch (err) {
      reportError(err, wasDone ? '重新打开失败' : '完成失败');
    }
  }

  async function cyclePriority(task) {
    const next = (normalizePriority(task.priority) + 1) % 4;
    try {
      await api.updateTask(task.id, { priority: next });
      task.priority = next;
      track('task.update', {
        targetType: 'task', targetId: task.id,
        summary: `任务「${task.title}」优先级改为${priorityLabel(next)}`,
        detail: { priority: next }
      });
      paintTasks();
    } catch (err) {
      reportError(err, '优先级更新失败');
    }
  }

  async function deleteTask(task) {
    const ok = await confirmModal({
      title: '删除任务',
      message: `确定删除任务「${task.title || ''}」？`,
      detail: '该操作不可撤销。',
      confirmLabel: '删除任务'
    });
    if (!ok) return;
    try {
      await api.deleteTask(task.id);
      track('task.delete', { targetType: 'task', targetId: task.id, summary: `删除任务「${task.title}」`, detail: { projectId: task.projectId } });
      ui.selected.delete(task.id);
      toastOk('任务已删除');
      await Promise.all([loadTasks(), refreshProjectCounts()]);
    } catch (err) {
      reportError(err, '删除失败');
    }
  }

  async function batch(action, extra = {}) {
    const ids = [...ui.selected];
    if (!ids.length) return;
    const titles = ids.map((id) => (findTask(id) || {}).title).filter(Boolean);
    const META = {
      complete: { act: 'task.complete', text: '批量完成', danger: false, confirm: `确定完成选中的 ${ids.length} 个任务？` },
      delete: { act: 'task.delete', text: '批量删除', danger: true, confirm: `确定删除选中的 ${ids.length} 个任务？该操作不可撤销。` },
      move: { act: 'task.update', text: '批量移动', danger: false, confirm: `把选中的 ${ids.length} 个任务移动到「${((ui.projects.find((p) => p.id === extra.projectId) || {}).name) || extra.projectId}」？` }
    };
    const m = META[action] || META.complete;
    const ok = await confirmModal({ title: m.text, message: m.confirm, confirmLabel: m.text, danger: m.danger });
    if (!ok) return;
    try {
      await api.batchTasks({ action, ids, projectId: extra.projectId || null });
      track(m.act, {
        targetType: 'task',
        summary: `${m.text} ${ids.length} 个任务：${preview(titles.join('、'), 80)}`,
        detail: { action, ids, projectId: extra.projectId || null, batch: true }
      });
      toastOk(`${m.text}：${ids.length} 个任务`);
      ui.selected.clear();
      await Promise.all([loadTasks(), refreshProjectCounts()]);
      paintBatchBar();
    } catch (err) {
      reportError(err, `${m.text}失败`);
    }
  }

  /* ---------------- 任务编辑器 ---------------- */

  function taskModal(task = null) {
    const isNew = !task;
    const t = task || {
      projectId: ui.projectId, title: '', notes: '',
      priority: normalizePriority((ui.settings.defaults || {}).priority),
      status: 'open', dueAt: null
    };

    const projectOptions = [{ value: '', label: '（未选择）' }]
      .concat(ui.projects.map((p) => ({ value: p.id, label: p.name })));

    const html = `
      <div class="field"><label class="field-label" for="tm-title">标题</label>
        <input id="tm-title" type="text" maxlength="200" value="${esc(t.title || '')}" placeholder="要做什么？" data-autofocus>
        <p class="field-error tm-err" hidden>请填写任务标题。</p></div>

      <div class="field"><label class="field-label" for="tm-notes">备注</label>
        <textarea id="tm-notes" placeholder="补充说明、链接、子步骤…">${esc(t.notes || '')}</textarea></div>

      <div class="field-row">
        <div class="field"><label class="field-label" for="tm-project">项目</label>
          ${selectHtml('tm-project', projectOptions, t.projectId || '', 'id="tm-project"')}</div>
        <div class="field" style="flex:0 0 168px"><span class="field-label">优先级</span>
          <div class="segmented" role="group" aria-label="优先级">
            ${PRIORITIES.map((p) => `<button type="button" data-act="pick-priority" data-v="${p.value}" aria-pressed="${String(normalizePriority(t.priority) === p.value)}">${esc(p.label)}</button>`).join('')}
          </div></div>
      </div>

      <div class="field"><label class="field-label" for="tm-due">${icon('calendar', { cls: 'ico-sm' })}截止日期时间</label>
        <div style="display:flex;gap:8px">
          <input id="tm-due" type="datetime-local" value="${esc(toInputValue(t.dueAt))}">
          <button class="btn btn-ghost" type="button" data-act="clear-due" title="清除截止时间">${icon('x', { cls: 'ico-sm' })}清除</button>
        </div>
        <p class="field-hint">留空 = 无截止时间，「今天 / 计划」视图不会包含该任务。</p></div>

      <div class="field-row">
        <div class="field" style="flex:0 0 170px"><span class="field-label">状态</span>
          ${selectHtml('tm-status', [{ value: 'open', label: '进行中' }, { value: 'done', label: '已完成' }, { value: 'canceled', label: '已取消' }], t.status || 'open', 'data-slot="tm-status"')}</div>
        <div class="field" style="justify-content:flex-end">
          <p class="field-hint">${isNew ? '留空截止时间 = 无截止时间。' : `创建 ${esc(fmtDateTime(t.createdAt, '—'))} · 更新 ${esc(fmtDateTime(t.updatedAt, '—'))}`}</p></div>
      </div>`;

    openModal({
      title: isNew ? '新任务' : '编辑任务',
      subtitle: isNew ? '' : `任务 ID ${t.id}`,
      html,
      wide: true,
      onMount: (body) => {
        const dueInput = body.querySelector('#tm-due');

        body.addEventListener('click', (e) => {
          const pr = e.target.closest('[data-act="pick-priority"]');
          if (pr) {
            body.querySelectorAll('[data-act="pick-priority"]').forEach((b) => b.setAttribute('aria-pressed', String(b === pr)));
            return;
          }
          if (e.target.closest('[data-act="clear-due"]')) {
            dueInput.value = '';
          }
        });
      },
      actions: [
        ...(isNew ? [] : [{
          label: '删除任务',
          kind: 'danger',
          onClick: async () => { await deleteTask(task); return false; }
        }]),
        { label: '取消', kind: 'ghost' },
        {
          label: isNew ? '创建任务' : '保存',
          kind: 'primary',
          onClick: async (h) => {
            const title = h.body.querySelector('#tm-title').value.trim();
            if (!title) {
              h.body.querySelector('.tm-err').hidden = false;
              h.body.querySelector('#tm-title').focus();
              return true;
            }
            const projectId = h.body.querySelector('#tm-project').value || ui.projectId;
            if (!projectId) { h.setFooterError('请选择所属项目'); return true; }

            const dueAt = fromInputValue(h.body.querySelector('#tm-due').value);
            const priorityBtn = h.body.querySelector('[data-act="pick-priority"][aria-pressed="true"]');
            const payload = {
              projectId,
              title,
              notes: h.body.querySelector('#tm-notes').value,
              priority: normalizePriority(priorityBtn ? priorityBtn.dataset.v : 0),
              status: h.body.querySelector('[data-slot="tm-status"]').value,
              dueAt
            };

            h.setFooterError('');
            try {
              if (isNew) {
                const created = await api.createTask(payload);
                track('task.create', { targetType: 'task', targetId: created && created.id, summary: `新建任务「${title}」`, detail: payload });
                toastOk(`已创建任务「${title}」`);
              } else {
                await api.updateTask(task.id, payload);
                track('task.update', { targetType: 'task', targetId: task.id, summary: `更新任务「${title}」`, detail: payload });
                if ((task.status === 'done') !== (payload.status === 'done')) {
                  track(payload.status === 'done' ? 'task.complete' : 'task.reopen', {
                    targetType: 'task', targetId: task.id,
                    summary: payload.status === 'done' ? `完成任务「${title}」` : `重新打开任务「${title}」`,
                    detail: { status: payload.status }
                  });
                }
                if ((task.projectId || null) !== projectId) {
                  track('task.update', { targetType: 'task', targetId: task.id, summary: `任务「${title}」移动到新项目`, detail: { from: task.projectId, to: projectId } });
                }
                toastOk('任务已保存');
              }
              await Promise.all([loadTasks(), refreshProjectCounts()]);
              return false;
            } catch (err) {
              h.setFooterError(err && err.message ? err.message : '保存失败');
              return true;
            }
          }
        }
      ]
    });
  }

  /* ---------------- 事件绑定 ---------------- */

  const offClick = delegate(root, {
    'project-select': (el) => {
      const id = el.dataset.id;
      if (!id) return;
      const project = ui.projects.find((p) => p.id === id);
      if (id !== ui.projectId) {
        ui.projectId = id;
        remembered.projectId = id;
        ui.selected.clear();
        track('ui.project.select', {
          targetType: 'project', targetId: id,
          summary: `选择项目「${(project && project.name) || id}」`,
          detail: { id, colorMode: project && project.colorMode }
        });
        document.body.classList.remove('projects-open');
        paintProjects();
        paintTabs();
        loadTasks();
        syncHash();
      }
    },
    'project-edit': (el, e) => {
      e.stopPropagation();
      const p = ui.projects.find((x) => x.id === el.closest('.p-row').dataset.id);
      if (p) projectModal(p);
    },
    'project-move': (el, e) => {
      e.stopPropagation();
      const p = ui.projects.find((x) => x.id === el.closest('.p-row').dataset.id);
      if (p) moveProject(p.id, Number(el.dataset.dir));
    },
    'new-project': () => projectModal(null),
    'retry-projects': () => loadProjects(),
    'retry-tasks': () => loadTasks(),
    'retry-state': () => window.dispatchEvent(new CustomEvent('app:retry-state')),
    'toggle-archived': (el) => {
      ui.includeArchived = !!el.checked;
      remembered.includeArchived = ui.includeArchived;
      loadProjects();
    },
    'open-drawer': () => { document.body.classList.add('projects-open'); },
    'close-drawer': () => { document.body.classList.remove('projects-open'); },
    tab: (el) => {
      const key = el.dataset.key;
      if (key === ui.tab) return;
      ui.tab = key;
      remembered.tab = key;
      ui.selected.clear();
      const label = (TABS.find((t) => t.key === key) || {}).label || key;
      track('ui.view.switch', { targetType: 'task', summary: `任务视图切换到「${label}」`, detail: { scope: 'tasks-tab', tab: key } });
      loadTasks();
    },
    'quick-add': () => quickAdd(),
    'quick-due': (el) => {
      ui.quickDue = el.dataset.key || null;
      paintQuickExtra();
    },
    'load-more': async () => {
      const tab = TABS.find((t) => t.key === ui.tab) || TABS[0];
      try {
        const more = await api.listTasks({
          projectId: ui.projectId || '', status: tab.status, view: tab.view,
          q: ui.taskQ, limit: TASK_LIMIT, offset: ui.offset
        });
        const items = more && Array.isArray(more.items) ? more.items : [];
        ui.tasks = ui.tasks.concat(items);
        ui.offset = ui.tasks.length;
        trimSelection();
        paintTasks();
      } catch (err) {
        reportError(err, '加载更多失败');
      }
    },
    'toggle-done': () => {
      remembered.doneCollapsed = !remembered.doneCollapsed;
      paintTasks();
    },
    'task-toggle': (el, e) => {
      e.stopPropagation();
      const task = findTask(el.closest('[data-id]').dataset.id);
      if (task) toggleComplete(task);
    },
    'task-flag': (el, e) => {
      e.stopPropagation();
      const task = findTask(el.closest('[data-id]').dataset.id);
      if (task) cyclePriority(task);
    },
    'task-edit': (el, e) => {
      e.stopPropagation();
      const holder = el.closest('[data-id]');
      if (!holder) return;
      const task = findTask(holder.dataset.id);
      if (task) taskModal(task);
    },
    'task-delete': (el, e) => {
      e.stopPropagation();
      const holder = el.closest('[data-id]');
      if (!holder) return;
      const task = findTask(holder.dataset.id);
      if (task) deleteTask(task);
    },
    'task-select': (el, e) => {
      e.stopPropagation();
      const holder = el.closest('[data-id]');
      if (holder) toggleSelect(holder.dataset.id, holder);
    },
    'toggle-select': () => {
      ui.selectMode = !ui.selectMode;
      if (!ui.selectMode) ui.selected.clear();
      const btn = root.querySelector('[data-act="toggle-select"]');
      if (btn) {
        btn.classList.toggle('btn-primary', ui.selectMode);
        btn.classList.toggle('btn-ghost', !ui.selectMode);
        btn.setAttribute('aria-pressed', String(ui.selectMode));
      }
      track('ui.view.switch', {
        targetType: 'task',
        summary: ui.selectMode ? '进入任务多选模式' : '退出任务多选模式',
        detail: { mode: 'batch', on: ui.selectMode }
      });
      paintTasks();
    },
    'select-all': () => {
      if (ui.selected.size === ui.tasks.length && ui.tasks.length) ui.selected.clear();
      else ui.tasks.forEach((t) => ui.selected.add(t.id));
      paintTasks();
    },
    'batch-complete': () => batch('complete'),
    'batch-delete': () => batch('delete'),
    'batch-move': () => {
      const sel = root.querySelector('[data-slot="batch-move"]');
      if (!sel || !sel.value) { toastInfo('请先选择目标项目'); return; }
      batch('move', { projectId: sel.value });
    },
    'new-task': () => taskModal(null)
  });
  cleanups.push(offClick);

  function toggleSelect(id, holder) {
    if (!id) return;
    if (ui.selected.has(id)) ui.selected.delete(id);
    else ui.selected.add(id);
    holder.classList.toggle('is-selected', ui.selected.has(id));
    const box = holder.querySelector('.check');
    if (box) {
      box.classList.toggle('is-checked', ui.selected.has(id));
      box.setAttribute('aria-pressed', String(ui.selected.has(id)));
    }
    const bar = root.querySelector('.batch-bar');
    if (bar) {
      const n = ui.selected.size;
      const count = bar.querySelector('.count');
      if (count) count.textContent = `已选 ${n}`;
      bar.querySelectorAll('[data-act^="batch-"]').forEach((b) => { b.disabled = !n; });
    }
  }

  slots.quickAdd.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); quickAdd(); }
    if (e.key === 'Escape') { e.target.value = ''; e.target.blur(); }
  });
  slots.quickAdd.addEventListener('focus', () => {
    track('ui.task.quick_add_focus', {
      targetType: 'project', targetId: ui.projectId || '',
      summary: '聚焦快速添加输入框', detail: { projectId: ui.projectId || null }
    });
  });

  slots.pNew.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') { e.preventDefault(); quickCreateProject(); }
    if (e.key === 'Escape') { e.target.value = ''; e.target.blur(); }
  });

  slots.taskSearch.addEventListener('input', debounce(() => {
    ui.taskQ = slots.taskSearch.value.trim();
    loadTasks();
  }, 320));

  root.addEventListener('keydown', (e) => {
    const row = e.target instanceof Element ? e.target.closest('.p-row[tabindex="0"]') : null;
    if (!row) return;
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); row.click(); }
  });

  function syncHash() {
    const target = ui.projectId ? `#/tasks?project=${encodeURIComponent(ui.projectId)}` : '#/tasks';
    if (location.hash !== target) history.replaceState(null, '', target);
  }

  const onKey = (e) => {
    if (disposed || e.defaultPrevented) return;
    const tag = (e.target && e.target.tagName) || '';
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || (e.target && e.target.isContentEditable)) return;
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    if (e.key === 'n' || e.key === 'N') {
      e.preventDefault();
      slots.quickAdd.focus();
      track('ui.task.quick_add_focus', { targetType: 'project', targetId: ui.projectId || '', summary: '快捷键 n 聚焦快速添加', detail: { via: 'keyboard' } });
    } else if (e.key === '/') {
      e.preventDefault();
      slots.pNew.focus();
      slots.pNew.select();
    } else if (e.key === 'Escape' && ui.selectMode) {
      ui.selectMode = false;
      ui.selected.clear();
      const btn = root.querySelector('[data-act="toggle-select"]');
      if (btn) { btn.classList.remove('btn-primary'); btn.classList.add('btn-ghost'); btn.setAttribute('aria-pressed', 'false'); }
      paintTasks();
    }
  };
  document.addEventListener('keydown', onKey);
  cleanups.push(() => document.removeEventListener('keydown', onKey));

  const offSettings = on('settings', (data) => {
    ui.settings = data || defaultSettings();
    paintQuickExtra();
  });
  cleanups.push(offSettings);

  loadProjects().then(() => {
    if (disposed) return;
    syncHash();
    return loadTasks();
  });

  return function cleanup() {
    disposed = true;
    cleanups.reverse().forEach((fn) => { try { fn(); } catch { /* 忽略清理异常 */ } });
  };
}
