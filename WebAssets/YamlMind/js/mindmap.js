/* ==========================================================================
   Mind-map viewer — layout engine, SVG renderer, pan/zoom, live sync
   ========================================================================== */
(function () {
  'use strict';
  const { api, icon, esc, toast, qs, state, debounce } = YM;

  /* ------------------------------------------------------------------ config */
  const CFG = {
    vgap: 11,                  // vertical gap between siblings
    hgap: [46, 36, 30, 26],    // horizontal gap after depth 1,2,3,4+
    pad: [                     // [padX, padY] per depth
      [20, 13], [15, 10.5], [13, 8.5], [11, 6.5],
    ],
    maxw: [430, 268, 240, 222],
    radius: [15, 12, 10, 9],
    font: [17.5, 14.6, 13.2, 12.3],
    lh: 1.42,
    autoCollapseNodes: 260,    // fold deeper levels automatically above this
    autoCollapseDepth: 3,
    minZoom: 0.12, maxZoom: 2.8,
    toggleR: 7.2,
  };

  const CJK = /[\u3000-\u303f\u3040-\u30ff\u31f0-\u31ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uff00-\uffef]/;
  const PALETTE = ['--b1', '--b2', '--b3', '--b4', '--b5', '--b6', '--b7', '--b8'];

  /* ------------------------------------------------------------------- state */
  let docId = qs('id');
  let tree = null, meta = null, revision = 0;
  let layout = localStorage.getItem('ym:layout') || 'radial';
  let view = { x: 0, y: 0, k: 1 };
  let fitted = true;
  let nodes = [], links = [], byId = {}, root = null;
  let collapsed = new Set();
  let activeId = null;
  let branchIds = new Set();
  let hueCache = {};
  let pollTimer = null;

  const $ = (id) => document.getElementById(id);
  const svg = $('map'), gWorld = $('world'), gLinks = $('links'), gNodes = $('nodes');
  const mctx = document.createElement('canvas').getContext('2d');

  /* font stacks must match tokens.css exactly, or canvas measuring drifts from
     what the browser actually paints and text gets clipped in its node box */
  const cssVar = (n, fb) => (getComputedStyle(document.documentElement).getPropertyValue(n) || '').trim() || fb;
  let FONT_STACK = '', DISPLAY_STACK = '', MONO_STACK = '';
  function loadFontStacks() {
    FONT_STACK = cssVar('--font-ui', 'system-ui, sans-serif');
    DISPLAY_STACK = cssVar('--font-display', 'system-ui, sans-serif');
    MONO_STACK = cssVar('--font-mono', 'monospace');
  }
  function fontOf(d, kind) {
    d = Math.min(d, 3);
    if (kind === 'num') return '700 ' + (d === 1 ? 11.5 : 10.5) + 'px ' + MONO_STACK;
    if (kind === 'value') return '500 11px ' + MONO_STACK;
    if (kind === 'note') return '500 10.6px ' + FONT_STACK;
    const w = d === 0 ? 800 : d === 1 ? 700 : d === 2 ? 650 : 550;
    return w + ' ' + CFG.font[d] + 'px ' + (d === 0 ? DISPLAY_STACK : FONT_STACK);
  }

  /* --------------------------------------------------- text measure + wrap */
  let measureCache = null;
  function measure(text, font) {
    const key = font + '\u0000' + text;
    const hit = measureCache.get(key);
    if (hit !== undefined) return hit;
    mctx.font = font;
    const w = mctx.measureText(text).width;
    measureCache.set(key, w);
    return w;
  }

  /** Split text into wrap atoms: CJK chars individually, latin words, whitespace. */
  function atoms(text) {
    const out = [];
    let buf = '';
    for (const ch of String(text)) {
      if (CJK.test(ch)) {
        if (buf) { out.push(buf); buf = ''; }
        out.push(ch);
      } else if (ch === ' ') {
        if (buf) { out.push(buf); buf = ''; }
        out.push(' ');
      } else {
        buf += ch;
      }
    }
    if (buf) out.push(buf);
    return out;
  }

  /** Greedy wrap; `first` is the width available on line 1, `rest` afterwards. */
  function wrapText(text, first, rest, font, maxLines) {
    const lines = [];
    let line = '', w = 0, avail = first;
    const list = atoms(text);
    for (let i = 0; i < list.length; i++) {
      const a = list[i];
      const aw = measure(a, font);
      if (w + aw > avail + 0.4 && line) {
        lines.push(line.replace(/\s+$/, ''));
        if (lines.length >= maxLines) return finish(lines, list, i, font);
        line = a === ' ' ? '' : a;
        w = line ? aw : 0;
        avail = rest;
      } else {
        line += a; w += aw;
      }
    }
    if (line.trim()) lines.push(line.replace(/\s+$/, ''));
    if (!lines.length) lines.push('');
    return lines;

    function finish(acc, all, from, f) {
      let tail = all.slice(from).join('').trim();
      if (tail) acc[acc.length - 1] += '…';
      void f;
      return acc;
    }
  }

  /* ------------------------------------------------------------- node model */
  function rootHue() {
    if (hueCache.root === undefined) {
      hueCache.root = getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() || '#f2a93b';
    }
    return hueCache.root;
  }

  function hueFor(branchIndex) {
    if (hueCache[branchIndex] === undefined) {
      const name = PALETTE[branchIndex % PALETTE.length];
      hueCache[branchIndex] = getComputedStyle(document.documentElement)
        .getPropertyValue(name).trim() || '#f2a93b';
    }
    return hueCache[branchIndex];
  }

  function buildLayout() {
    nodes = []; links = []; byId = {}; hueCache = {};
    if (!tree || !tree.root) return;
    loadFontStacks();
    measureCache = new Map();
    let idx = 0;
    root = visit(tree.root, null, 0, 0);

    function visit(src, parent, depth, branchIndex) {
      const n = {
        uid: 'u' + (idx++),
        id: src.id || ('u' + idx),
        parent, depth, branch: depth === 0 ? -1 : branchIndex,
        title: src.title || '',
        value: src.value || '',
        note: src.note || '',
        num: src.num || '',
        line: src.line || 0,
        kids: [],
        collapsed: false,
        x: 0, y: 0, w: 0, h: 0, cy: 0, side: 1,
      };
      const kids = src.children || [];
      n.kidsSrc = kids;
      n.descendants = 0;
      kids.forEach((k, i) => {
        const child = visit(k, n, depth + 1, depth === 0 ? i : branchIndex);
        n.kids.push(child);
        n.descendants += 1 + child.descendants;
      });
      sizeNode(n);
      byId[n.id] = n;
      nodes.push(n);
      return n;
    }
    // persisted collapse state + auto-fold for very large maps
    const saved = JSON.parse(localStorage.getItem('ym:collapsed:' + docId) || 'null');
    if (Array.isArray(saved)) collapsed = new Set(saved);
    else if (nodes.length > CFG.autoCollapseNodes) autoFold();
    nodes.forEach((n) => { n.collapsed = collapsed.has(n.id); });
  }

  function autoFold() {
    nodes.forEach((n) => {
      if (n.depth >= CFG.autoCollapseDepth && n.kids.length) collapsed.add(n.id);
    });
  }

  function sizeNode(n) {
    const d = Math.min(n.depth, 3);
    const padX = CFG.pad[d][0], padY = CFG.pad[d][1];
    const maxw = CFG.maxw[d];
    const titleW = maxw - padX * 2 - (n.depth === 0 ? 0 : 0);
    const tf = fontOf(d);
    const numText = n.depth ? n.num : '';
    const numW = numText ? measure(numText + '\u2002', fontOf(d, 'num')) : 0;
    const lines = wrapText(n.title, titleW - numW, titleW, tf, 4);

    let h = padY * 2 + lines.length * CFG.font[d] * CFG.lh;
    const vw = titleW;
    let valueLines = [];
    if (n.value) {
      valueLines = wrapText(n.value, vw - 12, vw - 12, fontOf(d, 'value'), 2);
      h += valueLines.length * (11 * 1.45) + 3;
    }
    if (n.note) {
      const noteLines = wrapText(n.note, vw - 4, vw - 4, fontOf(d, 'note'), 2);
      h += noteLines.length * (10.6 * 1.45) + 3;
      n.noteLines = noteLines;
    } else n.noteLines = [];

    let w = 0;
    lines.forEach((l, i) => { w = Math.max(w, measure(l, tf) + (i === 0 ? numW : 0)); });
    valueLines.forEach((l) => { w = Math.max(w, measure(l, fontOf(d, 'value')) + 12); });
    n.noteLines.forEach((l) => { w = Math.max(w, measure(l, fontOf(d, 'note')) + 4); });
    w = Math.max(w + padX * 2, n.depth === 0 ? 96 : 46);

    n.lines = lines; n.valueLines = valueLines;
    /* never clamp below the measured text width — that is what clips labels */
    n.w = Math.round(Math.min(w, maxw + numW + 16));
    n.h = Math.round(h + (n.depth >= 2 ? 1 : 2));
    n.padX = padX; n.padY = padY;
  }

  /* ---------------------------------------------------------------- layout */
  const visibleKids = (n) => (n.collapsed ? [] : n.kids);

  /** one post-order pass, because subtreeH() is used all over the layout loop */
  function measureHeights(vgap) {
    const calc = (n) => {
      const kids = visibleKids(n);
      if (!kids.length) { n.sh = n.h; return n.sh; }
      let t = 0;
      kids.forEach((k, i) => { t += calc(k) + (i ? vgap : 0); });
      n.sh = Math.max(n.h, t);
      return n.sh;
    };
    calc(root);
  }

  function computeLayout() {
    if (!root) return;
    const vgap = layout === 'tree' ? 13 : CFG.vgap;
    const hgap = (d) => CFG.hgap[Math.min(d - 1, CFG.hgap.length - 1)];
    measureHeights(vgap);

    const sides = layout === 'tree' ? { right: root.kids, left: [] } : splitBranches(root.kids);

    /* column width per side, per depth */
    const colW = { 1: {}, '-1': {} };
    const collect = (list, side) => list.forEach((k) => {
      const step = (n) => {
        colW[side][n.depth] = Math.max(colW[side][n.depth] || 0, n.w);
        visibleKids(n).forEach(step);
      };
      step(k);
    });
    collect(sides.right, 1);
    collect(sides.left, -1);

    /* column x offsets (distance from the root centre to the node edge) */
    const colX = { 1: {}, '-1': {} };
    [1, -1].forEach((side) => {
      if (!(side === 1 ? sides.right : sides.left).length) return;
      let x = root.w / 2 + hgap(1);
      colX[side][1] = x;
      for (let d = 2; d <= 16; d++) {
        const prevW = colW[side][d - 1];
        if (!prevW) break;
        x += prevW + hgap(d);
        colX[side][d] = x;
      }
    });

    /* root sits at the vertical centre of the whole map */
    root.side = 0;
    root.cy = 0;
    root.x = -root.w / 2;
    root.y = -root.h / 2;

    const place = (n, side) => {
      n.side = side;
      const edge = colX[side === 1 ? 1 : '-1'][n.depth] || (root.w / 2 + hgap(1));
      n.x = side === 1 ? edge : -(edge + n.w);
      n.y = n.cy - n.h / 2;
      const kids = visibleKids(n);
      if (!kids.length) return;
      let total = 0;
      kids.forEach((k, i) => { total += k.sh + (i ? vgap : 0); });
      let top = n.cy - total / 2;
      kids.forEach((k) => {
        k.cy = top + k.sh / 2;
        top += k.sh + vgap;
        place(k, side);
      });
    };

    const stack = (list, side) => {
      let total = 0;
      list.forEach((k, i) => { total += k.sh + (i ? vgap : 0); });
      let top = -total / 2;
      list.forEach((k) => {
        k.cy = top + k.sh / 2;
        top += k.sh + vgap;
        place(k, side);
      });
    };

    stack(sides.right, 1);
    stack(sides.left, -1);
    buildLinks();
  }

  function buildLinks() {
    links = [];
    nodes.forEach((n) => visibleKids(n).forEach((k) => links.push([n, k])));
  }

  /**
   * Balance the two halves so the radial map looks even.
   *
   * Mass is the *measured subtree height* (k.sh, already computed), not the node
   * count — a branch with three tall wrapped labels weighs more than ten short
   * ones. For <= 14 branches every split is evaluated and the best one wins;
   * above that we fall back to greedy. Order inside each side is always the
   * original YAML order, so numbering reads top-to-bottom.
   */
  function splitBranches(kids) {
    const n = kids.length;
    if (!n) return { right: [], left: [] };
    if (n === 1) return { right: [kids[0]], left: [] };

    const mass = kids.map((k) => Math.max(1, k.sh || k.descendants + 1));
    const total = mass.reduce((a, b) => a + b, 0);
    let best = null;

    const consider = (pick) => {
      let rs = 0, cntR = 0;
      for (let i = 0; i < n; i++) if (pick[i]) { rs += mass[i]; cntR++; }
      if (!cntR || cntR === n) return;                    // never leave a side empty
      const key = Math.abs(2 * rs - total) * 1000         // height imbalance dominates
        + Math.abs(n - 2 * cntR) * 24                     // then branch-count imbalance
        + (pick[0] ? 0 : 8);                              // prefer 一、 on the right
      if (!best || key < best.key) best = { key, pick: pick.slice() };
    };

    if (n <= 14) {
      const lim = 1 << n;
      for (let m = 1; m < lim; m++) {
        const pick = new Array(n);
        for (let i = 0; i < n; i++) pick[i] = !!(m & (1 << i));
        consider(pick);
      }
    } else {
      const pick = new Array(n).fill(false);
      let rw = 0, lw = 0;
      for (let i = 0; i < n; i++) {
        if (rw <= lw) { pick[i] = true; rw += mass[i]; } else lw += mass[i];
      }
      consider(pick);
    }
    if (!best) return { right: kids.slice(), left: [] };

    const right = [], left = [];
    kids.forEach((k, i) => (best.pick[i] ? right : left).push(k));
    return { right, left };
  }

  /* ---------------------------------------------------------------- render */
  let visSet = null;
  function markVisible() {
    const vis = new Set();
    const walk = (n) => { vis.add(n); if (!n.collapsed) n.kids.forEach(walk); };
    if (root) walk(root);
    return vis;
  }

  const visible = (n) => visSet ? visSet.has(n) : true;

  function numTextOf(n) {
    if (!n.depth) return '';
    return /\s$/.test(n.num) ? n.num : n.num + '\u2002';
  }

  function nodeMarkup(n, i, animate) {
    const d = Math.min(n.depth, 3);
    const hue = n.depth === 0 ? rootHue() : hueFor(n.branch);
    const r = CFG.radius[d];
    const kids = n.kids.length;
    const lh = CFG.font[d] * CFG.lh;
    const tx = n.padX;
    const num = numTextOf(n);
    n.numText = num;

    const style = '--hue:' + hue + (animate ? ';animation-delay:' + Math.min(i * 6, 620) + 'ms' : '');
    const cls = 'node' + (animate ? ' is-in' : '') + (activeId === n.id ? ' is-active' : '') +
      (branchIds.has(n.uid) ? ' is-branch' : '');
    const out = ['<g class="' + cls + '" data-uid="' + n.uid + '" data-id="' + esc(n.id) +
      '" style="' + style + '" transform="translate(' + n.x.toFixed(1) + ',' + n.y.toFixed(1) + ')">'];

    out.push('<title>' + esc((num ? num : '') + n.title + (n.value ? '：' + n.value : '') +
      (kids ? (n.collapsed ? ' · 点击展开 ' + n.descendants + ' 个子节点' : ' · 点击折叠') : ' · 叶子节点') +
      (n.line ? ' · YAML 第 ' + n.line + ' 行' : '')) + '</title>');

    out.push('<rect class="nbox nbox--' + d + '" width="' + n.w + '" height="' + n.h + '" rx="' + r + '"' +
      (d < 3 ? ' filter="url(#mshadow)"' : '') + '/>');
    if (d >= 2) {
      const barH = Math.max(8, n.h - Math.min(16, n.h * 0.34));
      out.push('<rect class="nbar" x="0" y="' + ((n.h - barH) / 2).toFixed(1) + '" width="2.6" height="' +
        barH.toFixed(1) + '" rx="1.3"/>');
    }

    let t = '<text>';
    n.lines.forEach((line, li) => {
      const y = (n.padY + lh * (li + 0.79)).toFixed(1);
      if (li === 0 && num) {
        t += '<tspan class="nnum nnum--' + d + '" x="' + tx + '" y="' + y + '">' + esc(num) + '</tspan>' +
          '<tspan class="ntitle ntitle--' + d + '">' + esc(line) + '</tspan>';
      } else {
        t += '<tspan class="ntitle ntitle--' + d + '" x="' + tx + '" y="' + y + '">' + esc(line) + '</tspan>';
      }
    });
    let extra = n.padY + lh * n.lines.length + 3;
    n.valueLines.forEach((line, li) => {
      t += '<tspan class="nvalue" x="' + tx + '" y="' + (extra + 11 * 1.45 * (li + 0.72)).toFixed(1) + '">' +
        esc(line) + '</tspan>';
    });
    if (n.valueLines.length) extra += n.valueLines.length * (11 * 1.45);
    n.noteLines.forEach((line, li) => {
      t += '<tspan class="nnote" x="' + tx + '" y="' + (extra + 10.6 * 1.45 * (li + 0.72)).toFixed(1) + '">' +
        esc(line) + '</tspan>';
    });
    t += '</text>';
    out.push(t);

    if (kids) {
      const tx2 = n.side < 0 ? -14 : n.w + 14;
      out.push('<g class="tog' + (n.collapsed ? ' is-collapsed' : '') + '" data-tog="1" transform="translate(' +
        tx2 + ',' + (n.h / 2).toFixed(1) + ')"><circle r="' + CFG.toggleR + '"/><text y="0.5">' +
        (n.collapsed ? '+' : '−') + '</text></g>');
    }
    out.push('</g>');
    return out.join('');
  }

  function linkPath(a, b) {
    const ax = b.side > 0 ? a.x + a.w : a.x;
    const ay = a.y + a.h / 2;
    const bx = b.side > 0 ? b.x : b.x + b.w;
    const by = b.y + b.h / 2;
    const s = b.side > 0 ? 1 : -1;
    const dx = Math.abs(bx - ax);
    const c = layout === 'tree' ? Math.min(58, dx * 0.6) : dx * 0.52;
    return 'M' + ax.toFixed(1) + ' ' + ay.toFixed(1) + 'C' + (ax + s * c).toFixed(1) + ' ' + ay.toFixed(1) +
      ',' + (bx - s * c).toFixed(1) + ' ' + by.toFixed(1) + ',' + bx.toFixed(1) + ' ' + by.toFixed(1);
  }

  function linkMarkup(pair) {
    const [a, b] = pair;
    const d = Math.min(b.depth, 4);
    const w = d <= 1 ? 2.6 : d === 2 ? 1.9 : d === 3 ? 1.4 : 1.15;
    const hue = b.depth === 0 ? rootHue() : hueFor(b.branch);
    const cls = 'link' + (branchIds.has(b.uid) || branchIds.has(a.uid) ? ' is-branch' : '');
    return '<path class="' + cls + '" style="--hue:' + hue + ';stroke-width:' + w + ';stroke-opacity:' +
      (d <= 1 ? 0.72 : d === 2 ? 0.55 : 0.4) + '" d="' + linkPath(a, b) + '"/>';
  }

  function render(animate) {
    if (!root) return;
    computeLayout();
    visSet = markVisible();
    const ns = nodes.filter((n) => visSet.has(n));
    gNodes.innerHTML = ns.map((n, i) => nodeMarkup(n, i, animate)).join('');
    gLinks.innerHTML = links.map(linkMarkup).join('');
    applyTransform();
    updateStats(ns.length);
    resetHoverCache();
    if (fitted) fit(false);
  }

  function applyTransform() {
    gWorld.setAttribute('transform',
      'translate(' + view.x.toFixed(2) + ',' + view.y.toFixed(2) + ') scale(' + view.k.toFixed(4) + ')');
    const z = $('zoomVal');
    if (z) z.textContent = Math.round(view.k * 100) + '%';
  }

  function bbox() {
    const vis = nodes.filter((n) => visible(n));
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    vis.forEach((n) => {
      x0 = Math.min(x0, n.x); y0 = Math.min(y0, n.y);
      x1 = Math.max(x1, n.x + n.w); y1 = Math.max(y1, n.y + n.h);
    });
    if (!vis.length) return { x: 0, y: 0, w: 100, h: 100 };
    return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
  }

  function fit(smooth) {
    const b = bbox();
    const W = svg.clientWidth, H = svg.clientHeight;
    const padT = 96, padB = 64, padX = 56;
    const k = Math.max(CFG.minZoom, Math.min(CFG.maxZoom,
      Math.min((W - padX * 2) / b.w, (H - padT - padB) / b.h)));
    const target = {
      k,
      x: (W - b.w * k) / 2 - b.x * k,
      y: padT + ((H - padT - padB) - b.h * k) / 2 - b.y * k,
    };
    if (!smooth) { view = target; applyTransform(); return; }
    animateView(target);
  }

  function animateView(to) {
    const from = Object.assign({}, view);
    const t0 = performance.now(), dur = 320;
    (function step(t) {
      const p = Math.min(1, (t - t0) / dur);
      const e = 1 - Math.pow(1 - p, 3);
      view = { x: from.x + (to.x - from.x) * e, y: from.y + (to.y - from.y) * e, k: from.k + (to.k - from.k) * e };
      applyTransform();
      if (p < 1) requestAnimationFrame(step);
    })(t0);
  }

  function zoomAt(factor, cx, cy) {
    const k = Math.max(CFG.minZoom, Math.min(CFG.maxZoom, view.k * factor));
    const ratio = k / view.k;
    view = { k, x: cx - (cx - view.x) * ratio, y: cy - (cy - view.y) * ratio };
    fitted = false;
    applyTransform();
  }

  /* ----------------------------------------------------------- interaction */
  function bindStage() {
    const stage = $('stage');
    let drag = null, pinch = null;

    stage.addEventListener('pointerdown', (e) => {
      // left: interact + pan · right: grab & pan only · middle: ignore
      if (e.pointerType === 'mouse' && e.button !== 0 && e.button !== 2) return;
      const panOnly = e.button === 2;                 // right button = pure grab-drag
      const tog = e.target.closest && e.target.closest('[data-tog]');
      const nodeEl = e.target.closest && e.target.closest('.node');
      if (tog && nodeEl && !panOnly) {
        e.preventDefault();
        toggleNode(nodeEl.dataset.id);
        return;
      }
      // never let the browser start a text selection or a native image drag
      e.preventDefault();
      try {
        if (e.pointerId != null) stage.setPointerCapture(e.pointerId);
      } catch (err) { /* synthetic events carry no pointerId */ }
      drag = { id: e.pointerId == null ? 'mouse' : e.pointerId, panOnly,
               x: e.clientX, y: e.clientY, vx: view.x, vy: view.y, moved: 0, nodeEl };
      svg.classList.add('is-dragging');
      stage.classList.add('is-pan');
      hideHint();
    });

    stage.addEventListener('pointermove', (e) => {
      if (!drag) return;
      const mid = e.pointerId == null ? 'mouse' : e.pointerId;
      if (mid !== drag.id) return;
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
      drag.moved = Math.max(drag.moved, Math.abs(dx) + Math.abs(dy));
      view.x = drag.vx + dx; view.y = drag.vy + dy;
      fitted = false;
      applyTransform();
    });

    const endDrag = (e) => {
      if (!drag) return;
      const pid = e.pointerId == null ? 'mouse' : e.pointerId;
      if (pid !== drag.id) return;
      svg.classList.remove('is-dragging');
      stage.classList.remove('is-pan');
      const wasClick = drag.moved < 5;
      const nodeEl = drag.nodeEl;
      const purePan = drag.panOnly;
      drag = null;
      if (!wasClick || purePan) return;      // a right-button drag never selects
      if (nodeEl) {
        selectNode(nodeEl.dataset.id);
      } else {
        clearSelection();
      }
    };
    stage.addEventListener('pointerup', endDrag);
    stage.addEventListener('pointercancel', endDrag);
    stage.addEventListener('lostpointercapture', (e) => {
      const pid = e.pointerId == null ? 'mouse' : e.pointerId;
      if (drag && pid === drag.id) endDrag(e);
    });

    stage.addEventListener('contextmenu', (e) => { e.preventDefault(); });

    stage.addEventListener('wheel', (e) => {
      e.preventDefault();
      const rect = svg.getBoundingClientRect();
      const cx = e.clientX - rect.left, cy = e.clientY - rect.top;
      const f = Math.pow(e.deltaY < 0 ? 1.12 : 1 / 1.12, e.ctrlKey ? 2.2 : 1);
      zoomAt(f, cx, cy);
      hideHint();
    }, { passive: false });

    stage.addEventListener('dblclick', (e) => {
      const nodeEl = e.target.closest && e.target.closest('.node');
      if (nodeEl) { focusNode(nodeEl.dataset.id); } else { fitted = true; fit(true); }
    });

    stage.addEventListener('pointerover', (e) => {
      const nodeEl = e.target.closest && e.target.closest('.node');
      if (!nodeEl) return;
      highlightBranch(nodeEl.dataset.id);
    });
    stage.addEventListener('pointerout', (e) => {
      const nodeEl = e.target.closest && e.target.closest('.node');
      if (!nodeEl || (e.relatedTarget && e.relatedTarget.closest && e.relatedTarget.closest('.node'))) return;
      highlightBranch(null);
    });

    window.addEventListener('resize', debounce(() => { if (fitted) fit(false); }, 140));
    void pinch;
  }

  let lastHover = null;
  function highlightBranch(id) {
    if (id === lastHover) return;
    lastHover = id;
    const n = id ? byId[id] : null;
    const ids = new Set();
    for (let cur = n; cur; cur = cur.parent) ids.add(cur.uid);
    if (n) {                                  // light up the whole hovered branch
      const walk = (x) => { ids.add(x.uid); visibleKids(x).forEach(walk); };
      walk(n);
    }
    branchIds = ids;
    gNodes.classList.toggle('is-focus', !!n);
    gLinks.classList.toggle('is-focus', !!n);
    gNodes.querySelectorAll('.node').forEach((el) =>
      el.classList.toggle('is-branch', ids.has(Number(el.dataset.uid))));
    gLinks.querySelectorAll('.link').forEach((el, i) => {
      const p = links[i];
      if (p) el.classList.toggle('is-branch', ids.has(p[0].uid) && ids.has(p[1].uid));
    });
  }
  function resetHoverCache() { lastHover = null; }

  function toggleNode(id) {
    const n = byId[id];
    if (!n || !n.kids.length) return;
    n.collapsed = !n.collapsed;
    if (n.collapsed) collapsed.add(id); else collapsed.delete(id);
    localStorage.setItem('ym:collapsed:' + docId, JSON.stringify([...collapsed]));
    render(false);
  }

  function selectNode(id) {
    activeId = id;
    gNodes.querySelectorAll('.node').forEach((el) => el.classList.toggle('is-active', el.dataset.id === id));
    showInspector(byId[id]);
  }
  function clearSelection() {
    activeId = null;
    gNodes.querySelectorAll('.node.is-active').forEach((el) => el.classList.remove('is-active'));
    $('inspect').hidden = true;
  }

  function focusNode(id) {
    const n = byId[id];
    if (!n) return;
    const W = svg.clientWidth, H = svg.clientHeight;
    const k = Math.max(view.k, 1);
    animateView({ k, x: W / 2 - (n.x + n.w / 2) * k, y: H / 2 - (n.y + n.h / 2) * k });
    fitted = false;
  }

  function expandAll() {
    collapsed.clear();
    nodes.forEach((n) => { n.collapsed = false; });
    localStorage.setItem('ym:collapsed:' + docId, '[]');
    render(false);
    fitted = true;
    fit(true);
    toast('已展开全部 ' + (nodes.length - 1) + ' 个节点', { kind: 'ok' });
  }
  function collapseAll() {
    collapsed.clear();
    nodes.forEach((n) => { if (n.kids.length) { n.collapsed = true; collapsed.add(n.id); } });
    const first = root && root.kids[0];
    if (first) { first.collapsed = false; collapsed.delete(first.id); }
    localStorage.setItem('ym:collapsed:' + docId, JSON.stringify([...collapsed]));
    render(false);
    fitted = true;
    fit(true);
  }


  /* --------------------------------------------------------------- chrome */
  function bindBar() {
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
      requestAnimationFrame(() => { hueCache = {}; render(false); });
    });

    $('layoutSeg').addEventListener('click', (e) => {
      const b = e.target.closest('[data-layout]');
      if (!b || b.dataset.layout === layout) return;
      setLayout(b.dataset.layout, true);
    });

    $('zoomSeg').addEventListener('click', (e) => {
      const b = e.target.closest('[data-zoom]');
      if (!b) return;
      const cx = svg.clientWidth / 2, cy = svg.clientHeight / 2;
      if (b.dataset.zoom === 'in') zoomAt(1.22, cx, cy);
      else if (b.dataset.zoom === 'out') zoomAt(1 / 1.22, cx, cy);
      else if (b.dataset.zoom === '100') { animateView(Object.assign({}, view, { k: 1 })); fitted = false; }
      else { fitted = true; fit(true); }
    });

    $('btnExpand').addEventListener('click', () => {
      const anyCollapsed = nodes.some((n) => n.collapsed);
      if (anyCollapsed) expandAll(); else collapseAll();
    });
    $('btnPng').addEventListener('click', exportPNG);
  }

  function setLayout(next, user) {
    const changed = layout !== (next === 'tree' ? 'tree' : 'radial');
    layout = next === 'tree' ? 'tree' : 'radial';
    document.querySelectorAll('#layoutSeg .seg__btn').forEach((b) =>
      b.classList.toggle('is-on', b.dataset.layout === layout));
    if (changed && root) {
      visSet = null;
      clearSelection();
      fitted = true;
      render(false);
    }
    if (user) {
      localStorage.setItem('ym:layout', layout);
      YM.setSetting({ layout }).catch(() => {});
      $expandBtnLabel();
    }
  }

  function $expandBtnLabel() {
    const b = $('btnExpand');
    const any = nodes.some((n) => n.collapsed);
    b.innerHTML = icon(any ? 'layers' : 'list');
    b.title = any ? '展开全部节点' : '只保留一级分支';
  }

  function updateStats(visibleCount) {
    const st = (tree && tree.stats) || {};
    const parts = [st.nodes + ' 节点', st.max_depth + ' 层', st.branches + ' 分支'];
    if (visibleCount != null && st.nodes && visibleCount < st.nodes) parts.push('显示 ' + visibleCount);
    $('docStats').textContent = parts.join(' · ');
    $expandBtnLabel();
  }

  function hideHint() {
    const h = $('hint');
    if (h) h.classList.add('is-out');
  }

  /* ------------------------------------------------------------ inspector */
  function showInspector(n) {
    if (!n || !n.depth) { $('inspect').hidden = true; return; }
    const kids = n.kids.length;
    const box = $('inspect');
    box.hidden = false;
    box.innerHTML =
      '<div class="mi__head">' +
      '<span class="mi__num">' + esc(n.numText || n.num) + '</span>' +
      '<div class="grow"><div class="mi__title">' + esc(n.title || '(空)') + '</div>' +
      '<div class="tiny muted mt-2">第 ' + n.depth + ' 层' + (kids ? ' · ' + kids + ' 个子节点' : ' · 叶子节点') + '</div></div>' +
      '<button class="icon-btn mi__close" id="miClose" title="关闭">' + icon('x') + '</button></div>' +
      (n.value ? '<div class="mi__row"><div class="mi__k">取值</div><div class="mi__v mono">' + esc(n.value) + '</div></div>' : '') +
      (n.note ? '<div class="mi__row"><div class="mi__k">备注</div><div class="mi__v">' + esc(n.note) + '</div></div>' : '') +
      (kids ? '<div class="mi__row"><div class="mi__k">下级节点</div><div class="mi__v">' +
        n.kids.slice(0, 8).map((k) => esc(k.title || '(空)')).join('、') +
        (kids > 8 ? ' 等 ' + kids + ' 项' : '') + '</div></div>' : '') +
      (n.line ? '<div class="mi__row"><div class="mi__k">源位置</div><div class="mi__v mono">YAML 第 ' + n.line + ' 行</div></div>' : '') +
      '<div class="mi__acts">' +
      (n.line ? '<button class="btn btn--sm btn--ghost" id="miGoto">' + icon('edit') + '在编辑器中定位</button>' : '') +
      (kids ? '<button class="btn btn--sm" id="miTog">' + icon(n.collapsed ? 'layers' : 'list') +
        (n.collapsed ? '展开' : '折叠') + '</button>' : '') +
      '</div>';
    $('miClose').onclick = clearSelection;
    const g = $('miGoto');
    if (g) g.onclick = () => window.open('/editor?id=' + encodeURIComponent(docId) + '&line=' + n.line, '_blank', 'noopener');
    const t = $('miTog');
    if (t) t.onclick = () => { toggleNode(n.id); selectNode(n.id); };
  }

  /* --------------------------------------------------------- empty / error */
  function showState(kind, title, detail, actions) {
    $('loadingState').hidden = true;
    const el = $('emptyState');
    el.hidden = false;
    gLinks.innerHTML = ''; gNodes.innerHTML = '';
    el.innerHTML = '<div class="mm-empty__card">' +
      '<div class="mm-empty__ico' + (kind === 'error' ? ' is-bad' : '') + '">' +
      icon(kind === 'error' ? 'alert' : kind === 'empty' ? 'doc' : 'map') + '</div>' +
      '<div class="mm-empty__t">' + esc(title) + '</div>' +
      (detail ? '<div class="mm-empty__d">' + detail + '</div>' : '') +
      '<div class="mm-empty__acts">' + (actions || '') + '</div></div>';
    el.querySelectorAll('[data-open-editor]').forEach((b) => {
      b.addEventListener('click', () => {
        const line = b.dataset.line;
        window.open('/editor?id=' + encodeURIComponent(docId) + (line ? '&line=' + line : ''), '_blank', 'noopener');
      });
    });
  }

  /* --------------------------------------------------------------- loading */
  async function load(silent) {
    if (!docId) {
      showState('empty', '缺少文档 ID', '请从「导图管理」页的 <b>展示思维导图</b> 按钮打开本页面。',
        '<a class="btn btn--primary" href="/">回到导图管理</a>');
      return;
    }
    try {
      const r = await api('/api/docs/' + encodeURIComponent(docId) + '/tree');
      tree = r.tree; meta = r.meta; revision = r.revision || 0;
      const title = (meta && meta.title) || '思维导图';
      document.title = title + ' · 思维导图';
      $('docTitle').textContent = title;
      $('btnEdit').href = '/editor?id=' + encodeURIComponent(docId);
      $('emptyState').hidden = true;

      if (!tree || !tree.ok) {
        const err = (tree && tree.error) || {};
        showState('error', 'YAML 语法错误，无法生成导图',
          '修好语法后本页会自动刷新。<div class="mm-empty__err">第 ' + (err.line || '?') + ' 行：' +
          esc(err.message || '无法解析') + (err.context ? '<br>' + esc(err.context) : '') + '</div>',
          '<button class="btn btn--primary" data-open-editor data-line="' + (err.line || 1) + '">' +
          icon('edit') + '打开编辑器修复</button>' +
          '<a class="btn btn--ghost" href="/">返回管理</a>');
        $('docStats').textContent = '解析失败';
        return;
      }
      if (tree.empty) {
        showState('empty', '文档还是空的', '在 YAML 里写下第一个键值对，思维导图会立刻出现在这里。',
          '<button class="btn btn--primary" data-open-editor>' + icon('edit') + '打开编辑器</button>' +
          '<a class="btn btn--ghost" href="/">返回管理</a>');
        $('docStats').textContent = '0 节点';
        return;
      }
      buildLayout();
      if (!silent) { fitted = true; }
      render(true);
      $('loadingState').hidden = true;
      if (!silent) setTimeout(hideHint, 7000);
      startSync();
    } catch (e) {
      showState('error', '载入失败', esc(e.message),
        '<a class="btn btn--primary" href="/">返回导图管理</a>');
    }
  }

  /* ------------------------------------------------------------ live sync */
  function startSync() {
    if (pollTimer) return;
    const chip = $('syncState');
    const poll = async () => {
      if (document.hidden || !docId) return;
      try {
        const r = await api('/api/docs/' + encodeURIComponent(docId) + '/rev');
        if (r.revision !== revision) {
          revision = r.revision;
          chip.classList.add('is-live');
          chip.querySelector('span:last-child').textContent = '已同步 v' + revision;
          const keep = { k: view.k, x: view.x, y: view.y, fitted };
          await load(true);
          view = keep; fitted = keep.fitted;
          applyTransform();
          setTimeout(() => {
            chip.classList.remove('is-live');
            chip.querySelector('span:last-child').textContent = '实时同步';
          }, 1600);
        }
      } catch (e) { chip.classList.add('is-stale'); }
    };
    pollTimer = setInterval(poll, 2500);
  }

  /* ------------------------------------------------------------- PNG export */
  function exportPNG() {
    try {
      const b = bbox();
      const pad = 46;
      const clone = svg.cloneNode(true);
      clone.removeAttribute('id');
      clone.setAttribute('width', Math.round((b.w + pad * 2) * 2));
      clone.setAttribute('height', Math.round((b.h + pad * 2) * 2));
      clone.setAttribute('viewBox', (b.x - pad) + ' ' + (b.y - pad) + ' ' + (b.w + pad * 2) + ' ' + (b.h + pad * 2));
      const world = clone.querySelector('#world');
      if (world) world.setAttribute('transform', '');
      clone.querySelectorAll('.is-in').forEach((n) => n.classList.remove('is-in'));

      const cs = getComputedStyle(document.documentElement);
      const gv = (n, f) => (cs.getPropertyValue(n).trim() || f);
      const stops = clone.querySelectorAll('#rootfill stop');
      if (stops[0]) stops[0].setAttribute('stop-color', gv('--accent-hi', '#ffc968'));
      if (stops[1]) stops[1].setAttribute('stop-color', gv('--accent', '#f2a93b'));
      const shadow = clone.querySelector('#mshadow feDropShadow');
      if (shadow) shadow.setAttribute('flood-color', '#000');

      const styleEl = document.createElementNS('http://www.w3.org/2000/svg', 'style');
      styleEl.textContent = exportCSS(cs) +
        '\nsvg{background:' + gv('--bg', '#0a0c11') + '}' +
        '\n#nodes,#links{font-family:' + FONT_STACK + '}';
      clone.insertBefore(styleEl, clone.firstChild);

      // drop the shadow filter from the standalone file: some renderers choke on it
      const xml = new XMLSerializer().serializeToString(clone);
      const url = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);
      const img = new Image();
      img.onload = () => {
        const cvs = document.createElement('canvas');
        cvs.width = Math.max(1, Math.round((b.w + pad * 2) * 2));
        cvs.height = Math.max(1, Math.round((b.h + pad * 2) * 2));
        const c2 = cvs.getContext('2d');
        c2.fillStyle = getComputedStyle(document.body).backgroundColor || '#0a0c11';
        c2.fillRect(0, 0, cvs.width, cvs.height);
        c2.drawImage(img, 0, 0, cvs.width, cvs.height);
        cvs.toBlob((blob) => {
          if (!blob) { toast('导出失败：浏览器拒绝了图像编码', { kind: 'danger' }); return; }
          const a = document.createElement('a');
          a.href = URL.createObjectURL(blob);
          a.download = ((meta && meta.title) || 'mindmap').replace(/[\\/:*?"<>|]/g, '_') + '.png';
          document.body.appendChild(a); a.click(); a.remove();
          setTimeout(() => URL.revokeObjectURL(a.href), 4000);
          toast('已导出 ' + a.download, { kind: 'ok', title: 'PNG 导出完成' });
        }, 'image/png');
      };
      img.onerror = () => toast('导出失败：SVG 渲染出错', { kind: 'danger' });
      img.src = url;
    } catch (e) {
      toast('导出失败：' + e.message, { kind: 'danger' });
    }
  }

  const EXPORT_SELECTORS = /\.n(box|title|num|value|note|bar)|\.node|\.tog|\.link/;
  function exportCSS(cs) {
    const out = [];
    for (const sheet of document.styleSheets) {
      let rules = null;
      try { rules = sheet.cssRules; } catch (e) { continue; }
      for (const r of rules) {
        if (r.selectorText && EXPORT_SELECTORS.test(r.selectorText)) out.push(r.cssText);
      }
    }
    const vars = ['--surface', '--surface-2', '--surface-3', '--text', '--text-2', '--text-3',
      '--accent', '--accent-hi', '--accent-dim', '--line', '--line-strong'].map((v) =>
      v + ':' + cs.getPropertyValue(v).trim()).join(';');
    return 'svg{' + vars + '}\n' + out.join('\n');
  }

  /* --------------------------------------------------------------- keys */
  function bindKeys() {
    window.addEventListener('keydown', (e) => {
      if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
      const k = e.key;
      if (k === 'f' || k === 'F') { fitted = true; fit(true); }
      else if (k === 'e' || k === 'E') expandAll();
      else if (k === 'c' || k === 'C') collapseAll();
      else if (k === '0') { animateView(Object.assign({}, view, { k: 1 })); }
      else if (k === '+' || k === '=') zoomAt(1.22, svg.clientWidth / 2, svg.clientHeight / 2);
      else if (k === '-' || k === '_') zoomAt(1 / 1.22, svg.clientWidth / 2, svg.clientHeight / 2);
      else if (k === 't' || k === 'T') setLayout(layout === 'tree' ? 'radial' : 'tree', true);
      else if (k === 'Escape') clearSelection();
      else return;
      e.preventDefault();
    });
  }

  /* ----------------------------------------------------------------- init */
  async function init() {
    bindBar();
    bindStage();
    bindKeys();
    try { await YM.bootstrap(); } catch (e) { /* viewer still works with defaults */ }
    const saved = localStorage.getItem('ym:layout') || (state.settings && state.settings.layout);
    setLayout(saved === 'tree' ? 'tree' : 'radial', false);
    if (document.fonts && document.fonts.ready) {
      try { await document.fonts.ready; } catch (e) { /* proceed anyway */ }
    }
    await load(false);
  }

  document.addEventListener('DOMContentLoaded', init);
})();
