namespace HUnityAutoTranslator.Plugin;

internal static class ControlPanelHtml
{
    public const string Document = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>HUnityAutoTranslator 控制面板</title>
  <style>
    :root {
      color-scheme: dark;
      --bg: #0d0f12;
      --bg-top: #10141a;
      --header-bg: rgba(18, 22, 28, 0.94);
      --panel: #171b21;
      --panel-strong: #1d232b;
      --field: #101419;
      --field-hover: #141a21;
      --ink: #eef3f7;
      --muted: #9aa6b2;
      --line: #2c3540;
      --line-strong: #40505f;
      --accent: #0d9488;
      --accent-hover: #14b8a6;
      --accent-soft: rgba(20, 184, 166, 0.14);
      --accent-ink: #ffffff;
      --warn: #f97316;
      --warn-soft: rgba(249, 115, 22, 0.14);
      --ok: #22c55e;
      --ok-soft: rgba(34, 197, 94, 0.13);
      --focus: #67e8f9;
      --shadow: 0 18px 54px rgba(0, 0, 0, 0.26);
    }
    @media (prefers-color-scheme: light) {
      :root {
        color-scheme: light;
        --bg: #f4f6f8;
        --bg-top: #eef2f6;
        --header-bg: rgba(255, 255, 255, 0.94);
        --panel: #ffffff;
        --panel-strong: #f8fafc;
        --field: #ffffff;
        --field-hover: #f8fafc;
        --ink: #17202a;
        --muted: #667085;
        --line: #d8dee6;
        --line-strong: #b8c2ce;
        --accent-soft: rgba(13, 148, 136, 0.11);
        --warn-soft: rgba(249, 115, 22, 0.12);
        --ok-soft: rgba(34, 197, 94, 0.12);
        --shadow: 0 18px 44px rgba(15, 23, 42, 0.08);
      }
    }
    * { box-sizing: border-box; }
    html {
      min-height: 100%;
      background: var(--bg);
    }
    body {
      margin: 0;
      min-height: 100%;
      background: linear-gradient(180deg, var(--bg-top) 0, var(--bg) 320px);
      color: var(--ink);
      font-family: "Microsoft YaHei", "Segoe UI", system-ui, sans-serif;
      letter-spacing: 0;
      font-size: 14px;
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 18px 32px;
      border-bottom: 1px solid var(--line);
      background: var(--header-bg);
      backdrop-filter: blur(16px);
      position: sticky;
      top: 0;
      z-index: 2;
    }
    h1 {
      margin: 0;
      font-size: 20px;
      line-height: 1.2;
    }
    main {
      max-width: 1420px;
      margin: 0 auto;
      padding: 30px 24px 48px;
      display: grid;
      grid-template-columns: repeat(12, 1fr);
      gap: 20px;
      align-items: stretch;
    }
    section {
      grid-column: span 6;
      display: flex;
      flex-direction: column;
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 22px;
      box-shadow: var(--shadow);
    }
    section.wide { grid-column: span 12; }
    h2 {
      margin: 0 0 18px;
      font-size: 17px;
      line-height: 1.3;
      letter-spacing: 0;
    }
    label {
      display: grid;
      gap: 6px;
      color: var(--muted);
      font-size: 12px;
      margin-bottom: 12px;
    }
    input, select {
      width: 100%;
      min-height: 46px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 10px 12px;
      background: var(--field);
      color: var(--ink);
      font: inherit;
      outline: none;
      transition: border-color 120ms ease, background-color 120ms ease, box-shadow 120ms ease;
    }
    input:hover, select:hover {
      background: var(--field-hover);
      border-color: var(--line-strong);
    }
    input:focus, select:focus {
      border-color: var(--focus);
      box-shadow: 0 0 0 3px rgba(103, 232, 249, 0.16);
    }
    input::placeholder {
      color: #748290;
      opacity: 1;
    }
    select {
      appearance: none;
      padding-right: 42px;
      background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='%239aa6b2' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpath d='m6 9 6 6 6-6'/%3E%3C/svg%3E");
      background-repeat: no-repeat;
      background-position: right 14px center;
      background-size: 16px;
    }
    select option {
      background: var(--field);
      color: var(--ink);
    }
    select option:checked {
      background: var(--accent);
      color: var(--accent-ink);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 14px;
    }
    .switches {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
      margin-top: 14px;
    }
    .check {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 46px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 10px 12px;
      background: var(--field);
      color: var(--ink);
      margin: 0;
      transition: border-color 120ms ease, background-color 120ms ease;
    }
    .check:hover {
      background: var(--field-hover);
      border-color: var(--line-strong);
    }
    .check input {
      width: 16px;
      min-height: 16px;
      accent-color: var(--accent-hover);
    }
    .row {
      display: flex;
      gap: 10px;
      align-items: center;
      flex-wrap: wrap;
      margin-top: 16px;
    }
    button {
      min-height: 46px;
      border: 1px solid var(--accent);
      border-radius: 6px;
      padding: 10px 18px;
      background: var(--accent);
      color: var(--accent-ink);
      font: inherit;
      font-weight: 650;
      cursor: pointer;
      transition: background-color 120ms ease, border-color 120ms ease, color 120ms ease, transform 120ms ease;
    }
    button:hover {
      background: var(--accent-hover);
      border-color: var(--accent-hover);
      transform: translateY(-1px);
    }
    button:active {
      transform: translateY(0);
    }
    button.secondary {
      background: var(--accent-soft);
      color: var(--accent-hover);
    }
    button.secondary:hover { color: var(--accent-ink); }
    .stat {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 12px;
    }
    .tile {
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 14px;
      min-height: 86px;
      background: var(--panel-strong);
    }
    .tile span {
      color: var(--muted);
      font-size: 12px;
    }
    .tile strong {
      display: block;
      margin-top: 10px;
      font-size: 22px;
      line-height: 1.1;
    }
    .status {
      color: var(--muted);
      font-size: 13px;
    }
    header .status {
      display: inline-flex;
      align-items: center;
      min-height: 28px;
      padding: 4px 10px;
      border: 1px solid var(--line);
      border-radius: 999px;
      background: var(--field);
    }
    .ok { color: var(--ok); }
    .warn { color: var(--warn); }
    .tile strong.ok {
      width: fit-content;
      padding: 2px 10px;
      border-radius: 999px;
      background: var(--ok-soft);
    }
    .tile strong.warn {
      width: fit-content;
      padding: 2px 10px;
      border-radius: 999px;
      background: var(--warn-soft);
    }
    @media (max-width: 760px) {
      header { align-items: flex-start; flex-direction: column; }
      main { padding: 14px; grid-template-columns: 1fr; }
      section, section.wide { grid-column: span 1; }
      .grid, .switches, .stat { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <header>
    <h1>HUnityAutoTranslator 控制面板</h1>
    <div class="status" id="status">正在连接...</div>
  </header>
  <main>
    <section class="wide">
      <h2>运行状态</h2>
      <div class="stat">
        <div class="tile"><span>翻译状态</span><strong id="enabledText">-</strong></div>
        <div class="tile"><span>队列</span><strong id="queueCount">0</strong></div>
        <div class="tile"><span>缓存</span><strong id="cacheCount">0</strong></div>
        <div class="tile"><span>密钥</span><strong id="keyState">-</strong></div>
      </div>
    </section>

    <section>
      <h2>翻译设置</h2>
      <label>目标语言
        <input id="targetLanguage" autocomplete="off" placeholder="zh-Hans / en / ja / ko">
      </label>
      <div class="grid">
        <label>并发请求
          <input id="maxConcurrentRequests" type="number" min="1" max="16">
        </label>
        <label>每分钟请求
          <input id="requestsPerMinute" type="number" min="1" max="600">
        </label>
        <label>批量字符上限
          <input id="maxBatchCharacters" type="number" min="256" max="8000">
        </label>
        <label>扫描间隔(ms)
          <input id="scanIntervalMilliseconds" type="number" min="100" max="5000">
        </label>
      </div>
      <div class="switches">
        <label class="check"><input id="enabled" type="checkbox">启用翻译</label>
        <label class="check"><input id="enableUgui" type="checkbox">UGUI</label>
        <label class="check"><input id="enableTmp" type="checkbox">TextMeshPro</label>
        <label class="check"><input id="enableImgui" type="checkbox">IMGUI</label>
      </div>
    </section>

    <section>
      <h2>AI 服务</h2>
      <label>服务商
        <select id="providerKind">
          <option value="0">OpenAI 原生 Responses</option>
          <option value="1">DeepSeek</option>
          <option value="2">OpenAI 兼容</option>
        </select>
      </label>
      <div class="grid">
        <label>Base URL
          <input id="baseUrl" autocomplete="off">
        </label>
        <label>Endpoint
          <input id="endpoint" autocomplete="off">
        </label>
        <label>模型
          <input id="model" autocomplete="off">
        </label>
        <label>API Key
          <input id="apiKey" type="password" autocomplete="off" placeholder="留空不会覆盖已有密钥">
        </label>
      </div>
      <div class="row">
        <button id="save">保存配置</button>
        <button id="saveKey" class="secondary">更新密钥</button>
        <button id="refresh" class="secondary">刷新</button>
      </div>
    </section>

    <section class="wide">
      <h2>高级性能</h2>
      <div class="grid">
        <label>每次扫描目标数
          <input id="maxScanTargetsPerTick" type="number" min="1" max="4096">
        </label>
        <label>每帧回写数
          <input id="maxWritebacksPerFrame" type="number" min="1" max="512">
        </label>
      </div>
      <div class="status" id="lastError"></div>
    </section>
  </main>
  <script>
    const $ = id => document.getElementById(id);
    const fields = [
      "targetLanguage", "baseUrl", "endpoint", "model",
      "maxConcurrentRequests", "requestsPerMinute", "maxBatchCharacters",
      "scanIntervalMilliseconds", "maxScanTargetsPerTick", "maxWritebacksPerFrame"
    ];
    const checks = ["enabled", "enableUgui", "enableTmp", "enableImgui"];
    const isEditing = () => {
      const active = document.activeElement;
      return active && (active.tagName === "INPUT" || active.tagName === "SELECT");
    };

    async function api(path, options) {
      const response = await fetch(path, Object.assign({
        headers: { "Content-Type": "application/json" },
        cache: "no-store"
      }, options || {}));
      if (!response.ok) throw new Error(await response.text() || response.statusText);
      return await response.json();
    }

    function applyState(state) {
      $("enabledText").textContent = state.Enabled ? "开启" : "暂停";
      $("enabledText").className = state.Enabled ? "ok" : "warn";
      $("queueCount").textContent = state.QueueCount;
      $("cacheCount").textContent = state.CacheCount;
      $("keyState").textContent = state.ApiKeyConfigured ? "已配置" : "未配置";
      $("keyState").className = state.ApiKeyConfigured ? "ok" : "warn";
      $("lastError").textContent = state.LastError ? "最近错误：" + state.LastError : "";
      $("status").textContent = "已连接";
      $("status").className = "status ok";
      if (isEditing()) return;
      $("targetLanguage").value = state.TargetLanguage || "";
      $("providerKind").value = String(state.ProviderKind);
      $("baseUrl").value = state.BaseUrl || "";
      $("endpoint").value = state.Endpoint || "";
      $("model").value = state.Model || "";
      $("maxConcurrentRequests").value = state.MaxConcurrentRequests;
      $("requestsPerMinute").value = state.RequestsPerMinute;
      $("maxBatchCharacters").value = state.MaxBatchCharacters;
      $("scanIntervalMilliseconds").value = state.ScanIntervalMilliseconds;
      $("maxScanTargetsPerTick").value = state.MaxScanTargetsPerTick;
      $("maxWritebacksPerFrame").value = state.MaxWritebacksPerFrame;
      $("enabled").checked = state.Enabled;
      $("enableUgui").checked = state.EnableUgui;
      $("enableTmp").checked = state.EnableTmp;
      $("enableImgui").checked = state.EnableImgui;
    }

    function readConfig() {
      const body = {
        TargetLanguage: $("targetLanguage").value,
        ProviderKind: Number($("providerKind").value),
        BaseUrl: $("baseUrl").value,
        Endpoint: $("endpoint").value,
        Model: $("model").value
      };
      for (const id of ["maxConcurrentRequests", "requestsPerMinute", "maxBatchCharacters", "scanIntervalMilliseconds", "maxScanTargetsPerTick", "maxWritebacksPerFrame"]) {
        body[id[0].toUpperCase() + id.slice(1)] = Number($(id).value);
      }
      for (const id of checks) {
        body[id[0].toUpperCase() + id.slice(1)] = $(id).checked;
      }
      return body;
    }

    async function refresh() {
      try {
        applyState(await api("/api/state"));
      } catch (error) {
        $("status").textContent = "连接失败：" + error.message;
        $("status").className = "status warn";
      }
    }

    $("save").addEventListener("click", async () => {
      applyState(await api("/api/config", { method: "POST", body: JSON.stringify(readConfig()) }));
    });
    $("saveKey").addEventListener("click", async () => {
      applyState(await api("/api/key", { method: "POST", body: JSON.stringify({ ApiKey: $("apiKey").value }) }));
      $("apiKey").value = "";
    });
    $("refresh").addEventListener("click", refresh);
    $("providerKind").addEventListener("change", () => {
      if ($("providerKind").value === "0") {
        $("baseUrl").value = "https://api.openai.com";
        $("endpoint").value = "/v1/responses";
        $("model").value = "gpt-5.5";
      } else if ($("providerKind").value === "1") {
        $("baseUrl").value = "https://api.deepseek.com";
        $("endpoint").value = "/chat/completions";
        $("model").value = "deepseek-v4-flash";
      } else {
        $("endpoint").value = "/v1/chat/completions";
      }
    });
    refresh();
    setInterval(refresh, 2000);
  </script>
</body>
</html>
""";
}
