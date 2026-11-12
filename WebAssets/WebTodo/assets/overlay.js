/* 共用遮罩层（模态框 + 窄屏抽屉），用栈管理点击处理与显示状态。 */

const stack = [];

function scrimEl() {
  let el = document.getElementById('scrim');
  if (!el) {
    el = document.createElement('div');
    el.id = 'scrim';
    el.className = 'scrim';
    document.body.appendChild(el);
  }
  return el;
}

function sync() {
  const el = scrimEl();
  if (stack.length) {
    el.hidden = false;
  } else {
    el.hidden = true;
  }
  document.body.classList.toggle('has-overlay', stack.length > 0);
  document.documentElement.classList.toggle('no-scroll', stack.length > 0);
}

export function pushScrim(onClick) {
  const el = scrimEl();
  const handler = (event) => { onClick(event); };
  el.addEventListener('click', handler);
  stack.push(handler);
  sync();
  return () => popScrim(handler);
}

export function popScrim(handler) {
  const el = scrimEl();
  const i = stack.indexOf(handler);
  if (i >= 0) stack.splice(i, 1);
  el.removeEventListener('click', handler);
  sync();
}

export function scrimDepth() { return stack.length; }
