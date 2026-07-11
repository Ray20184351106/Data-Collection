"use strict";

const state = { csrf: "", timer: null, socket: null, agents: [] };
const $ = id => document.getElementById(id);

async function api(url, options = {}) {
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (state.csrf && options.method && options.method !== "GET") headers["X-CSRF-TOKEN"] = state.csrf;
  const response = await fetch(url, { credentials: "same-origin", ...options, headers });
  if (response.status === 401) throw new Error("中心拒绝访问，请检查内网模式配置。");
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    throw new Error(body?.error?.message || `请求失败 (${response.status})`);
  }
  return response.status === 204 ? null : response.json();
}

async function getCsrf() { state.csrf = (await api("/api/auth/csrf")).token; }

function metric(label, value) {
  const box = document.createElement("div"); box.className = "metric";
  const caption = document.createElement("span"); caption.textContent = label;
  const number = document.createElement("strong"); number.textContent = String(value);
  box.append(caption, number); return box;
}

async function refresh() {
  const [summary, agents] = await Promise.all([
    api("/api/v1/fleet/summary"),
    api(`/api/v1/fleet/agents?page=1&pageSize=100&search=${encodeURIComponent($("search").value.trim())}`)
  ]);
  const summaryNode = $("summary"); summaryNode.replaceChildren(
    metric("Agent总数", summary.agents), metric("在线Agent", summary.onlineAgents),
    metric("运行设备", summary.runningDevices), metric("等待任务", summary.queueDepth),
    metric("今日成功", summary.todaySuccess), metric("今日失败", summary.todayFailure), metric("未确认告警", summary.activeAlerts)
  );
  const rows = $("agentRows"); rows.replaceChildren();
  state.agents = agents.data.map(item => item.id);
  const now = Date.now();
  for (const item of agents.data) {
    const online = now - new Date(item.lastHeartbeatUtc).getTime() <= 30000;
    const row = document.createElement("tr");
    const values = [online ? "在线" : "离线", item.computerName, item.id, item.site || "-", item.agentVersion, item.pendingUploadCount, new Date(item.lastHeartbeatUtc).toLocaleString()];
    values.forEach((value, index) => {
      const cell = document.createElement("td");
      if (index === 0) { const dot = document.createElement("i"); dot.className = `dot${online ? " online" : ""}`; cell.append(dot); }
      cell.append(document.createTextNode(String(value))); row.append(cell);
    });
    const actionCell = document.createElement("td");
    for (const action of [{ label: "启动", type: 0 }, { label: "停止", type: 1 }, { label: "刷新", type: 2 }]) {
      const button = document.createElement("button");
      button.className = action.type === 2 ? "small secondary" : "small";
      button.textContent = action.label;
      button.addEventListener("click", () => sendCommand(item.id, action.type, button));
      actionCell.append(button);
    }
    row.append(actionCell);
    rows.append(row);
  }
  $("empty").hidden = agents.data.length !== 0;
  $("lastRefresh").textContent = `最近刷新：${new Date().toLocaleString()}`;
}

async function sendCommand(agentId, type, button) {
  button.disabled = true;
  try {
    await api(`/api/v1/agents/${encodeURIComponent(agentId)}/commands`, {
      method: "POST", body: JSON.stringify({ type, parametersJson: "{}", validForSeconds: 300 })
    });
    button.textContent = "已下发";
  } catch (error) { alert(error.message); }
  finally { setTimeout(() => { button.disabled = false; }, 1000); }
}

async function syncConfig() {
  const message = $("configMessage"); message.textContent = "";
  try {
    if (state.agents.length === 0) throw new Error("当前没有可同步的Agent。");
    const result = await api("/api/v1/configs/sync", {
      method: "POST", body: JSON.stringify({ name: $("configName").value, payloadJson: $("configPayload").value,
        minimumAgentVersion: $("minimumAgentVersion").value, agentIds: state.agents })
    });
    message.textContent = `配置 v${result.version} 已同步到 ${state.agents.length} 个Agent。`;
  } catch (error) { message.textContent = error.message; }
}

async function connectSignalR() {
  try {
    const negotiate = await api("/hubs/fleet/negotiate?negotiateVersion=1", { method: "POST" });
    const scheme = location.protocol === "https:" ? "wss" : "ws";
    const socket = new WebSocket(`${scheme}://${location.host}/hubs/fleet?id=${encodeURIComponent(negotiate.connectionToken)}`);
    state.socket = socket;
    socket.onopen = () => socket.send(JSON.stringify({ protocol: "json", version: 1 }) + "\u001e");
    socket.onmessage = event => {
      for (const frame of event.data.split("\u001e").filter(Boolean)) {
        const message = JSON.parse(frame);
        if (message.type === 1 && message.target === "fleetChanged") refresh().catch(() => {});
      }
    };
    socket.onclose = () => { $("liveState").textContent = "实时连接断开"; $("liveState").classList.add("offline"); setTimeout(connectSignalR, 5000); };
    socket.onerror = () => socket.close();
    $("liveState").textContent = "实时连接正常"; $("liveState").classList.remove("offline");
  } catch { $("liveState").textContent = "轮询模式"; $("liveState").classList.add("offline"); setTimeout(connectSignalR, 5000); }
}

$("searchButton").addEventListener("click", () => refresh().catch(error => alert(error.message)));
$("syncConfig").addEventListener("click", syncConfig);
getCsrf().then(() => refresh()).then(() => { connectSignalR(); state.timer = setInterval(() => refresh().catch(() => {}), 10000); })
  .catch(error => { $("lastRefresh").textContent = error.message; });
