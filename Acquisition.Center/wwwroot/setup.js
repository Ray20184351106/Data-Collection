(function () {
  "use strict";

  const logic = window.AcquisitionUiLogic;
  const byId = id => document.getElementById(id);
  const validationTracker = logic.createSetupValidationTracker();
  let csrf = "";
  let completed = false;

  function setMessage(element, message, tone) {
    element.textContent = message || "";
    element.classList.remove("success", "warning");
    if (tone) element.classList.add(tone);
  }

  async function request(url, options = {}) {
    const method = options.method || "GET";
    const headers = { Accept: "application/json" };
    if (options.body !== undefined) {
      headers["Content-Type"] = "application/json";
      headers["X-CSRF-TOKEN"] = csrf;
    }
    const response = await fetch(url, { method, headers, body: options.body === undefined ? undefined : JSON.stringify(options.body), credentials: "same-origin" });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      throw new Error(body?.error?.message || body?.message || `请求失败 (${response.status})`);
    }
    return response.status === 204 ? null : response.json();
  }

  async function ensureCsrf() {
    const response = await request("/api/auth/csrf");
    csrf = response?.token || "";
    if (!csrf) throw new Error("未获得初始化安全令牌，请刷新页面后重试。");
  }

  function readInput() {
    return {
      runMode: byId("setupRunMode").value,
      databaseKind: byId("setupDatabaseKind").value,
      bindAddress: byId("setupBindAddress").value,
      port: byId("setupPort").value,
      advertisedBaseUrl: byId("setupAdvertisedBaseUrl").value,
      connectionString: byId("setupConnectionString").value,
      requireHttps: byId("setupRequireHttps").checked
    };
  }

  function renderDetails(container, pairs) {
    container.replaceChildren();
    for (const [label, value] of pairs) {
      const term = document.createElement("dt");
      term.textContent = label;
      const definition = document.createElement("dd");
      definition.textContent = String(value ?? "-");
      container.append(term, definition);
    }
  }

  function renderIssues(issues) {
    const container = byId("setupValidationChecks");
    container.replaceChildren();
    for (const issue of issues || []) {
      const item = document.createElement("div");
      item.className = "check-item danger";
      const marker = document.createElement("span");
      marker.textContent = "×";
      const text = document.createElement("span");
      text.textContent = issue.message || String(issue);
      item.append(marker, text);
      container.append(item);
    }
  }

  async function refreshStatus() {
    const message = byId("setupStatusMessage");
    try {
      const status = await request("/api/bootstrap/v1/status");
      renderDetails(byId("setupStatusDetails"), [
        ["首次配置", status?.configured ? "已生成，等待重启" : "尚未生成"],
        ["访问范围", "仅本机回环地址"]
      ]);
      const checks = byId("setupStatusChecks");
      checks.replaceChildren();
      const item = document.createElement("div");
      item.className = `check-item ${status?.configured ? "warning" : "good"}`;
      item.textContent = status?.configured ? "配置已写入，必须重启 Center 才会加载最终生效值。" : "本机初始化接口已启用。";
      checks.append(item);
      setMessage(message, status?.configured ? "当前进程仍处于初始化模式。" : "可以填写首次启动配置。", status?.configured ? "warning" : "success");
      if (status?.configured) {
        byId("validateSetup").disabled = true;
        byId("completeSetup").disabled = true;
      }
    } catch (error) {
      setMessage(message, error.message);
    }
  }

  function invalidateValidation() {
    if (completed) return;
    validationTracker.invalidate();
    byId("completeSetup").disabled = true;
    byId("setupValidationSummary").hidden = true;
    setMessage(byId("setupFormMessage"), "配置已修改，请先重新校验。", "warning");
  }

  async function validateSetup() {
    const formMessage = byId("setupFormMessage");
    const input = readInput();
    const clientIssues = logic.validateSetupDraftClient(input);
    if (clientIssues.length) {
      renderIssues(clientIssues);
      byId("setupValidationSummary").hidden = false;
      validationTracker.invalidate();
      byId("completeSetup").disabled = true;
      setMessage(formMessage, "请先修正校验错误。");
      return;
    }

    const button = byId("validateSetup");
    button.disabled = true;
    setMessage(formMessage, "正在进行本机配置校验…", "warning");
    try {
      const draft = logic.buildSetupDraft(input);
      const result = await request("/api/bootstrap/v1/validate", { method: "POST", body: draft });
      const issues = result?.issues || [];
      renderDetails(byId("setupValidationDetails"), [
        ["运行模式", draft.runMode],
        ["监听地址", draft.bindAddress],
        ["端口", draft.port],
        ["Agent 使用地址", draft.advertisedBaseUrl],
        ["数据库连接串", "已提交校验，不回显"]
      ]);
      renderIssues(issues);
      byId("setupValidationSummary").hidden = false;
      if (result?.isValid) {
        validationTracker.markValidated({ draft: JSON.stringify(draft) });
        byId("completeSetup").disabled = false;
        setMessage(formMessage, "校验通过。请复核摘要后生成最终配置。", "success");
      } else {
        validationTracker.invalidate();
        byId("completeSetup").disabled = true;
        setMessage(formMessage, "校验未通过，最终配置未写入。");
      }
    } catch (error) {
      validationTracker.invalidate();
      byId("completeSetup").disabled = true;
      setMessage(formMessage, error.message);
    } finally {
      button.disabled = false;
    }
  }

  async function completeSetup() {
    const input = readInput();
    const draft = logic.buildSetupDraft(input);
    if (!validationTracker.canComplete() || validationTracker.proof()?.draft !== JSON.stringify(draft)) {
      invalidateValidation();
      return;
    }
    const button = byId("completeSetup");
    button.disabled = true;
    setMessage(byId("setupFormMessage"), "正在写入最终配置…", "warning");
    try {
      const result = await request("/api/bootstrap/v1/complete", { method: "POST", body: draft });
      const code = String(result?.deploymentAdminAccessCode || "");
      if (!code) throw new Error("服务端未返回一次性管理员访问码。");
      completed = true;
      byId("setupAccessCode").textContent = code;
      byId("setupAccessCodeResult").hidden = false;
      byId("setupStepComplete").classList.add("is-active");
      byId("setupStepConfigure").classList.remove("is-active");
      setMessage(byId("setupFormMessage"), "最终配置已写入。请保存访问码，然后重启 Center。", "success");
      Array.from(byId("setupForm").elements).forEach(element => { element.disabled = true; });
      await refreshStatus();
    } catch (error) {
      button.disabled = false;
      setMessage(byId("setupFormMessage"), error.message);
    }
  }

  async function copyAccessCode() {
    const code = byId("setupAccessCode").textContent || "";
    if (!code) return;
    try {
      if (!navigator.clipboard?.writeText) throw new Error("当前浏览器不支持安全剪贴板。");
      await navigator.clipboard.writeText(code);
      setMessage(byId("setupAccessCodeMessage"), "已复制。页面不会保存此访问码。", "success");
    } catch (error) {
      setMessage(byId("setupAccessCodeMessage"), `复制失败：${error.message}`);
    }
  }

  function wireEvents() {
    byId("refreshSetupStatus").addEventListener("click", refreshStatus);
    byId("validateSetup").addEventListener("click", validateSetup);
    byId("completeSetup").addEventListener("click", completeSetup);
    byId("copySetupAccessCode").addEventListener("click", copyAccessCode);
    byId("installCenterService").addEventListener("click", () => {
      setMessage(byId("serviceMessage"), "一期不通过网页远程执行服务安装。请以管理员身份按发布包中的说明安装服务。", "warning");
    });
    byId("setupForm").addEventListener("input", invalidateValidation);
    byId("setupForm").addEventListener("change", invalidateValidation);
  }

  async function initialize() {
    wireEvents();
    try {
      await ensureCsrf();
      await refreshStatus();
    } catch (error) {
      setMessage(byId("setupStatusMessage"), error.message);
    }
  }

  initialize();
})();
