"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const logic = require("../../Acquisition.Center/wwwroot/app.logic.js");

test("Agent selection starts empty and is not inferred from visible rows", () => {
  const selection = logic.createAgentSelection();
  const rows = logic.decorateAgentsWithSelection([
    { id: "agent-01" },
    { id: "agent-02" }
  ], selection.selectedIds());

  assert.deepEqual(selection.selectedIds(), []);
  assert.deepEqual(rows.map(row => row.isSelected), [false, false]);
});

test("explicit Agent selection survives search and refresh result changes", () => {
  const selection = logic.createAgentSelection();
  selection.setSelected("agent-02", true);

  const filteredRows = logic.decorateAgentsWithSelection(
    [{ id: "agent-01" }],
    selection.selectedIds()
  );
  const refreshedRows = logic.decorateAgentsWithSelection(
    [{ id: "agent-02" }, { id: "agent-03" }],
    selection.selectedIds()
  );

  assert.deepEqual(selection.selectedIds(), ["agent-02"]);
  assert.equal(filteredRows[0].isSelected, false);
  assert.equal(refreshedRows[0].isSelected, true);
});

test("machine payload contains only the supported five fields", () => {
  const result = logic.validateAndNormalizeMachines([{
    id: "1",
    name: "机台 1",
    monitorPath: "D:\\Acquisition\\Machine1\\Incoming",
    successPath: "D:\\Acquisition\\Machine1\\Success",
    errorPath: "D:\\Acquisition\\Machine1\\Error",
    fileType: ".csv",
    enabled: true
  }]);

  assert.deepEqual(result.errors, []);
  assert.deepEqual(result.machines, [{
    id: 1,
    name: "机台 1",
    monitorPath: "D:\\Acquisition\\Machine1\\Incoming",
    successPath: "D:\\Acquisition\\Machine1\\Success",
    errorPath: "D:\\Acquisition\\Machine1\\Error"
  }]);
});

test("machine validation rejects duplicate ids, relative paths and reused paths", () => {
  const result = logic.validateAndNormalizeMachines([
    {
      id: 1,
      name: "机台 1",
      monitorPath: "relative\\incoming",
      successPath: "D:\\Shared",
      errorPath: "D:\\Shared"
    },
    {
      id: 1,
      name: "机台 2",
      monitorPath: "D:\\Machine2\\Incoming",
      successPath: "D:\\Machine2\\Success",
      errorPath: "D:\\Machine2\\Error"
    }
  ]);

  assert.ok(result.errors.some(error => error.code === "DUPLICATE_ID"));
  assert.ok(result.errors.some(error => error.code === "ABSOLUTE_PATH_REQUIRED"));
  assert.ok(result.errors.some(error => error.code === "PATHS_MUST_DIFFER"));
});

test("preview fingerprint is stable for selection order but changes after form edits", () => {
  const machines = [{
    id: 1,
    name: "机台 1",
    monitorPath: "D:\\Machine1\\Incoming",
    successPath: "D:\\Machine1\\Success",
    errorPath: "D:\\Machine1\\Error"
  }];

  const first = logic.createPreviewFingerprint(machines, ["agent-02", "agent-01"]);
  const reordered = logic.createPreviewFingerprint(machines, ["agent-01", "agent-02"]);
  const edited = logic.createPreviewFingerprint([
    { ...machines[0], name: "机台一" }
  ], ["agent-01", "agent-02"]);

  assert.equal(first, reordered);
  assert.notEqual(first, edited);
  assert.equal(logic.isPreviewCurrent(first, reordered), true);
  assert.equal(logic.isPreviewCurrent(first, edited), false);
});

test("coalesced refresh throttle schedules one refresh per interval", () => {
  const scheduled = [];
  let refreshCount = 0;
  const throttle = logic.createCoalescedThrottle(
    () => { refreshCount += 1; },
    500,
    (callback, delay) => {
      scheduled.push({ callback, delay });
      return scheduled.length;
    },
    () => {}
  );

  throttle.request();
  throttle.request();
  throttle.request();

  assert.equal(scheduled.length, 1);
  assert.equal(scheduled[0].delay, 500);
  scheduled[0].callback();
  assert.equal(refreshCount, 1);

  throttle.request();
  assert.equal(scheduled.length, 2);
});

test("assignment display distinguishes task creation from terminal apply states", () => {
  assert.deepEqual(logic.assignmentPresentation(null), {
    key: "Pending",
    label: "等待 Agent 确认",
    terminal: false
  });
  assert.equal(logic.assignmentPresentation("Applied").label, "已生效");
  assert.equal(logic.assignmentPresentation("RolledBack").label, "已回滚");
  assert.equal(logic.assignmentPresentation("Failed").label, "失败");
});

test("setup draft sends only bootstrap fields and uses the backend run-mode value", () => {
  const draft = logic.buildSetupDraft({
    runMode: "IsolatedTest",
    databaseKind: "LocalDb",
    bindAddress: " 127.0.0.1 ",
    port: "5080",
    advertisedBaseUrl: " http://127.0.0.1:5080 ",
    connectionString: "Server=(localdb)\\\\MSSQLLocalDB;Database=AcquisitionCenterPilot;Integrated Security=true;",
    requireHttps: false,
    generateAccessCode: true
  });

  assert.deepEqual(draft, {
    runMode: "IsolatedTest",
    bindAddress: "127.0.0.1",
    port: 5080,
    advertisedBaseUrl: "http://127.0.0.1:5080",
    databaseConnectionString: "Server=(localdb)\\\\MSSQLLocalDB;Database=AcquisitionCenterPilot;Integrated Security=true;",
    internalLanEnabled: true,
    requireHttps: false
  });
});

test("production mode with LocalDB is blocked before server validation", () => {
  const issues = logic.validateSetupDraftClient({
    runMode: "Production",
    databaseKind: "LocalDb"
  });

  assert.deepEqual(issues, [{
    code: "PRODUCTION_LOCALDB_NOT_ALLOWED",
    field: "databaseKind",
    message: "正式环境不能使用 LocalDB。"
  }]);
  assert.deepEqual(logic.validateSetupDraftClient({
    runMode: "Production",
    databaseKind: "SqlServer"
  }), []);
});

test("setup completion proof is cleared after any form edit", () => {
  const tracker = logic.createSetupValidationTracker();
  tracker.markValidated({ token: "validation-token", fingerprint: "server-fingerprint" });

  assert.equal(tracker.canComplete(), true);
  assert.deepEqual(tracker.proof(), {
    token: "validation-token",
    fingerprint: "server-fingerprint"
  });

  tracker.invalidate();

  assert.equal(tracker.canComplete(), false);
  assert.equal(tracker.proof(), null);
});

test("setup wizard only treats loopback hostnames as local access", () => {
  assert.equal(logic.isLoopbackHostname("localhost"), true);
  assert.equal(logic.isLoopbackHostname("127.0.0.1"), true);
  assert.equal(logic.isLoopbackHostname("[::1]"), true);
  assert.equal(logic.isLoopbackHostname("192.168.20.109"), false);
  assert.equal(logic.isLoopbackHostname("center-server"), false);
});

test("deployment request uses the exact backend contract field names", () => {
  const request = logic.buildDeploymentRequest({
    agentId: " LINE01-PC01 ",
    site: " 一厂 ",
    building: " 一楼 ",
    line: " 1 号线 ",
    acquisitionDirectory: " D:\\Acquisition ",
    autoStart: true,
    ignored: "not-sent"
  });

  assert.deepEqual(request, {
    agentId: "LINE01-PC01",
    site: "一厂",
    building: "一楼",
    line: "1 号线",
    acquisitionAppDirectory: "D:\\Acquisition",
    startWinFormsOnLogon: true
  });
  assert.equal("acquisitionDirectory" in request, false);
  assert.equal("autoStart" in request, false);
});

test("each idempotent action request receives a fresh requestId", () => {
  const ids = [
    "11111111-1111-4111-8111-111111111111",
    "22222222-2222-4222-8222-222222222222"
  ];
  const source = { agentIds: ["agent-01"] };

  const first = logic.withRequestId(source, () => ids.shift());
  const second = logic.withRequestId(source, () => ids.shift());

  assert.equal(first.requestId, "11111111-1111-4111-8111-111111111111");
  assert.equal(second.requestId, "22222222-2222-4222-8222-222222222222");
  assert.deepEqual(source, { agentIds: ["agent-01"] });
});
