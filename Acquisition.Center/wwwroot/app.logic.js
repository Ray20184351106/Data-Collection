(function (root, factory) {
  "use strict";

  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  if (root) root.AcquisitionUiLogic = api;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  const assignmentLabels = {
    Pending: { key: "Pending", label: "等待 Agent 确认", terminal: false },
    Downloaded: { key: "Downloaded", label: "已下载", terminal: false },
    Validated: { key: "Validated", label: "已校验", terminal: false },
    Applied: { key: "Applied", label: "已生效", terminal: true },
    RolledBack: { key: "RolledBack", label: "已回滚", terminal: true },
    Failed: { key: "Failed", label: "失败", terminal: true }
  };

  function createAgentSelection(initialIds) {
    const selected = new Set(
      Array.from(initialIds || [])
        .map(value => String(value || "").trim())
        .filter(Boolean)
    );

    return Object.freeze({
      setSelected(agentId, isSelected) {
        const normalized = String(agentId || "").trim();
        if (!normalized) return;
        if (isSelected) selected.add(normalized);
        else selected.delete(normalized);
      },
      isSelected(agentId) {
        return selected.has(String(agentId || "").trim());
      },
      selectedIds() {
        return Array.from(selected).sort((left, right) => left.localeCompare(right));
      },
      clear() {
        selected.clear();
      }
    });
  }

  function decorateAgentsWithSelection(agents, selectedIds) {
    const selected = new Set(Array.from(selectedIds || []).map(String));
    return Array.from(agents || []).map(agent => ({
      ...agent,
      isSelected: selected.has(String(agent.id || agent.agentId || ""))
    }));
  }

  function normalizeWindowsPath(value) {
    return String(value || "").trim().replace(/[\\/]+$/, "");
  }

  function isAbsoluteWindowsPath(value) {
    const path = String(value || "").trim();
    return /^[a-zA-Z]:[\\/]/.test(path) || /^\\\\[^\\/]+[\\/][^\\/]+/.test(path);
  }

  function validateAndNormalizeMachines(input) {
    const machines = [];
    const errors = [];
    const ids = new Set();

    for (const [index, source] of Array.from(input || []).entries()) {
      const row = source || {};
      const id = Number(row.id);
      const name = String(row.name || "").trim();
      const monitorPath = normalizeWindowsPath(row.monitorPath);
      const successPath = normalizeWindowsPath(row.successPath);
      const errorPath = normalizeWindowsPath(row.errorPath);

      if (!Number.isInteger(id) || id < 1 || id > 6) {
        errors.push({ code: "INVALID_ID", row: index, message: "机台编号必须是 1 到 6 的整数。" });
      } else if (ids.has(id)) {
        errors.push({ code: "DUPLICATE_ID", row: index, message: `机台编号 ${id} 重复。` });
      } else {
        ids.add(id);
      }

      if (!name) errors.push({ code: "NAME_REQUIRED", row: index, message: "机台名称不能为空。" });

      for (const [field, path] of [
        ["monitorPath", monitorPath],
        ["successPath", successPath],
        ["errorPath", errorPath]
      ]) {
        if (!isAbsoluteWindowsPath(path)) {
          errors.push({
            code: "ABSOLUTE_PATH_REQUIRED",
            row: index,
            field,
            message: `${field} 必须是 Windows 绝对路径。`
          });
        }
      }

      const comparablePaths = [monitorPath, successPath, errorPath].map(path => path.toLocaleLowerCase());
      if (new Set(comparablePaths).size !== comparablePaths.length) {
        errors.push({ code: "PATHS_MUST_DIFFER", row: index, message: "监听、成功和失败目录不能相同。" });
      }

      machines.push({ id, name, monitorPath, successPath, errorPath });
    }

    if (machines.length === 0) {
      errors.push({ code: "MACHINE_REQUIRED", row: -1, message: "至少配置一台机台。" });
    }

    return { machines, errors };
  }

  function canonicalMachines(machines) {
    return Array.from(machines || [])
      .map(machine => ({
        id: Number(machine.id),
        name: String(machine.name || "").trim(),
        monitorPath: normalizeWindowsPath(machine.monitorPath),
        successPath: normalizeWindowsPath(machine.successPath),
        errorPath: normalizeWindowsPath(machine.errorPath)
      }))
      .sort((left, right) => left.id - right.id);
  }

  function createPreviewFingerprint(machines, agentIds) {
    return JSON.stringify({
      machines: canonicalMachines(machines),
      agentIds: Array.from(new Set(Array.from(agentIds || []).map(String))).sort((left, right) => left.localeCompare(right))
    });
  }

  function isPreviewCurrent(previewFingerprint, currentFingerprint) {
    return Boolean(previewFingerprint) && previewFingerprint === currentFingerprint;
  }

  function createCoalescedThrottle(callback, intervalMs, schedule, cancelSchedule) {
    const setTimer = schedule || setTimeout;
    const clearTimer = cancelSchedule || clearTimeout;
    let timer = null;

    return Object.freeze({
      request() {
        if (timer !== null) return;
        timer = setTimer(() => {
          timer = null;
          callback();
        }, Math.max(0, Number(intervalMs) || 0));
      },
      cancel() {
        if (timer === null) return;
        clearTimer(timer);
        timer = null;
      }
    });
  }

  function assignmentPresentation(state) {
    let key = state;
    if (state === null || state === undefined || state === "") key = "Pending";
    if (typeof key === "number") {
      key = ["Downloaded", "Validated", "Applied", "Failed", "RolledBack"][key] || "Pending";
    }
    const normalized = String(key);
    return { ...(assignmentLabels[normalized] || { key: normalized, label: normalized, terminal: false }) };
  }

  function toPayloadJson(machines) {
    return JSON.stringify({ machines: canonicalMachines(machines) }, null, 2);
  }

  function buildSetupDraft(input) {
    const source = input || {};
    return {
      runMode: String(source.runMode || "IsolatedTest"),
      bindAddress: String(source.bindAddress || "").trim(),
      port: Number(source.port),
      advertisedBaseUrl: String(source.advertisedBaseUrl || "").trim(),
      databaseConnectionString: String(source.connectionString || ""),
      internalLanEnabled: source.internalLanEnabled !== false,
      requireHttps: Boolean(source.requireHttps)
    };
  }

  function validateSetupDraftClient(input) {
    const source = input || {};
    if (source.runMode === "Production" && source.databaseKind === "LocalDb") {
      return [{
        code: "PRODUCTION_LOCALDB_NOT_ALLOWED",
        field: "databaseKind",
        message: "正式环境不能使用 LocalDB。"
      }];
    }
    return [];
  }

  function createSetupValidationTracker() {
    let validationProof = null;
    return Object.freeze({
      markValidated(proof) {
        validationProof = proof && typeof proof === "object" ? { ...proof } : {};
      },
      invalidate() {
        validationProof = null;
      },
      canComplete() {
        return validationProof !== null;
      },
      proof() {
        return validationProof === null ? null : { ...validationProof };
      }
    });
  }

  function isLoopbackHostname(hostname) {
    const normalized = String(hostname || "").trim().toLocaleLowerCase().replace(/^\[|\]$/g, "");
    return normalized === "localhost" || normalized === "::1" || /^127(?:\.\d{1,3}){3}$/.test(normalized);
  }

  function buildDeploymentRequest(input) {
    const source = input || {};
    return {
      agentId: String(source.agentId || "").trim(),
      site: String(source.site || "").trim(),
      building: String(source.building || "").trim(),
      line: String(source.line || "").trim(),
      acquisitionAppDirectory: String(source.acquisitionAppDirectory ?? source.acquisitionDirectory ?? "").trim(),
      startWinFormsOnLogon: Boolean(source.startWinFormsOnLogon ?? source.autoStart)
    };
  }

  function randomUuid() {
    const cryptoApi = typeof globalThis !== "undefined" ? globalThis.crypto : null;
    if (typeof cryptoApi?.randomUUID === "function") return cryptoApi.randomUUID();
    if (typeof cryptoApi?.getRandomValues !== "function") {
      throw new Error("当前浏览器无法生成安全的请求编号，请使用新版浏览器或 HTTPS。" );
    }
    const bytes = cryptoApi.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, value => value.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  function withRequestId(input, generateRequestId) {
    const requestId = String((generateRequestId || randomUuid)()).trim();
    if (!requestId) throw new Error("无法生成请求编号。" );
    return { ...(input || {}), requestId };
  }

  return Object.freeze({
    assignmentPresentation,
    buildDeploymentRequest,
    buildSetupDraft,
    createAgentSelection,
    createCoalescedThrottle,
    createPreviewFingerprint,
    createSetupValidationTracker,
    decorateAgentsWithSelection,
    isAbsoluteWindowsPath,
    isLoopbackHostname,
    isPreviewCurrent,
    normalizeWindowsPath,
    toPayloadJson,
    validateAndNormalizeMachines,
    validateSetupDraftClient,
    withRequestId
  });
});
