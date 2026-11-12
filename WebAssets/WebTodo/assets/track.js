/* 操作日志埋点已随后端日志功能一并移除：保留同名导出（no-op）以免改动所有调用点。 */

/** 占位实现：不再上报任何事件。 */
export function track(action, info = {}) { /* no-op */ }

/** 占位实现：始终返回 null。 */
export async function trackAwait(action, info = {}) {
  return null;
}

/** 占位实现：不再绑定全局点击监听。 */
export function installGlobalTrack() { /* no-op */ }
