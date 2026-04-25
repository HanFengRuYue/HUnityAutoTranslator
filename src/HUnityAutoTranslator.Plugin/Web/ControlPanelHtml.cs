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
      color-scheme: light dark;
      --bg: #f7f7f4;
      --panel: #ffffff;
      --ink: #1f2328;
      --muted: #667085;
      --line: #d9dde3;
      --accent: #0f766e;
      --accent-ink: #ffffff;
      --warn: #b54708;
      --ok: #067647;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --bg: #151719;
        --panel: #202327;
        --ink: #f0f2f4;
        --muted: #a3aab5;
        --line: #343942;
      }
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      background: var(--bg);
      color: var(--ink);
      font-family: "Microsoft YaHei", "Segoe UI", system-ui, sans-serif;
      letter-spacing: 0;
    }
    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 16px;
      padding: 18px 24px;
      border-bottom: 1px solid var(--line);
      background: var(--panel);
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
      max-width: 1180px;
      margin: 0 auto;
      padding: 24px;
      display: grid;
      grid-template-columns: repeat(12, 1fr);
      gap: 16px;
    }
    section {
      grid-column: span 6;
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 18px;
    }
    section.wide { grid-column: span 12; }
    h2 {
      margin: 0 0 14px;
      font-size: 16px;
      line-height: 1.3;
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
      min-height: 38px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 8px 10px;
      background: transparent;
      color: var(--ink);
      font: inherit;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 12px;
    }
    .switches {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
    }
    .check {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 38px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 8px 10px;
      color: var(--ink);
      margin: 0;
    }
    .check input { width: 16px; min-height: 16px; }
    .row {
      display: flex;
      gap: 10px;
      align-items: center;
      flex-wrap: wrap;
    }
    button {
      min-height: 38px;
      border: 1px solid var(--accent);
      border-radius: 6px;
      padding: 8px 14px;
      background: var(--accent);
      color: var(--accent-ink);
      font: inherit;
      cursor: pointer;
    }
    button.secondary {
      background: transparent;
      color: var(--accent);
    }
    .stat {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
    }
    .tile {
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 10px;
      min-height: 68px;
    }
    .tile span {
      color: var(--muted);
      font-size: 12px;
    }
    .tile strong {
      display: block;
      margin-top: 6px;
      font-size: 18px;
    }
    .status {
      color: var(--muted);
      font-size: 13px;
    }
    .ok { color: var(--ok); }
    .warn { color: var(--warn); }
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
