/* 自绘 inline SVG 图标集 —— 无任何图标库依赖。
 * 统一 24x24 viewBox，stroke 由 CSS 控制（currentColor / stroke-width）。 */

const RAW = {
  briefcase: '<rect x="3" y="7.4" width="18" height="12.2" rx="2"/><path d="M8.8 7.4V5.8c0-.9.7-1.6 1.6-1.6h3.2c.9 0 1.6.7 1.6 1.6v1.6"/><path d="M3 11.6h18"/>',
  check: '<path d="M5 13l4 4 10-10"/>',
  plus: '<path d="M12 5.5v13M5.5 12h13"/>',
  x: '<path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>',
  search: '<circle cx="11" cy="11" r="6.4"/><path d="m15.8 15.8 4.2 4.2"/>',
  flag: '<path d="M5.5 20.5V4"/><path d="M5.5 4.8h11.2l-1.9 3.6 1.9 3.6H5.5"/>',
  flagFilled: '<path d="M5.5 20.5V4"/><path d="M5.5 4.8h11.2l-1.9 3.6 1.9 3.6H5.5z" fill="currentColor" stroke="none"/>',
  calendar: '<rect x="3.5" y="5" width="17" height="15" rx="2.2"/><path d="M8 3.2v3.4M16 3.2v3.4M3.5 10h17"/>',
  clock: '<circle cx="12" cy="12" r="8.4"/><path d="M12 7.4V12l3.2 2"/>',
  pencil: '<path d="m4.5 19.5 4-.7L19.4 8.4a1.7 1.7 0 0 0-2.4-2.4L6.2 16.4l-.7 4Z" /><path d="m15.6 7.6 2.4 2.4"/>',
  trash: '<path d="M4.5 7.2h15"/><path d="M9.2 7.2V5.4c0-.7.5-1.2 1.2-1.2h3.2c.7 0 1.2.5 1.2 1.2v1.8"/><path d="m6.8 7.2.8 11.9c0 .9.7 1.6 1.6 1.6h5.6c.9 0 1.6-.7 1.6-1.6l.8-11.9"/><path d="M10.4 11v5.6M13.6 11v5.6"/>',
  pin: '<path d="M8.6 3.4h6.8l-1.2 6.1 3.1 3.2H6.7l3.1-3.2Z"/><path d="M12 12.7V20.6"/>',
  archive: '<rect x="3.4" y="4.8" width="17.2" height="4.4" rx="1.3"/><path d="M5.3 9.2v9.4c0 .9.7 1.6 1.6 1.6h10.2c.9 0 1.6-.7 1.6-1.6V9.2"/><path d="M9.8 13h4.4"/>',
  unarchive: '<rect x="3.4" y="4.8" width="17.2" height="4.4" rx="1.3"/><path d="M5.3 9.2v9.4c0 .9.7 1.6 1.6 1.6h10.2c.9 0 1.6-.7 1.6-1.6V9.2"/><path d="m10 15.4 2-2 2 2"/>',
  chevronDown: '<path d="m6.5 9.5 5.5 5.5 5.5-5.5"/>',
  chevronRight: '<path d="m9.5 6 5.5 6-5.5 6"/>',
  chevronLeft: '<path d="m14.5 6-5.5 6 5.5 6"/>',
  arrowUp: '<path d="M12 19.4V5M12 5 6.4 10.6M12 5l5.6 5.6"/>',
  arrowDown: '<path d="M12 4.6v14.4M12 19l5.6-5.6M12 19l-5.6-5.6"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 3v2.2M12 18.8V21M3 12h2.2M18.8 12H21M5.6 5.6l1.6 1.6M16.8 16.8l1.6 1.6M18.4 5.6l-1.6 1.6M7.2 16.8l-1.6 1.6"/>',
  moon: '<path d="M20.4 14.6A8.6 8.6 0 0 1 9.4 3.6a8.6 8.6 0 1 0 11 11Z"/>',
  monitor: '<rect x="3" y="4.6" width="18" height="12.4" rx="2"/><path d="M9 21h6M12 17v4"/>',
  refresh: '<path d="M20.4 12a8.4 8.4 0 1 1-2.6-6.1"/><path d="M20.6 4v4.6H16"/>',
  alert: '<path d="m10.3 4.2-7.4 12.9c-.7 1.2.2 2.7 1.6 2.7h15c1.4 0 2.3-1.5 1.6-2.7L13.7 4.2a2 2 0 0 0-3.4 0Z"/><path d="M12 9.2v4.4M12 17.2h.01"/>',
  info: '<circle cx="12" cy="12" r="8.4"/><path d="M12 11v5.4M12 7.9h.01"/>',
  download: '<path d="M12 4.4v10.2m0 0 4-4m-4 4-4-4"/><path d="M4.6 17v1.9c0 1 .8 1.7 1.7 1.7h11.4c1 0 1.7-.8 1.7-1.7V17"/>',
  upload: '<path d="M12 19.6V9.4m0 0-4 4m4-4 4 4"/><path d="M4.6 6.6V4.8C4.6 3.8 5.4 3 6.3 3h11.4c1 0 1.7.8 1.7 1.7v1.9"/>',
  eye: '<path d="M2.6 12S6.2 5.8 12 5.8 21.4 12 21.4 12 17.8 18.2 12 18.2 2.6 12 2.6 12Z"/><circle cx="12" cy="12" r="2.9"/>',
  menu: '<path d="M4 7h16M4 12h16M4 17h16"/>',
  inbox: '<path d="M3.4 13.2 6 5.4A1.8 1.8 0 0 1 7.7 4h8.6c.8 0 1.5.5 1.7 1.4l2.6 7.8"/><path d="M3.4 13.2h4.8l1 2.4h5.6l1-2.4h4.8v4.8c0 1-.8 1.8-1.8 1.8H5.2c-1 0-1.8-.8-1.8-1.8Z"/>',
  target: '<circle cx="12" cy="12" r="8.4"/><circle cx="12" cy="12" r="4.4"/><circle cx="12" cy="12" r="1"/>',
  star: '<path d="m12 3.6 2.7 5.4 6 .9-4.3 4.2 1 5.9-5.4-2.8-5.4 2.8 1-5.9L3.3 9.9l6-.9Z"/>',
  heart: '<path d="M12 20.2S3.8 15.6 3.8 9.6A4 4 0 0 1 12 7.5a4 4 0 0 1 8.2 2.1c0 6-8.2 10.6-8.2 10.6Z"/>',
  home: '<path d="m3.6 10.4 7.3-6.2a1.8 1.8 0 0 1 2.3 0l7.2 6.2"/><path d="M5.6 9.6v9.2c0 1 .8 1.8 1.7 1.8h9.4c1 0 1.7-.8 1.7-1.8V9.6"/><path d="M9.8 20.6v-6h4.4v6"/>',
  book: '<path d="M4.4 5.4c0-1 .8-1.8 1.7-1.8H19v16.8H6.1c-1 0-1.7-.8-1.7-1.8Z"/><path d="M4.4 15.6h14.6"/><path d="M8 7.4h7"/>',
  cart: '<circle cx="9.4" cy="19.4" r="1.5"/><circle cx="17" cy="19.4" r="1.5"/><path d="M3.4 4.6h2.4l2.2 10.2h9.9l1.9-7.2H6.4"/>',
  coffee: '<path d="M4.6 8.4h12v6.4a4 4 0 0 1-4 4H8.6a4 4 0 0 1-4-4Z"/><path d="M16.6 9.6h1.8a2.2 2.2 0 0 1 0 4.4h-1.8"/><path d="M5 21h11"/>',
  code: '<path d="m9 7.4-4.6 4.6L9 16.6M15 7.4l4.6 4.6L15 16.6"/>',
  sparkles: '<path d="m12 3.4 1.7 4.5 4.5 1.7-4.5 1.7L12 15.8l-1.7-4.5L5.8 9.6l4.5-1.7Z"/><path d="M18.4 15.4l.8 2 2 .8-2 .8-.8 2-.8-2-2-.8 2-.8Z"/>',
  gift: '<rect x="3.6" y="8.4" width="16.8" height="4.4" rx="1.2"/><path d="M5.4 12.8v6.4c0 1 .8 1.8 1.7 1.8h9.8c1 0 1.7-.8 1.7-1.8v-6.4M12 8.4v12.6"/><path d="M12 8.4S11 4 8.7 4a2.2 2.2 0 0 0 0 4.4ZM12 8.4s1-4.4 3.3-4.4a2.2 2.2 0 0 1 0 4.4Z"/>',
  plane: '<path d="M10.4 20.6 9 14.4l-4.6 1.4-1.2-2.2 5-3.4L6 4.6l2.4-1 3.6 5.6 4.4-3c.9-.6 2-.4 2.5.5l.7 1.2-3.8 5.4.6 6-2.4.7-1.6-4.6Z"/>',
  listChecks: '<path d="m3.4 7 1.8 1.8 3-3M3.4 16.4l1.8 1.8 3-3"/><path d="M11.4 7.6h9.2M11.4 17h9.2"/>',
  database: '<ellipse cx="12" cy="6" rx="7.5" ry="3"/><path d="M4.5 6v6c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3V6"/><path d="M4.5 12v6c0 1.7 3.4 3 7.5 3s7.5-1.3 7.5-3v-6"/>',
  sliders: '<path d="M4 7h9M17 7h3M4 17h3M11 17h9"/><circle cx="15" cy="7" r="2.1"/><circle cx="7" cy="17" r="2.1"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M19 12a7 7 0 0 0-.1-1.2l2-1.6-1.9-3.3-2.4 1a7 7 0 0 0-2-1.2L16.2 3H13.8l-.4 2.5a7 7 0 0 0-2 1.2l-2.4-1L6.1 9.2l2 1.6a7 7 0 0 0 0 2.4l-2 1.6 1.9 3.3 2.4-1a7 7 0 0 0 2 1.2l.4 2.5h2.4l.4-2.5a7 7 0 0 0 2-1.2l2.4 1 1.9-3.3-2-1.6c.1-.4.1-.8.1-1.2Z"/>',
  history: '<path d="M12 7.4V12l3.2 2"/><path d="M4.6 8.4A8 8 0 1 1 4 12"/><path d="M4 4v4.4h4.4"/>',
  filter: '<path d="M4 6.4h16M7 12h10M9.8 17.6h4.4"/>',
  copy: '<rect x="8.4" y="8.4" width="11.2" height="11.2" rx="1.8"/><path d="M15.6 5.6V5c0-.9-.7-1.6-1.6-1.6H5c-.9 0-1.6.7-1.6 1.6v9c0 .9.7 1.6 1.6 1.6h.6"/>',
  more: '<circle cx="12" cy="5.4" r="1.3" fill="currentColor" stroke="none"/><circle cx="12" cy="12" r="1.3" fill="currentColor" stroke="none"/><circle cx="12" cy="18.6" r="1.3" fill="currentColor" stroke="none"/>',
  external: '<path d="M14 4h6v6"/><path d="M19.5 4.5 11 13"/><path d="M18 14v4.6c0 .9-.7 1.6-1.6 1.6H5.6c-.9 0-1.6-.7-1.6-1.6V7.8c0-.9.7-1.6 1.6-1.6H10"/>',
  tasks: '<path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2"/><rect x="9" y="3" width="6" height="4" rx="1.2"/><path d="M8.5 12.5l1.8 1.8 4-4"/>',
  dot: '<circle cx="12" cy="12" r="4.2" fill="currentColor" stroke="none"/>',
  zap: '<path d="M13.4 2.6 4.8 13.2h6.2l-1.4 8.2 8.6-10.6h-6.2Z"/>',
  fileJson: '<path d="M13.4 3.2H7.6c-1 0-1.8.8-1.8 1.8v14c0 1 .8 1.8 1.8 1.8h8.8c1 0 1.8-.8 1.8-1.8V8Z"/><path d="M13.4 3.2V8h4.8"/><path d="M9.4 12.6c0 1-.6 1.4-1.2 1.6.6.2 1.2.6 1.2 1.6M13.6 12.6c0 1 .6 1.4 1.2 1.6-.6.2-1.2.6-1.2 1.6"/>',
  circle: '<circle cx="12" cy="12" r="8.4"/>',
  folder: '<path d="M3.4 7.2c0-1 .8-1.8 1.8-1.8h4l2 2.4h7.6c1 0 1.8.8 1.8 1.8v8.6c0 1-.8 1.8-1.8 1.8H5.2c-1 0-1.8-.8-1.8-1.8Z"/><path d="M3.4 11h17.2"/>',
  cloudOff: '<path d="M18.4 14.6a4 4 0 0 0-.6-8 6 6 0 0 0-8.3-2.3"/><path d="M6.3 8.6a4 4 0 0 0 .6 7.9"/><path d="M3.4 3.4 20.6 20.6"/>'
};

/** 图标选择器（新建/编辑项目）可选的图标名。 */
export const PROJECT_ICONS = [
  'briefcase', 'book', 'cart', 'star', 'heart', 'home', 'flag', 'target', 'code', 'coffee', 'gift', 'plane'
];

/**
 * 生成内联 SVG 字符串。
 * @param {string} name 图标名
 * @param {{cls?: string, filled?: boolean, size?: number}} [opts]
 * @returns {string}
 */
export function icon(name, opts = {}) {
  const inner = RAW[name] || RAW.circle;
  const cls = ['ico', opts.cls || '', opts.filled ? 'ico-fill' : ''].filter(Boolean).join(' ');
  const style = opts.size ? ` style="width:${opts.size}px;height:${opts.size}px"` : '';
  return `<svg viewBox="0 0 24 24" class="${cls}"${style} aria-hidden="true" focusable="false">${inner}</svg>`;
}

/** 未知图标时返回一个稳定的占位圆点，避免 UI 破裂。 */
export function hasIcon(name) {
  return Object.prototype.hasOwnProperty.call(RAW, name);
}
