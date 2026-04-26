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
      color-scheme: light;
      --bg: #f6f7fb;
      --sidebar: #eef1f6;
      --surface: #ffffff;
      --surface-2: #f8fafc;
      --field: #ffffff;
      --line: #d7dde7;
      --line-strong: #aeb8c7;
      --ink: #17202a;
      --muted: #687385;
      --accent: #2563eb;
      --accent-hover: #1d4ed8;
      --accent-soft: rgba(37, 99, 235, 0.11);
      --warn: #c77700;
      --ok: #2563eb;
      --danger: #dc2626;
      --shadow: 0 18px 42px rgba(24, 32, 45, 0.08);
    }
    [data-theme="dark"] {
      color-scheme: dark;
      --bg: #1b1f27;
      --sidebar: #222733;
      --surface: #282e3a;
      --surface-2: #202530;
      --field: #1f2430;
      --line: #3c4657;
      --line-strong: #647086;
      --ink: #f3f6fb;
      --muted: #aab4c3;
      --accent: #7aa2ff;
      --accent-hover: #9bb8ff;
      --accent-soft: rgba(122, 162, 255, 0.16);
      --warn: #f5b544;
      --ok: #9bb8ff;
      --danger: #ff7b86;
      --shadow: 0 18px 42px rgba(0, 0, 0, 0.2);
    }
    @media (prefers-color-scheme: dark) {
      :root:not([data-theme="light"]) {
        color-scheme: dark;
        --bg: #1b1f27;
        --sidebar: #222733;
        --surface: #282e3a;
        --surface-2: #202530;
        --field: #1f2430;
        --line: #3c4657;
        --line-strong: #647086;
        --ink: #f3f6fb;
        --muted: #aab4c3;
        --accent: #7aa2ff;
        --accent-hover: #9bb8ff;
        --accent-soft: rgba(122, 162, 255, 0.16);
        --warn: #f5b544;
        --ok: #9bb8ff;
        --danger: #ff7b86;
        --shadow: 0 18px 42px rgba(0, 0, 0, 0.2);
      }
    }
    * { box-sizing: border-box; }
    html, body { min-height: 100%; }
    body {
      margin: 0;
      background: var(--bg);
      color: var(--ink);
      font: 14px/1.5 "Microsoft YaHei", "Segoe UI", system-ui, sans-serif;
      letter-spacing: 0;
    }
    button, input, select, textarea { font: inherit; }
    button {
      min-height: 36px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 7px 12px;
      background: var(--surface-2);
      color: var(--ink);
      cursor: pointer;
      transition: border-color 120ms ease, background-color 120ms ease, color 120ms ease, transform 120ms ease;
    }
    button:hover { border-color: var(--line-strong); transform: translateY(-1px); }
    button.primary {
      border-color: var(--accent);
      background: var(--accent);
      color: #fff;
      font-weight: 700;
    }
    button.secondary {
      border-color: var(--accent);
      background: var(--accent-soft);
      color: var(--accent-hover);
      font-weight: 650;
    }
    button.danger {
      border-color: rgba(220, 38, 38, 0.45);
      color: var(--danger);
      background: rgba(220, 38, 38, 0.08);
    }
    input, select, textarea {
      width: 100%;
      min-height: 36px;
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 7px 10px;
      background: var(--field);
      color: var(--ink);
      outline: none;
    }
    textarea {
      min-height: 220px;
      resize: vertical;
      white-space: pre-wrap;
    }
    input:focus, select:focus, textarea:focus, [contenteditable="true"]:focus {
      border-color: var(--accent);
      box-shadow: 0 0 0 3px var(--accent-soft);
    }
    label {
      display: grid;
      gap: 6px;
      color: var(--muted);
      font-size: 12px;
    }
    label span { color: var(--muted); }
    .app-shell {
      min-height: 100vh;
      display: grid;
      grid-template-columns: 254px minmax(0, 1fr);
    }
    .sidebar {
      position: sticky;
      top: 0;
      height: 100vh;
      display: flex;
      flex-direction: column;
      gap: 18px;
      padding: 20px 16px;
      border-right: 1px solid var(--line);
      background: var(--sidebar);
    }
    .brand {
      display: grid;
      gap: 4px;
      padding: 4px 8px 14px;
      border-bottom: 1px solid var(--line);
    }
    .brand strong { font-size: 16px; line-height: 1.2; }
    .brand span { color: var(--muted); font-size: 12px; }
    .nav-list { display: grid; gap: 8px; }
    .nav-item {
      width: 100%;
      justify-content: flex-start;
      text-align: left;
      border-color: transparent;
      background: transparent;
      color: var(--muted);
    }
    .nav-item.active {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
      font-weight: 700;
    }
    .sidebar-footer {
      margin-top: auto;
      display: grid;
      gap: 12px;
      padding-top: 14px;
      border-top: 1px solid var(--line);
    }
    .connection {
      min-height: 24px;
      color: var(--muted);
      font-size: 12px;
    }
    .theme-cycle {
      width: 100%;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .workspace {
      min-width: 0;
      padding: 26px 28px 44px;
    }
    .page {
      display: none;
      max-width: 1480px;
      margin: 0 auto;
    }
    .page.active { display: block; }
    .page-head {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 18px;
      margin-bottom: 18px;
    }
    .page-head h1 {
      margin: 0;
      font-size: 24px;
      line-height: 1.25;
    }
    .band {
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 18px;
      box-shadow: var(--shadow);
      margin-bottom: 16px;
    }
    .band h2 {
      margin: 0 0 14px;
      font-size: 16px;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 14px;
    }
    .grid.three { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    .grid.four { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    .grid.status-grid { grid-template-columns: repeat(7, minmax(0, 1fr)); }
    .metric {
      min-height: 82px;
      background: var(--surface-2);
      border: 1px solid var(--line);
      border-radius: 6px;
      padding: 12px;
      position: relative;
    }
    .metric[data-help] { cursor: help; }
    .metric span {
      display: block;
      color: var(--muted);
      font-size: 12px;
    }
    .metric strong {
      display: block;
      margin-top: 10px;
      font-size: 21px;
      line-height: 1.12;
      overflow-wrap: anywhere;
    }
    .metric[data-help]::after {
      content: attr(data-help);
      position: absolute;
      z-index: 30;
      top: calc(100% + 8px);
      left: 12px;
      box-sizing: border-box;
      width: max-content;
      max-width: min(360px, calc(100vw - 44px));
      padding: 9px 10px;
      border: 1px solid var(--line-strong);
      border-radius: 6px;
      background: var(--surface);
      color: var(--ink);
      box-shadow: var(--shadow);
      font-size: 12px;
      line-height: 1.45;
      white-space: normal;
      overflow-wrap: anywhere;
      opacity: 0;
      visibility: hidden;
      pointer-events: none;
      transform: translateY(-3px);
      transition: opacity 120ms ease, transform 120ms ease, visibility 120ms ease;
    }
    .status-grid .metric:nth-last-child(-n + 2)::after {
      right: 12px;
      left: auto;
    }
    .metric[data-help]:hover::after,
    .metric[data-help]:focus-visible::after {
      opacity: 1;
      visibility: visible;
      transform: translateY(0);
    }
    .metric[data-help]:focus-visible {
      outline: none;
      border-color: var(--accent);
      box-shadow: 0 0 0 3px var(--accent-soft);
    }
    .row {
      display: flex;
      gap: 10px;
      align-items: center;
      flex-wrap: wrap;
    }
    .checks {
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
      background: var(--field);
    }
    .check input {
      width: 16px;
      min-height: 16px;
      accent-color: var(--accent);
    }
    .stack { display: grid; gap: 16px; }
    .provider-option[hidden] { display: none; }
    .message {
      color: var(--muted);
      margin-top: 10px;
      white-space: pre-wrap;
    }
    .message:empty { display: none; }
    .toast-host {
      position: fixed;
      top: 18px;
      right: 18px;
      z-index: 80;
      display: grid;
      gap: 10px;
      width: min(460px, calc(100vw - 32px));
      pointer-events: none;
    }
    .toast {
      pointer-events: auto;
      border: 1px solid var(--line);
      border-left: 4px solid var(--accent);
      border-radius: 8px;
      padding: 12px 14px;
      background: var(--surface);
      color: var(--ink);
      box-shadow: var(--shadow);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      transform: translateY(0);
      opacity: 1;
      transition: opacity 160ms ease, transform 160ms ease;
      animation: toast-in 160ms ease-out;
    }
    .toast.ok { border-left-color: var(--ok); }
    .toast.warn { border-left-color: var(--warn); }
    .toast.danger { border-left-color: var(--danger); }
    @keyframes toast-in {
      from { opacity: 0; transform: translateY(-8px); }
      to { opacity: 1; transform: translateY(0); }
    }
    .ok { color: var(--ok); }
    .warn { color: var(--warn); }
    .danger-text { color: var(--danger); }
    .toast.ok, .toast.warn, .toast.danger { color: var(--ink); }
    .recent-list { display: grid; gap: 8px; }
    .recent-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) 160px;
      gap: 12px;
      align-items: start;
      padding: 10px 0;
      border-bottom: 1px solid var(--line);
    }
    .recent-row:last-child { border-bottom: 0; }
    .recent-row b {
      display: block;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .recent-row span {
      color: var(--muted);
      font-size: 12px;
    }
    .editor-tools {
      display: grid;
      grid-template-columns: minmax(260px, 1fr) auto;
      gap: 10px;
      align-items: center;
      margin-bottom: 12px;
    }
    .editor-actions {
      display: flex;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
      justify-content: flex-end;
    }
    .file-actions {
      display: flex;
      align-items: center;
      gap: 8px;
      padding-left: 8px;
      border-left: 1px solid var(--line);
    }
    .file-action-button {
      min-width: 82px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 6px;
    }
    .hidden-file-input {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
      pointer-events: none;
    }
    .export-control {
      position: relative;
    }
    .export-menu {
      position: absolute;
      top: calc(100% + 6px);
      right: 0;
      z-index: 45;
      display: none;
      min-width: 168px;
      padding: 6px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--surface);
      box-shadow: var(--shadow);
    }
    .export-menu.open {
      display: grid;
      gap: 4px;
    }
    .export-menu button {
      width: 100%;
      min-height: 34px;
      border-color: transparent;
      background: transparent;
      text-align: left;
      justify-content: flex-start;
    }
    .export-menu button:hover {
      background: var(--surface-2);
    }
    .column-control {
      position: relative;
    }
    .column-chooser {
      position: absolute;
      top: calc(100% + 6px);
      right: 0;
      z-index: 40;
      display: none;
      width: 260px;
      max-width: calc(100vw - 32px);
      padding: 10px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--surface);
      box-shadow: var(--shadow);
    }
    .column-chooser.open {
      display: grid;
      gap: 10px;
    }
    .column-chooser-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
      color: var(--muted);
      font-size: 12px;
    }
    .column-chooser-list {
      display: grid;
      gap: 6px;
      max-height: 300px;
      overflow: auto;
    }
    .column-choice {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 6px;
      align-items: center;
    }
    .column-choice-label {
      min-height: 34px;
      padding: 6px 8px;
    }
    .column-choice-label span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .column-move-buttons {
      display: flex;
      gap: 4px;
    }
    .column-move-buttons button {
      width: 30px;
      min-height: 30px;
      padding: 0;
      justify-content: center;
      border-color: transparent;
      background: transparent;
      color: var(--muted);
    }
    .column-move-buttons button:hover:not(:disabled) {
      background: var(--surface-2);
      color: var(--ink);
    }
    .column-move-buttons button:disabled {
      opacity: 0.35;
      cursor: default;
    }
    .column-chooser-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
    }
    .table-wrap {
      overflow: auto;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--surface);
      max-height: 620px;
      outline: none;
    }
    table {
      width: max-content;
      min-width: 100%;
      border-collapse: collapse;
      table-layout: fixed;
    }
    th, td {
      border-right: 1px solid var(--line);
      border-bottom: 1px solid var(--line);
      padding: 8px 10px;
      vertical-align: top;
    }
    th {
      position: sticky;
      top: 0;
      z-index: 2;
      background: var(--surface-2);
      color: var(--muted);
      font-size: 12px;
      text-align: left;
      cursor: pointer;
      user-select: none;
    }
    th .header-inner {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto auto;
      gap: 6px;
      align-items: center;
    }
    .header-title {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .header-filter {
      width: 24px;
      min-height: 24px;
      padding: 0;
      justify-content: center;
      border-color: transparent;
      background: transparent;
      color: var(--muted);
    }
    .header-filter:hover,
    .header-filter.filter-active {
      background: var(--surface);
      color: var(--accent);
      border-color: var(--line);
    }
    .col-resizer {
      position: absolute;
      top: 0;
      right: -3px;
      width: 7px;
      height: 100%;
      cursor: col-resize;
      z-index: 3;
    }
    td {
      background: var(--surface);
      min-width: 72px;
    }
    td.selected {
      outline: 2px solid var(--accent);
      outline-offset: -2px;
      background: var(--accent-soft);
    }
    td.dirty::after {
      content: "";
      float: right;
      width: 6px;
      height: 6px;
      border-radius: 999px;
      background: var(--warn);
      margin-top: 4px;
    }
    .cell-text {
      max-height: 88px;
      overflow: auto;
      white-space: pre-wrap;
      word-break: break-word;
    }
    .context-menu {
      position: fixed;
      z-index: 50;
      min-width: 190px;
      padding: 6px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--surface);
      box-shadow: var(--shadow);
      display: none;
    }
    .context-menu.open { display: grid; gap: 4px; }
    .column-filter-menu {
      position: fixed;
      z-index: 60;
      display: none;
      width: min(320px, calc(100vw - 24px));
      max-height: min(460px, calc(100vh - 24px));
      padding: 10px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--surface);
      box-shadow: var(--shadow);
    }
    .column-filter-menu.open {
      display: grid;
      gap: 8px;
    }
    .filter-option-list {
      display: grid;
      gap: 4px;
      max-height: 250px;
      overflow: auto;
    }
    .filter-option-row {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      gap: 8px;
      align-items: center;
      min-height: 30px;
      color: var(--ink);
    }
    .filter-option-row span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .filter-menu-actions {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      flex-wrap: wrap;
    }
    .context-menu button {
      min-height: 32px;
      border-color: transparent;
      background: transparent;
      text-align: left;
      justify-content: flex-start;
    }
    @media (max-width: 1080px) {
      .grid.status-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
      .grid.four { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .checks { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (max-width: 780px) {
      .app-shell { grid-template-columns: 1fr; }
      .sidebar { position: static; height: auto; }
      .grid, .grid.three, .grid.four, .grid.status-grid, .checks, .recent-row {
        grid-template-columns: 1fr;
      }
      .workspace { padding: 18px 14px 34px; }
      .page-head, .editor-tools { grid-template-columns: 1fr; flex-direction: column; }
      .editor-actions, .file-actions, .column-control, .column-control > button, .file-action-button, .export-control { width: 100%; }
      .editor-actions { justify-content: stretch; }
      .file-actions {
        padding-left: 0;
        border-left: 0;
      }
      .column-chooser, .export-menu { left: 0; right: auto; }
      .metric[data-help]::after { left: 12px; right: auto; max-width: calc(100vw - 52px); }
      .toast-host { left: 12px; right: 12px; width: auto; }
    }
  </style>
</head>
<body>
  <div class="toast-host" id="toastHost" aria-live="polite" aria-atomic="false"></div>
  <div class="app-shell">
    <aside class="sidebar">
      <div class="brand">
        <strong>HUnityAutoTranslator</strong>
        <span>本机控制面板</span>
      </div>
      <nav class="nav-list" aria-label="功能导航">
        <button class="nav-item active" data-page="status">状态</button>
        <button class="nav-item" data-page="plugin">插件设置</button>
        <button class="nav-item" data-page="ai">AI 翻译设置</button>
        <button class="nav-item" data-page="glossary">术语库</button>
        <button class="nav-item" data-page="editor">文本编辑</button>
        <button class="nav-item" data-page="about">版本信息</button>
      </nav>
      <div class="sidebar-footer">
        <div class="connection" id="status">正在连接...</div>
        <button class="theme-cycle" id="themeCycle" type="button"><span>主题</span><strong id="themeCycleText">跟随系统</strong></button>
      </div>
    </aside>

    <main class="workspace">
      <section class="page active" id="page-status">
        <div class="page-head">
          <div>
            <h1>运行状态</h1>
          </div>
          <button class="secondary" id="refresh" type="button">刷新</button>
        </div>

        <div class="band">
          <div class="grid status-grid">
            <div class="metric" tabindex="0" data-help="当前插件是否启用并能与游戏端保持连接。运行中表示翻译管线已启动。"><span>插件状态</span><strong id="enabledText">-</strong></div>
            <div class="metric" tabindex="0" data-help="游戏界面中已经被扫描到并进入处理流程的文本数量。"><span>已捕获文本</span><strong id="capturedTextCount">0</strong></div>
            <div class="metric" tabindex="0" data-help="已经排队、尚未被 AI 服务处理的文本数量。"><span>等待翻译</span><strong id="queuedTextCount">0</strong></div>
            <div class="metric" tabindex="0" data-help="当前正在请求 AI 服务翻译的文本数量。"><span>正在翻译</span><strong id="inFlightTranslationCount">0</strong></div>
            <div class="metric" tabindex="0" data-help="本次运行中已经拿到译文并完成处理的文本数量。"><span>已完成</span><strong id="completedTranslationCount">0</strong></div>
            <div class="metric" tabindex="0" data-help="译文已准备好，等待写回 Unity 文本组件的数量。"><span>写回队列</span><strong id="writebackQueueCount">0</strong></div>
            <div class="metric" tabindex="0" data-help="已经保存到本地 SQLite，后续可直接复用或编辑的译文数量。"><span>已翻译文本</span><strong id="cacheCount">0</strong></div>
          </div>
        </div>

        <div class="stack">
          <div class="band">
            <h2>AI 服务</h2>
            <div class="grid four">
              <div class="metric"><span>服务商</span><strong id="providerName">-</strong></div>
              <div class="metric"><span>模型</span><strong id="providerModel">-</strong></div>
              <div class="metric"><span>密钥</span><strong id="keyState">-</strong></div>
              <div class="metric"><span>累计 Token</span><strong id="totalTokenCount">0</strong></div>
              <div class="metric"><span>平均耗时</span><strong id="averageLatency">0 ms</strong></div>
              <div class="metric"><span>平均速度</span><strong id="averageSpeed">0 字/秒</strong></div>
            </div>
            <p class="message danger-text" id="lastError"></p>
          </div>

          <div class="band">
            <h2>最近完成</h2>
            <div class="recent-list" id="recentTranslations"></div>
          </div>
        </div>
      </section>

      <section class="page" id="page-plugin">
        <div class="page-head">
          <div>
            <h1>插件设置</h1>
          </div>
          <button class="primary" id="savePlugin" type="button">保存插件设置</button>
        </div>

        <div class="band">
          <h2>翻译行为</h2>
          <div class="grid three">
            <label><span>翻译语言</span>
              <select id="targetLanguage">
                <option value="zh-Hans">简体中文</option>
                <option value="zh-Hant">繁体中文</option>
                <option value="en">英语</option>
                <option value="ja">日语</option>
                <option value="ko">韩语</option>
                <option value="fr">法语</option>
                <option value="de">德语</option>
                <option value="es">西班牙语</option>
                <option value="ru">俄语</option>
              </select>
            </label>
            <label><span>最大源文本长度</span><input id="maxSourceTextLength" type="number" min="20" max="10000"></label>
            <label><span>上下文条数</span><input id="translationContextMaxExamples" type="number" min="0" max="20"></label>
            <label><span>上下文字符上限</span><input id="translationContextMaxCharacters" type="number" min="0" max="8000"></label>
          </div>
          <div class="checks" style="margin-top:14px">
            <label class="check"><input id="enabled" type="checkbox">启用翻译</label>
            <label class="check"><input id="autoOpenControlPanel" type="checkbox">启动后自动打开控制面板</label>
            <label class="check"><input id="ignoreInvisibleText" type="checkbox">忽略不可见文本</label>
            <label class="check"><input id="skipNumericSymbolText" type="checkbox">跳过数字/符号文本</label>
            <label class="check"><input id="enableCacheLookup" type="checkbox">启用缓存查找</label>
            <label class="check"><input id="enableTranslationContext" type="checkbox">启用翻译上下文</label>
          </div>
        </div>

        <div class="band">
          <h2>捕获与性能</h2>
          <div class="grid four">
            <label><span>扫描间隔 (ms)</span><input id="scanIntervalMilliseconds" type="number" min="100" max="5000"></label>
            <label><span>每次扫描目标数</span><input id="maxScanTargetsPerTick" type="number" min="1" max="4096"></label>
            <label><span>每帧写回数</span><input id="maxWritebacksPerFrame" type="number" min="1" max="512"></label>
          </div>
          <div class="checks" style="margin-top:14px">
            <label class="check"><input id="enableUgui" type="checkbox">UGUI</label>
            <label class="check"><input id="enableTmp" type="checkbox">TextMeshPro</label>
            <label class="check"><input id="enableImgui" type="checkbox">IMGUI</label>
            <label class="check"><input id="manualEditsOverrideAi" type="checkbox">人工编辑优先</label>
            <label class="check"><input id="reapplyRememberedTranslations" type="checkbox">重新应用已记忆译文</label>
          </div>
        </div>

        <div class="band">
          <h2>字体替换</h2>
          <div class="grid four">
            <label><span>字体名称</span><input id="replacementFontName" autocomplete="off" placeholder="留空自动选择，如 Microsoft YaHei / Noto Sans SC"></label>
            <label><span>字体文件路径</span><input id="replacementFontFile" autocomplete="off" placeholder="留空自动选择，如 C:\Windows\Fonts\msyh.ttc"></label>
            <label><span>TMP 采样字号</span><input id="fontSamplingPointSize" type="number" min="16" max="180"></label>
          </div>
          <div class="checks" style="margin-top:14px">
            <label class="check"><input id="enableFontReplacement" type="checkbox">启用字体替换</label>
            <label class="check"><input id="replaceUguiFonts" type="checkbox">UGUI 替换字体</label>
            <label class="check"><input id="replaceTmpFonts" type="checkbox">TextMeshPro fallback</label>
            <label class="check"><input id="replaceImguiFonts" type="checkbox">IMGUI 替换字体</label>
            <label class="check"><input id="autoUseCjkFallbackFonts" type="checkbox">自动使用系统中日韩字体</label>
          </div>
        </div>
      </section>

      <section class="page" id="page-ai">
        <div class="page-head">
          <div>
            <h1>AI 翻译设置</h1>
          </div>
          <div class="row">
            <button class="primary" id="saveAi" type="button">保存 AI 设置</button>
            <button class="secondary" id="saveKey" type="button">更新密钥</button>
          </div>
        </div>

        <div class="band">
          <h2>服务连接</h2>
          <div class="grid three">
            <label><span>服务商</span>
              <select id="providerKind">
                <option value="0">OpenAI Responses</option>
                <option value="1">DeepSeek</option>
                <option value="2">OpenAI 兼容</option>
              </select>
            </label>
            <label><span>Base URL</span><input id="baseUrl" autocomplete="off"></label>
            <label><span>Endpoint</span><input id="endpoint" autocomplete="off"></label>
            <label><span>API Key</span><input id="apiKey" type="password" autocomplete="off" placeholder="留空不会覆盖已保存密钥"></label>
            <label><span>请求超时 (秒)</span><input id="requestTimeoutSeconds" type="number" min="5" max="180"></label>
          </div>
          <div class="row" style="margin-top:14px">
            <button id="testProvider" class="secondary" type="button">测试连接</button>
            <button id="fetchModels" class="secondary" type="button">获取模型列表</button>
            <button id="fetchBalance" class="secondary" type="button">查询余额/成本</button>
          </div>
        </div>

        <div class="band">
          <h2>模型与提示词</h2>
          <div class="grid four">
            <label><span>模型预设</span><select id="modelPreset"></select></label>
            <label><span>模型</span><input id="model" autocomplete="off"></label>
            <label><span>并发请求</span><input id="maxConcurrentRequests" type="number" min="1" max="16"></label>
            <label><span>每分钟请求</span><input id="requestsPerMinute" type="number" min="1" max="600"></label>
            <label><span>批量字符上限</span><input id="maxBatchCharacters" type="number" min="256" max="8000"></label>
            <label><span>翻译风格</span>
              <select id="style">
                <option value="0">忠实</option>
                <option value="1">自然</option>
                <option value="2">本地化</option>
                <option value="3">UI 简洁</option>
              </select>
            </label>
            <label class="provider-option" data-providers="0"><span>OpenAI 推理强度</span>
              <select id="reasoningEffort">
                <option value="none">none</option>
                <option value="low">low</option>
                <option value="medium">medium</option>
                <option value="high">high</option>
                <option value="xhigh">xhigh</option>
              </select>
            </label>
            <label class="provider-option" data-providers="0"><span>OpenAI 输出详细度</span>
              <select id="outputVerbosity">
                <option value="low">low</option>
                <option value="medium">medium</option>
                <option value="high">high</option>
              </select>
            </label>
            <label class="provider-option" data-providers="1"><span>DeepSeek Thinking</span>
              <select id="deepSeekThinkingMode">
                <option value="enabled">启用</option>
                <option value="disabled">关闭</option>
              </select>
            </label>
            <label class="provider-option" data-providers="1"><span>DeepSeek 推理强度</span>
              <select id="deepSeekReasoningEffort">
                <option value="high">high</option>
                <option value="max">max</option>
              </select>
            </label>
            <label class="provider-option" data-providers="1,2"><span>Temperature</span><input id="temperature" type="number" min="0" max="2" step="0.1"></label>
          </div>
          <label style="margin-top:14px"><span>自定义完整提示词</span><textarea id="customPrompt" spellcheck="false"></textarea></label>
        </div>
      </section>

      <section class="page" id="page-glossary">
        <div class="page-head">
          <div>
            <h1>术语库</h1>
          </div>
          <button class="primary" id="saveGlossarySettings" type="button">保存术语设置</button>
        </div>

        <div class="band">
          <h2>应用规则</h2>
          <div class="grid four">
            <label><span>注入术语上限</span><input id="glossaryMaxTerms" type="number" min="0" max="100"></label>
            <label><span>术语字符上限</span><input id="glossaryMaxCharacters" type="number" min="0" max="8000"></label>
          </div>
          <div class="checks" style="margin-top:14px">
            <label class="check"><input id="enableGlossary" type="checkbox">启用术语库约束</label>
            <label class="check"><input id="enableAutoTermExtraction" type="checkbox">启用 AI 自动提取术语</label>
          </div>
          <p class="message warn">AI 自动提取默认关闭。开启后，模型会从已完成译文中提取术语并直接启用；人工维护的术语永远优先，不会被自动结果覆盖。</p>
        </div>

        <div class="band">
          <h2>手动维护</h2>
          <div class="grid four">
            <label><span>原术语</span><input id="glossarySourceTerm" autocomplete="off"></label>
            <label><span>指定译名</span><input id="glossaryTargetTerm" autocomplete="off"></label>
            <label><span>目标语言</span><input id="glossaryTargetLanguage" autocomplete="off" placeholder="zh-Hans"></label>
            <label><span>备注</span><input id="glossaryNote" autocomplete="off"></label>
          </div>
          <div class="row" style="margin-top:14px">
            <button id="addGlossaryTerm" type="button">新增/更新术语</button>
            <button id="clearGlossaryForm" class="secondary" type="button">清空</button>
          </div>
        </div>

        <div class="band">
          <div class="editor-tools">
            <input id="glossarySearch" placeholder="搜索原术语、指定译名、语言或备注">
            <div class="editor-actions">
              <button id="refreshGlossary" class="secondary" type="button">刷新</button>
            </div>
          </div>
          <div class="table-wrap" id="glossaryWrap" tabindex="0">
            <table>
              <colgroup>
                <col style="width:80px">
                <col style="width:220px">
                <col style="width:220px">
                <col style="width:110px">
                <col style="width:110px">
                <col style="width:220px">
                <col style="width:100px">
                <col style="width:170px">
                <col style="width:120px">
              </colgroup>
              <thead><tr>
                <th>启用</th>
                <th>原术语</th>
                <th>指定译名</th>
                <th>语言</th>
                <th>来源</th>
                <th>备注</th>
                <th>使用次数</th>
                <th>更新时间</th>
                <th>操作</th>
              </tr></thead>
              <tbody id="glossaryBody"></tbody>
            </table>
          </div>
          <div class="message" id="glossaryMessage"></div>
        </div>
      </section>

      <section class="page" id="page-editor">
        <div class="page-head">
          <div>
            <h1>文本编辑</h1>
          </div>
          <button class="primary" id="saveRows" type="button">保存修改</button>
        </div>
        <div class="band">
          <div class="editor-tools">
            <input id="tableSearch" placeholder="搜索原文、译文、场景或组件">
            <div class="editor-actions">
              <div class="column-control">
                <button id="columnMenuButton" class="secondary" type="button" aria-controls="columnChooser" aria-expanded="false">列显示</button>
                <div class="column-chooser" id="columnChooser"></div>
              </div>
              <button id="clearTableFilters" class="secondary" type="button">清空筛选</button>
              <div class="file-actions" aria-label="导入导出">
                <input id="importFile" class="hidden-file-input" type="file" accept=".json,.csv,text/csv,application/json">
                <button id="importRows" class="secondary file-action-button" type="button">导入</button>
                <div class="export-control">
                  <button id="exportRows" class="secondary file-action-button" type="button" aria-controls="exportMenu" aria-expanded="false">导出</button>
                  <div class="export-menu" id="exportMenu" role="menu" aria-label="导出格式">
                    <button type="button" role="menuitem" data-export-format="json">JSON 文件</button>
                    <button type="button" role="menuitem" data-export-format="csv">CSV 文件</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div class="table-wrap" id="tableWrap" tabindex="0">
            <table>
              <colgroup id="translationColgroup"></colgroup>
              <thead><tr id="translationHead"></tr></thead>
              <tbody id="translationBody"></tbody>
            </table>
          </div>
          <div class="message" id="tableMessage"></div>
        </div>
      </section>

      <section class="page" id="page-about">
        <div class="page-head">
          <div>
            <h1>版本信息</h1>
          </div>
        </div>
        <div class="band">
          <div class="grid three">
            <div class="metric"><span>插件</span><strong>HUnityAutoTranslator</strong></div>
            <div class="metric"><span>版本</span><strong>0.1.0</strong></div>
            <div class="metric"><span>通道</span><strong>local</strong></div>
            <div class="metric"><span>运行目标</span><strong>BepInEx 6 Unity Mono</strong></div>
            <div class="metric"><span>面板访问</span><strong>loopback only</strong></div>
            <div class="metric"><span>数据存储</span><strong>SQLite</strong></div>
          </div>
        </div>
      </section>
    </main>
  </div>

  <div class="context-menu" id="tableContextMenu">
    <button data-table-action="refresh" type="button">刷新表格</button>
    <button data-table-action="copy" type="button">复制选区</button>
    <button data-table-action="paste" type="button">粘贴到选区</button>
    <button data-table-action="retranslate" type="button">重翻选中行</button>
    <button data-table-action="highlight" type="button">高亮显示</button>
    <button data-table-action="delete" class="danger" type="button">删除选中已翻译文本</button>
    <button data-table-action="save" type="button">保存修改</button>
  </div>

  <div class="column-filter-menu" id="columnFilterMenu" role="dialog" aria-label="列筛选"></div>

  <script>
    const $ = id => document.getElementById(id);
    const $$ = selector => Array.from(document.querySelectorAll(selector));
    const providerNames = { 0: "OpenAI", 1: "DeepSeek", 2: "OpenAI 兼容" };
    const themeChoices = ["system", "light", "dark"];
    const themeLabels = { system: "跟随系统", light: "浅色", dark: "深色" };
    const styleInstructions = {
      0: "风格：忠实保留原意，避免增删信息。",
      1: "风格：表达自然流畅，避免机器翻译腔。",
      2: "风格：允许自然本地化，符合游戏语境和角色口吻。",
      3: "风格：UI、菜单和按钮要短而清楚。"
    };
    const providerDefaults = {
      0: { baseUrl: "https://api.openai.com", endpoint: "/v1/responses", model: "gpt-5.5" },
      1: { baseUrl: "https://api.deepseek.com", endpoint: "/chat/completions", model: "deepseek-v4-flash" },
      2: { baseUrl: "http://127.0.0.1:8000", endpoint: "/v1/chat/completions", model: "local-model" }
    };
    const modelPresets = {
      0: [["gpt-5.5", "GPT-5.5"], ["gpt-5.4", "GPT-5.4"], ["gpt-5.4-mini", "GPT-5.4 Mini"], ["custom", "手动填写"]],
      1: [["deepseek-v4-flash", "DeepSeek V4 Flash"], ["deepseek-v4-pro", "DeepSeek V4 Pro"], ["deepseek-chat", "DeepSeek Chat"], ["deepseek-reasoner", "DeepSeek Reasoner"], ["custom", "手动填写"]],
      2: [["local-model", "本地/兼容模型"], ["custom", "手动填写"]]
    };
    const defaultFontNamePlaceholder = "\u7559\u7a7a\u81ea\u52a8\u9009\u62e9\uff0c\u5982 Microsoft YaHei / Noto Sans SC";
    const defaultFontFilePlaceholder = "\u7559\u7a7a\u81ea\u52a8\u9009\u62e9\uff0c\u5982 C:\\Windows\\Fonts\\msyh.ttc";
    const textFields = ["targetLanguage", "baseUrl", "endpoint", "model", "replacementFontName", "replacementFontFile"];
    const numberFields = [
      "maxConcurrentRequests", "requestsPerMinute", "maxBatchCharacters",
      "scanIntervalMilliseconds", "maxScanTargetsPerTick", "maxWritebacksPerFrame",
      "requestTimeoutSeconds", "temperature", "maxSourceTextLength",
      "translationContextMaxExamples", "translationContextMaxCharacters",
      "glossaryMaxTerms", "glossaryMaxCharacters",
      "fontSamplingPointSize"
    ];
    const checks = [
      "enabled", "autoOpenControlPanel", "enableUgui", "enableTmp", "enableImgui", "ignoreInvisibleText",
      "skipNumericSymbolText", "enableCacheLookup", "enableTranslationContext", "manualEditsOverrideAi",
      "enableGlossary", "enableAutoTermExtraction",
      "reapplyRememberedTranslations", "enableFontReplacement", "replaceUguiFonts", "replaceTmpFonts",
      "replaceImguiFonts", "autoUseCjkFallbackFonts"
    ];
    const tableColumns = [
      { key: "SourceText", title: "原文", sort: "source_text", editable: false, width: 260 },
      { key: "TranslatedText", title: "译文", sort: "translated_text", editable: true, width: 260 },
      { key: "TargetLanguage", title: "语言", sort: "target_language", editable: false, width: 110 },
      { key: "SceneName", title: "场景", sort: "scene_name", editable: true, width: 160 },
      { key: "ComponentHierarchy", title: "层级", sort: "component_hierarchy", editable: true, width: 260 },
      { key: "ComponentType", title: "组件", sort: "component_type", editable: true, width: 170 },
      { key: "ReplacementFont", title: "替换字体", sort: "replacement_font", editable: true, width: 220 },
      { key: "ProviderKind", title: "服务商", sort: "provider_kind", editable: false, width: 120 },
      { key: "ProviderModel", title: "模型", sort: "provider_model", editable: false, width: 160 },
      { key: "CreatedUtc", title: "创建时间", sort: "created_utc", editable: false, width: 170, time: true },
      { key: "UpdatedUtc", title: "更新时间", sort: "updated_utc", editable: false, width: 170, time: true }
    ];
    const columnVisibilityStorageKey = "hunity.editor.visibleColumns";
    const columnOrderStorageKey = "hunity.editor.columnOrder";
    const columnFilterStorageKey = "hunity.editor.columnFilters";
    const emptyFilterValue = "__HUNITY_EMPTY__";
    const exportFileTypes = [
      { description: "JSON 文件", accept: { "application/json": [".json"] } },
      { description: "CSV 文件", accept: { "text/csv": [".csv"] } }
    ];
    const tableState = {
      search: "",
      sort: "updated_utc",
      direction: "desc",
      offset: 0,
      limit: 100,
      totalCount: 0,
      rows: [],
      selected: new Set(),
      dirty: new Map(),
      anchor: null,
      filters: {},
      openFilterColumn: null,
      filterOptionDraft: new Set()
    };
    const glossaryState = {
      search: "",
      sort: "updated_utc",
      direction: "desc",
      offset: 0,
      limit: 100,
      rows: [],
      totalCount: 0
    };
    let promptTouched = false;

    function setText(id, value) {
      const element = $(id);
      if (element) element.textContent = value ?? "";
    }

    function isEditing() {
      const active = document.activeElement;
      return active && ["INPUT", "SELECT", "TEXTAREA"].includes(active.tagName);
    }

    async function api(path, options) {
      const response = await fetch(path, Object.assign({
        headers: { "Content-Type": "application/json" },
        cache: "no-store"
      }, options || {}));
      if (!response.ok) throw new Error(await response.text() || response.statusText);
      const text = await response.text();
      return text ? JSON.parse(text) : {};
    }

    function showToast(message, tone = "info", timeout = 5200) {
      const host = $("toastHost");
      if (!host || !message) return;
      const toast = document.createElement("div");
      toast.className = tone === "info" ? "toast" : `toast ${tone}`;
      toast.setAttribute("role", tone === "danger" ? "alert" : "status");
      toast.textContent = message;
      host.appendChild(toast);
      while (host.children.length > 4 && host.firstElementChild) {
        host.firstElementChild.remove();
      }
      window.setTimeout(() => {
        toast.style.opacity = "0";
        toast.style.transform = "translateY(-8px)";
        window.setTimeout(() => toast.remove(), 180);
      }, timeout);
    }

    function compactLines(items, formatter, limit = 8) {
      const lines = items.map(formatter).filter(Boolean);
      const visible = lines.slice(0, limit);
      const hidden = lines.length - visible.length;
      return visible.join("\n") + (hidden > 0 ? `\n还有 ${hidden} 条未显示。` : "");
    }

    async function runProviderUtility(label, action) {
      try {
        await saveConfigOnly();
        await savePendingApiKey();
        await action();
      } catch (error) {
        showToast(`${label}失败：${error.message || error}`, "danger");
      }
    }

    function showPage(page) {
      $$(".nav-item").forEach(button => button.classList.toggle("active", button.dataset.page === page));
      $$(".page").forEach(section => section.classList.toggle("active", section.id === "page-" + page));
      hideContextMenu();
      if (page === "glossary") loadGlossaryTerms();
      if (page === "editor" && tableState.rows.length === 0) loadTranslations();
    }

    function applyTheme(choice) {
      localStorage.setItem("hunity.theme", choice);
      if (choice === "system") document.documentElement.removeAttribute("data-theme");
      else document.documentElement.dataset.theme = choice;
      setText("themeCycleText", themeLabels[choice] || themeLabels.system);
    }

    function cycleTheme() {
      const current = localStorage.getItem("hunity.theme") || "system";
      const next = themeChoices[(themeChoices.indexOf(current) + 1) % themeChoices.length];
      applyTheme(next);
    }

    function numberValue(id) {
      const value = $(id).value.trim();
      return value === "" ? null : Number(value);
    }

    function readConfig() {
      const temperatureText = $("temperature").value.trim();
      return {
        TargetLanguage: $("targetLanguage").value,
        Style: Number($("style").value),
        ProviderKind: Number($("providerKind").value),
        BaseUrl: $("baseUrl").value,
        Endpoint: $("endpoint").value,
        Model: $("model").value,
        MaxConcurrentRequests: numberValue("maxConcurrentRequests"),
        RequestsPerMinute: numberValue("requestsPerMinute"),
        MaxBatchCharacters: numberValue("maxBatchCharacters"),
        ScanIntervalMilliseconds: numberValue("scanIntervalMilliseconds"),
        MaxScanTargetsPerTick: numberValue("maxScanTargetsPerTick"),
        MaxWritebacksPerFrame: numberValue("maxWritebacksPerFrame"),
        RequestTimeoutSeconds: numberValue("requestTimeoutSeconds"),
        ReasoningEffort: activeReasoningEffort(),
        OutputVerbosity: $("outputVerbosity").value,
        DeepSeekThinkingMode: $("deepSeekThinkingMode").value,
        Temperature: temperatureText === "" ? null : Number(temperatureText),
        ClearTemperature: isControlVisible("temperature") && temperatureText === "",
        CustomPrompt: $("customPrompt").value,
        MaxSourceTextLength: numberValue("maxSourceTextLength"),
        TranslationContextMaxExamples: numberValue("translationContextMaxExamples"),
        TranslationContextMaxCharacters: numberValue("translationContextMaxCharacters"),
        GlossaryMaxTerms: numberValue("glossaryMaxTerms"),
        GlossaryMaxCharacters: numberValue("glossaryMaxCharacters"),
        ReplacementFontName: $("replacementFontName").value,
        ReplacementFontFile: $("replacementFontFile").value,
        FontSamplingPointSize: numberValue("fontSamplingPointSize"),
        Enabled: $("enabled").checked,
        AutoOpenControlPanel: $("autoOpenControlPanel").checked,
        EnableUgui: $("enableUgui").checked,
        EnableTmp: $("enableTmp").checked,
        EnableImgui: $("enableImgui").checked,
        EnableFontReplacement: $("enableFontReplacement").checked,
        ReplaceUguiFonts: $("replaceUguiFonts").checked,
        ReplaceTmpFonts: $("replaceTmpFonts").checked,
        ReplaceImguiFonts: $("replaceImguiFonts").checked,
        AutoUseCjkFallbackFonts: $("autoUseCjkFallbackFonts").checked,
        IgnoreInvisibleText: $("ignoreInvisibleText").checked,
        SkipNumericSymbolText: $("skipNumericSymbolText").checked,
        EnableCacheLookup: $("enableCacheLookup").checked,
        EnableTranslationContext: $("enableTranslationContext").checked,
        EnableGlossary: $("enableGlossary").checked,
        EnableAutoTermExtraction: $("enableAutoTermExtraction").checked,
        ManualEditsOverrideAi: $("manualEditsOverrideAi").checked,
        ReapplyRememberedTranslations: $("reapplyRememberedTranslations").checked
      };
    }

    function activeReasoningEffort() {
      return $("providerKind").value === "1" ? $("deepSeekReasoningEffort").value : $("reasoningEffort").value;
    }

    function isControlVisible(id) {
      const option = $(id).closest(".provider-option");
      return !option || !option.hidden;
    }

    function markPanelDisconnected(error) {
      const message = error && error.message ? error.message : String(error || "未知错误");
      $("status").textContent = "连接失败：" + message;
      $("status").className = "connection danger-text";
      setText("enabledText", "连接中断");
      $("enabledText").className = "danger-text";
      setText("lastError", "与插件丢失连接：" + message);
    }

    function applyAutomaticFontPlaceholders(state) {
      const replacementFontName = $("replacementFontName");
      const replacementFontFile = $("replacementFontFile");
      if (!replacementFontName || !replacementFontFile) return;

      const hasCustomFont = Boolean((state.ReplacementFontName || "").trim() || (state.ReplacementFontFile || "").trim());
      const canUseAutomatic = !hasCustomFont && Boolean(state.EnableFontReplacement) && Boolean(state.AutoUseCjkFallbackFonts);
      const automaticName = canUseAutomatic ? (state.AutomaticReplacementFontName || "").trim() : "";
      const automaticFile = canUseAutomatic ? (state.AutomaticReplacementFontFile || "").trim() : "";
      replacementFontName.placeholder = automaticName ? `自动使用：${automaticName}` : defaultFontNamePlaceholder;
      replacementFontFile.placeholder = automaticFile ? `自动使用：${automaticFile}` : defaultFontFilePlaceholder;
    }

    function hasStateField(state, key) {
      return Object.prototype.hasOwnProperty.call(state, key);
    }

    function stateKeyFromInputId(id) {
      return id[0].toUpperCase() + id.slice(1);
    }

    function setStateTextValue(state, id, key) {
      if (!hasStateField(state, key) || !$(id)) return;
      $(id).value = state[key] || "";
    }

    function setStateNumberValues(state) {
      for (const id of numberFields) {
        const key = stateKeyFromInputId(id);
        if ($(id) && hasStateField(state, key)) $(id).value = state[key] ?? "";
      }
    }

    function setStateCheckboxValues(state) {
      for (const id of checks) {
        const key = stateKeyFromInputId(id);
        if ($(id) && hasStateField(state, key)) $(id).checked = Boolean(state[key]);
      }
    }

    function applyState(state) {
      setText("enabledText", state.Enabled ? "运行中" : "已暂停");
      $("enabledText").className = state.Enabled ? "ok" : "warn";
      setText("capturedTextCount", state.CapturedTextCount || 0);
      setText("queuedTextCount", state.QueueCount ?? state.QueuedTextCount ?? 0);
      setText("inFlightTranslationCount", state.InFlightTranslationCount || 0);
      setText("completedTranslationCount", state.CompletedTranslationCount || 0);
      setText("writebackQueueCount", state.WritebackQueueCount || 0);
      setText("cacheCount", state.CacheCount || 0);
      setText("providerName", providerNames[state.ProviderKind] || "-");
      setText("providerModel", state.Model || "-");
      setText("keyState", state.ApiKeyConfigured ? "已配置" : "未配置");
      $("keyState").className = state.ApiKeyConfigured ? "ok" : "warn";
      setText("totalTokenCount", formatNumber(state.TotalTokenCount || 0));
      setText("averageLatency", `${Math.round(state.AverageTranslationMilliseconds || 0)} ms`);
      setText("averageSpeed", `${formatNumber(Math.round(state.AverageCharactersPerSecond || 0))} 字/秒`);
      setText("lastError", state.LastError ? "最近错误：" + state.LastError : "");
      $("status").textContent = "已连接";
      $("status").className = "connection ok";
      renderRecentTranslations(state.RecentTranslations || []);
      if (isEditing()) return;

      if (hasStateField(state, "TargetLanguage")) {
        setSelectValue("targetLanguage", state.TargetLanguage || "zh-Hans", state.TargetLanguage || "zh-Hans");
        if (!$("glossaryTargetLanguage").value) $("glossaryTargetLanguage").value = state.TargetLanguage || "zh-Hans";
      }
      setKnownSelectValue("style", state.Style);
      setKnownSelectValue("providerKind", state.ProviderKind);
      setStateTextValue(state, "baseUrl", "BaseUrl");
      setStateTextValue(state, "endpoint", "Endpoint");
      setStateTextValue(state, "model", "Model");
      setStateTextValue(state, "replacementFontName", "ReplacementFontName");
      setStateTextValue(state, "replacementFontFile", "ReplacementFontFile");
      applyAutomaticFontPlaceholders(state);
      setStateNumberValues(state);
      if (hasStateField(state, "ReasoningEffort")) {
        setKnownSelectValue("reasoningEffort", state.ReasoningEffort);
        setKnownSelectValue("deepSeekReasoningEffort", normalizeDeepSeekEffort(state.ReasoningEffort));
      }
      setKnownSelectValue("outputVerbosity", state.OutputVerbosity);
      setKnownSelectValue("deepSeekThinkingMode", state.DeepSeekThinkingMode);
      setStateCheckboxValues(state);
      updateProviderUi(false);
      if (!promptTouched && hasStateField(state, "CustomPrompt")) {
        $("customPrompt").value = state.CustomPrompt || buildDefaultPrompt();
      }
    }

    function setSelectValue(id, value, label) {
      const select = $(id);
      if (!Array.from(select.options).some(option => option.value === value)) {
        select.add(new Option(label, value));
      }
      select.value = value;
    }

    function setKnownSelectValue(id, value) {
      const select = $(id);
      const normalized = value === null || value === undefined ? "" : String(value).trim();
      if (!normalized) return;
      const match = Array.from(select.options).find(option => option.value.toLowerCase() === normalized.toLowerCase());
      if (match) select.value = match.value;
    }

    function normalizeOpenAiEffort(value) {
      return ["none", "low", "medium", "high", "xhigh"].includes(value) ? value : "low";
    }

    function normalizeDeepSeekEffort(value) {
      const normalized = value === null || value === undefined ? "" : String(value).trim().toLowerCase();
      if (normalized === "max" || normalized === "xhigh") return "max";
      if (["low", "medium", "high"].includes(normalized)) return "high";
      return "";
    }

    function buildDefaultPrompt() {
      const targetLanguage = $("targetLanguage").value || "zh-Hans";
      const style = styleInstructions[Number($("style").value)] || styleInstructions[2];
      return `你是游戏本地化翻译引擎。目标语言：${targetLanguage}。
自动判断源语言；无论源语言是什么，都翻译为目标语言。
只输出译文，不要解释，不要寒暄，不要添加引号、Markdown 或“翻译如下”等前缀。
不要改变占位符、控制符、换行符、Unity 富文本标签或 TextMeshPro 标签。
允许自然本地化，避免机器翻译腔；菜单和按钮要短，对话要符合角色口吻。
${style}`;
    }

    function renderRecentTranslations(rows) {
      const target = $("recentTranslations");
      if (!rows.length) {
        target.innerHTML = '<p class="message">暂无最近完成的翻译。</p>';
        return;
      }
      target.innerHTML = rows.map(row => `
        <div class="recent-row">
          <div><span>原文</span><b title="${escapeHtml(row.SourceText)}">${escapeHtml(row.SourceText)}</b></div>
          <div><span>译文</span><b title="${escapeHtml(row.TranslatedText)}">${escapeHtml(row.TranslatedText)}</b></div>
          <div><span>${formatDateTime(row.CompletedUtc)}</span></div>
        </div>`).join("");
    }

    async function refresh() {
      try {
        applyState(await api("/api/state"));
      } catch (error) {
        markPanelDisconnected(error);
      }
    }

    async function saveConfigOnly() {
      applyState(await api("/api/config", { method: "POST", body: JSON.stringify(readConfig()) }));
    }

    async function savePendingApiKey() {
      const apiKey = $("apiKey").value.trim();
      if (!apiKey) return false;
      applyState(await api("/api/key", { method: "POST", body: JSON.stringify({ ApiKey: apiKey }) }));
      $("apiKey").value = "";
      return true;
    }

    async function saveConfig() {
      await saveConfigOnly();
      const keyUpdated = await savePendingApiKey();
      showToast(keyUpdated ? "配置和密钥已保存。" : "配置已保存。", "ok");
      promptTouched = false;
    }

    async function saveKey() {
      if (await savePendingApiKey()) {
        showToast("密钥状态已更新。", "ok");
        return;
      }

      showToast("未填写新密钥，已保留当前密钥。", "info");
    }

    async function loadGlossaryTerms() {
      const params = new URLSearchParams({
        search: glossaryState.search,
        sort: glossaryState.sort,
        direction: glossaryState.direction,
        offset: String(glossaryState.offset),
        limit: String(glossaryState.limit)
      });
      const page = await api("/api/glossary?" + params.toString());
      glossaryState.rows = page.Items || [];
      glossaryState.totalCount = page.TotalCount || 0;
      renderGlossaryTerms();
    }

    function renderGlossaryTerms() {
      $("glossaryBody").innerHTML = glossaryState.rows.map((row, index) => `
        <tr>
          <td><input type="checkbox" data-glossary-toggle="${index}" ${row.Enabled ? "checked" : ""}></td>
          <td title="${escapeHtml(row.SourceTerm)}">${escapeHtml(row.SourceTerm)}</td>
          <td title="${escapeHtml(row.TargetTerm)}">${escapeHtml(row.TargetTerm)}</td>
          <td>${escapeHtml(row.TargetLanguage)}</td>
          <td>${Number(row.Source) === 1 || row.Source === "Automatic" ? "AI 提取" : "人工"}</td>
          <td title="${escapeHtml(row.Note || "")}">${escapeHtml(row.Note || "")}</td>
          <td>${formatNumber(row.UsageCount || 0)}</td>
          <td>${formatDateTime(row.UpdatedUtc)}</td>
          <td>
            <button class="secondary" type="button" data-glossary-edit="${index}">编辑</button>
            <button class="danger" type="button" data-glossary-delete="${index}">删除</button>
          </td>
        </tr>`).join("");
      $("glossaryMessage").textContent = `共 ${glossaryState.totalCount} 条术语，当前显示 ${glossaryState.rows.length} 条。`;
      $$("[data-glossary-toggle]").forEach(input => {
        input.addEventListener("change", async () => {
          const row = Object.assign({}, glossaryState.rows[Number(input.dataset.glossaryToggle)]);
          row.Enabled = input.checked;
          await saveGlossaryTerm(row);
        });
      });
      $$("[data-glossary-edit]").forEach(button => {
        button.addEventListener("click", () => editGlossaryTerm(Number(button.dataset.glossaryEdit)));
      });
      $$("[data-glossary-delete]").forEach(button => {
        button.addEventListener("click", () => deleteGlossaryTerm(Number(button.dataset.glossaryDelete)));
      });
    }

    function readGlossaryForm() {
      return {
        SourceTerm: $("glossarySourceTerm").value.trim(),
        TargetTerm: $("glossaryTargetTerm").value.trim(),
        TargetLanguage: ($("glossaryTargetLanguage").value.trim() || $("targetLanguage").value || "zh-Hans"),
        Note: $("glossaryNote").value.trim(),
        Enabled: true
      };
    }

    function clearGlossaryForm() {
      $("glossarySourceTerm").value = "";
      $("glossaryTargetTerm").value = "";
      $("glossaryTargetLanguage").value = $("targetLanguage").value || "zh-Hans";
      $("glossaryNote").value = "";
    }

    function editGlossaryTerm(index) {
      const row = glossaryState.rows[index];
      if (!row) return;
      $("glossarySourceTerm").value = row.SourceTerm || "";
      $("glossaryTargetTerm").value = row.TargetTerm || "";
      $("glossaryTargetLanguage").value = row.TargetLanguage || $("targetLanguage").value || "zh-Hans";
      $("glossaryNote").value = row.Note || "";
    }

    async function saveGlossaryTerm(term) {
      const payload = term || readGlossaryForm();
      if (!payload.SourceTerm || !payload.TargetTerm) {
        $("glossaryMessage").textContent = "请填写原术语和指定译名。";
        return;
      }

      await api("/api/glossary", { method: term ? "PATCH" : "POST", body: JSON.stringify(payload) });
      clearGlossaryForm();
      await loadGlossaryTerms();
      showToast("术语已保存。", "ok");
    }

    async function deleteGlossaryTerm(index) {
      const row = glossaryState.rows[index];
      if (!row) return;
      if (!confirm(`删除术语「${row.SourceTerm}」？`)) return;
      const result = await api("/api/glossary", { method: "DELETE", body: JSON.stringify([row]) });
      await loadGlossaryTerms();
      $("glossaryMessage").textContent = `已删除 ${result.DeletedCount || 1} 条术语。`;
    }

    function renderModels(result) {
      const models = result.Models || [];
      if (!result.Succeeded) {
        showToast(result.Message || "获取模型列表失败。", "danger");
        return;
      }
      const details = models.length ? "\n" + compactLines(models, item => item.Id) : "";
      showToast(`已获取 ${models.length} 个模型。${details}`, "ok", models.length > 4 ? 8000 : 5200);
    }

    function renderBalance(result) {
      const balances = result.Balances || [];
      if (!result.Succeeded) {
        showToast(result.Message || "查询余额/成本失败。", "danger");
        return;
      }
      const details = balances.length ? "\n" + compactLines(balances, item => `${item.Currency}：${item.TotalBalance}`) : "";
      showToast((result.Message || "查询完成。") + details, "ok", balances.length > 4 ? 8000 : 5200);
    }

    function renderProviderTest(result) {
      showToast(result.Succeeded ? "连接可用。" : (result.Message || "连接失败。"), result.Succeeded ? "ok" : "danger");
    }

    function updateProviderUi(applyDefaults) {
      const provider = $("providerKind").value;
      if (applyDefaults) {
        const defaults = providerDefaults[provider];
        $("baseUrl").value = defaults.baseUrl;
        $("endpoint").value = defaults.endpoint;
        $("model").value = defaults.model;
      }
      $$(".provider-option").forEach(item => {
        item.hidden = !item.dataset.providers.split(",").includes(provider);
      });
      renderModelPresetOptions(provider);
      updateModelPresetFromInput();
    }

    function renderModelPresetOptions(provider) {
      const select = $("modelPreset");
      const current = select.dataset.provider;
      if (current === provider) return;
      select.dataset.provider = provider;
      select.innerHTML = modelPresets[provider].map(([value, label]) => `<option value="${value}">${label}</option>`).join("");
    }

    function updateModelPresetFromInput() {
      const model = $("model").value;
      const preset = Array.from($("modelPreset").options).find(option => option.value === model);
      $("modelPreset").value = preset ? model : "custom";
    }

    function loadColumnLayout() {
      loadColumnOrder();
      loadColumnVisibility();
    }

    function loadColumnFilters() {
      try {
        const saved = JSON.parse(localStorage.getItem(columnFilterStorageKey) || "{}");
        tableState.filters = saved && typeof saved === "object" ? saved : {};
      } catch (_) {
        localStorage.removeItem(columnFilterStorageKey);
        tableState.filters = {};
      }
    }

    function persistColumnFilters() {
      localStorage.setItem(columnFilterStorageKey, JSON.stringify(tableState.filters));
    }

    function activeFilterCount() {
      return Object.values(tableState.filters).filter(values => Array.isArray(values) && values.length > 0).length;
    }

    function appendColumnFilters(params, excludedColumn) {
      Object.entries(tableState.filters).forEach(([column, values]) => {
        if (column === excludedColumn || !Array.isArray(values)) return;
        values.forEach(value => params.append("filter." + column, value === null || value === "" ? emptyFilterValue : value));
      });
    }

    function filterValuesFor(column) {
      return new Set((tableState.filters[column] || []).map(value => value === "" ? null : value));
    }

    function displayFilterValue(value, column) {
      if (value === null || value === undefined || value === "") return "空值";
      const definition = tableColumns.find(item => item.sort === column);
      return definition && definition.time ? formatDateTime(value) : value;
    }

    function loadColumnOrder() {
      try {
        const keys = JSON.parse(localStorage.getItem(columnOrderStorageKey) || "null");
        if (!Array.isArray(keys)) return;
        const ordered = [];
        const remaining = tableColumns.slice();
        keys.forEach(key => {
          const index = remaining.findIndex(column => column.key === key);
          if (index >= 0) ordered.push(remaining.splice(index, 1)[0]);
        });
        if (!ordered.length) return;
        tableColumns.splice(0, tableColumns.length, ...ordered, ...remaining);
      } catch (_) {
        localStorage.removeItem(columnOrderStorageKey);
      }
    }

    function loadColumnVisibility() {
      try {
        const keys = JSON.parse(localStorage.getItem(columnVisibilityStorageKey) || "null");
        if (!Array.isArray(keys)) return;
        const visibleKeys = new Set(keys.filter(key => tableColumns.some(column => column.key === key)));
        if (!visibleKeys.size) return;
        tableColumns.forEach(column => { column.visible = visibleKeys.has(column.key); });
        ensureVisibleColumn();
      } catch (_) {
        localStorage.removeItem(columnVisibilityStorageKey);
      }
    }

    function persistColumnVisibility() {
      localStorage.setItem(columnVisibilityStorageKey, JSON.stringify(visibleTableColumns().map(column => column.key)));
    }

    function persistColumnOrder() {
      localStorage.setItem(columnOrderStorageKey, JSON.stringify(tableColumns.map(column => column.key)));
    }

    function ensureVisibleColumn() {
      if (tableColumns.every(column => column.visible === false)) tableColumns[0].visible = true;
    }

    function visibleTableColumns() {
      ensureVisibleColumn();
      return tableColumns
        .map((column, index) => Object.assign({ index }, column))
        .filter(column => column.visible !== false);
    }

    function visibleColumnIndexes() {
      return new Set(visibleTableColumns().map(column => column.index));
    }

    function pruneHiddenSelection() {
      const indexes = visibleColumnIndexes();
      tableState.selected = new Set(Array.from(tableState.selected).filter(key => indexes.has(parseCellKey(key)[1])));
      if (tableState.anchor && !indexes.has(tableState.anchor.col)) tableState.anchor = null;
    }

    function renderColumnChooser() {
      const visibleCount = visibleTableColumns().length;
      $("columnChooser").innerHTML = `
        <div class="column-chooser-head">
          <span>显示列</span>
          <span>${visibleCount}/${tableColumns.length}</span>
        </div>
        <div class="column-chooser-list">
          ${tableColumns.map((column, index) => `
            <div class="column-choice">
              <label class="check column-choice-label">
                <input type="checkbox" data-column-key="${column.key}" ${column.visible !== false ? "checked" : ""} ${column.visible !== false && visibleCount === 1 ? "disabled" : ""}>
                <span>${column.title}</span>
              </label>
              <div class="column-move-buttons">
                <button type="button" data-column-move="${column.key}" data-column-direction="up" title="上移" aria-label="上移 ${column.title}" ${index === 0 ? "disabled" : ""}>↑</button>
                <button type="button" data-column-move="${column.key}" data-column-direction="down" title="下移" aria-label="下移 ${column.title}" ${index === tableColumns.length - 1 ? "disabled" : ""}>↓</button>
              </div>
            </div>`).join("")}
        </div>
        <div class="column-chooser-actions">
          <button id="showAllColumns" type="button">全部显示</button>
        </div>`;
      $$("input[data-column-key]").forEach(input => {
        input.addEventListener("change", () => updateColumnVisibility(input.dataset.columnKey, input.checked));
      });
      $$("button[data-column-move]").forEach(button => {
        button.addEventListener("click", () => moveColumn(button.dataset.columnMove, button.dataset.columnDirection));
      });
      $("showAllColumns").addEventListener("click", showAllColumns);
    }

    function updateColumnVisibility(key, visible) {
      const column = tableColumns.find(item => item.key === key);
      if (!column) return;
      column.visible = visible;
      ensureVisibleColumn();
      persistColumnVisibility();
      pruneHiddenSelection();
      renderColumnChooser();
      renderTranslationTable();
    }

    function moveColumn(key, direction) {
      const index = tableColumns.findIndex(item => item.key === key);
      if (index < 0) return;
      const targetIndex = direction === "up" ? index - 1 : index + 1;
      if (targetIndex < 0 || targetIndex >= tableColumns.length) return;
      const column = tableColumns.splice(index, 1)[0];
      tableColumns.splice(targetIndex, 0, column);
      tableState.selected.clear();
      tableState.anchor = null;
      persistColumnOrder();
      renderColumnChooser();
      renderTranslationTable();
    }

    function showAllColumns() {
      tableColumns.forEach(column => { column.visible = true; });
      persistColumnVisibility();
      renderColumnChooser();
      renderTranslationTable();
    }

    function toggleColumnChooser() {
      const chooser = $("columnChooser");
      const nextOpen = !chooser.classList.contains("open");
      chooser.classList.toggle("open", nextOpen);
      $("columnMenuButton").setAttribute("aria-expanded", String(nextOpen));
    }

    function hideColumnChooser() {
      $("columnChooser").classList.remove("open");
      $("columnMenuButton").setAttribute("aria-expanded", "false");
    }

    function renderColgroup() {
      $("translationColgroup").innerHTML = visibleTableColumns().map(column => `<col style="width:${column.width}px">`).join("");
    }

    function renderTableHead() {
      const columns = visibleTableColumns();
      renderColgroup();
      $("translationHead").innerHTML = columns.map(column => {
        const mark = tableState.sort === column.sort ? (tableState.direction === "desc" ? "↓" : "↑") : "";
        const active = tableState.filters[column.sort] && tableState.filters[column.sort].length ? "filter-active" : "";
        return `<th data-sort="${column.sort}" data-col="${column.index}">
          <div class="header-inner">
            <span class="header-title">${column.title}</span>
            <span>${mark}</span>
            <button class="header-filter ${active}" type="button" data-filter-column="${column.sort}" title="筛选 ${column.title}" aria-label="筛选 ${column.title}">⌄</button>
          </div>
          <span class="col-resizer" data-col="${column.index}"></span>
        </th>`;
      }).join("");
      $$("#translationHead th").forEach(th => th.addEventListener("click", event => {
        if (event.target.classList.contains("col-resizer")) return;
        if (event.target.closest("[data-filter-column]")) return;
        const sort = th.dataset.sort;
        if (tableState.sort === sort) tableState.direction = tableState.direction === "desc" ? "asc" : "desc";
        else { tableState.sort = sort; tableState.direction = "asc"; }
        loadTranslations();
      }));
      $$("[data-filter-column]").forEach(button => {
        button.addEventListener("click", event => {
          event.stopPropagation();
          openColumnFilterMenu(button.dataset.filterColumn, button);
        });
      });
      $$(".col-resizer").forEach(handle => handle.addEventListener("pointerdown", startColumnResize));
    }

    async function loadColumnFilterOptions(column, optionSearch) {
      const params = new URLSearchParams({
        column,
        search: tableState.search,
        optionSearch: optionSearch || "",
        limit: "200"
      });
      appendColumnFilters(params, column);
      return api("/api/translations/filter-options?" + params.toString());
    }

    async function openColumnFilterMenu(column, anchor) {
      const menu = $("columnFilterMenu");
      tableState.openFilterColumn = column;
      tableState.filterOptionDraft = filterValuesFor(column);
      menu.innerHTML = `<div class="message">正在加载筛选值...</div>`;
      menu.classList.add("open");
      positionColumnFilterMenu(menu, anchor);
      const page = await loadColumnFilterOptions(column, "");
      renderColumnFilterMenu(column, anchor, page.Items || []);
    }

    function positionColumnFilterMenu(menu, anchor) {
      const rect = anchor.getBoundingClientRect();
      const left = Math.min(rect.left, window.innerWidth - menu.offsetWidth - 12);
      menu.style.left = Math.max(12, left) + "px";
      menu.style.top = Math.min(rect.bottom + 6, window.innerHeight - menu.offsetHeight - 12) + "px";
    }

    function renderColumnFilterMenu(column, anchor, items) {
      const menu = $("columnFilterMenu");
      menu.innerHTML = `
        <input id="columnFilterSearch" placeholder="搜索筛选值">
        <div class="filter-option-list">
          ${items.map(item => {
            const encoded = item.Value === null || item.Value === undefined ? "" : String(item.Value);
            const checked = tableState.filterOptionDraft.has(item.Value) ? "checked" : "";
            return `<label class="filter-option-row" title="${escapeHtml(displayFilterValue(item.Value, column))}">
              <input type="checkbox" data-filter-value="${escapeHtml(encoded)}" data-filter-empty="${item.Value === null || item.Value === undefined ? "true" : "false"}" ${checked}>
              <span>${escapeHtml(displayFilterValue(item.Value, column))}</span>
              <small>${item.Count}</small>
            </label>`;
          }).join("")}
        </div>
        <div class="filter-menu-actions">
          <button id="selectAllFilterOptions" type="button" class="secondary">全选</button>
          <button id="clearColumnFilter" type="button" class="secondary">清空</button>
          <button id="applyColumnFilter" type="button">应用</button>
        </div>`;
      $("columnFilterSearch").addEventListener("input", async event => {
        const value = event.target.value;
        const nextPage = await loadColumnFilterOptions(column, value);
        renderColumnFilterMenu(column, anchor, nextPage.Items || []);
        $("columnFilterSearch").value = value;
        $("columnFilterSearch").focus();
      });
      $$(".filter-option-row input").forEach(input => {
        input.addEventListener("change", () => {
          const value = input.dataset.filterEmpty === "true" ? null : input.dataset.filterValue;
          if (input.checked) tableState.filterOptionDraft.add(value);
          else tableState.filterOptionDraft.delete(value);
        });
      });
      $("selectAllFilterOptions").addEventListener("click", () => {
        $$(".filter-option-row input").forEach(input => {
          input.checked = true;
          tableState.filterOptionDraft.add(input.dataset.filterEmpty === "true" ? null : input.dataset.filterValue);
        });
      });
      $("clearColumnFilter").addEventListener("click", () => applyColumnFilter(column, []));
      $("applyColumnFilter").addEventListener("click", () => applyColumnFilter(column, Array.from(tableState.filterOptionDraft)));
      positionColumnFilterMenu(menu, anchor);
    }

    function applyColumnFilter(column, values) {
      const cleanValues = values.filter((value, index, array) => array.findIndex(item => item === value) === index);
      if (cleanValues.length) tableState.filters[column] = cleanValues;
      else delete tableState.filters[column];
      tableState.offset = 0;
      tableState.selected.clear();
      tableState.anchor = null;
      persistColumnFilters();
      hideColumnFilterMenu();
      loadTranslations();
    }

    function clearAllColumnFilters() {
      tableState.filters = {};
      tableState.offset = 0;
      persistColumnFilters();
      hideColumnFilterMenu();
      loadTranslations();
    }

    function hideColumnFilterMenu() {
      $("columnFilterMenu").classList.remove("open");
      tableState.openFilterColumn = null;
    }

    function startColumnResize(event) {
      event.preventDefault();
      event.stopPropagation();
      const index = Number(event.currentTarget.dataset.col);
      const startX = event.clientX;
      const startWidth = tableColumns[index].width;
      const move = moveEvent => {
        tableColumns[index].width = Math.max(72, Math.min(640, startWidth + moveEvent.clientX - startX));
        renderColgroup();
      };
      const up = () => {
        document.removeEventListener("pointermove", move);
        document.removeEventListener("pointerup", up);
      };
      document.addEventListener("pointermove", move);
      document.addEventListener("pointerup", up);
    }

    function renderTranslationTable(page) {
      if (page && typeof page.TotalCount === "number") tableState.totalCount = page.TotalCount;
      const columns = visibleTableColumns();
      pruneHiddenSelection();
      renderTableHead();
      $("translationBody").innerHTML = tableState.rows.map((row, rowIndex) => `<tr>${columns.map(column => {
        const key = cellKey(rowIndex, column.index);
        const editable = column.editable;
        const value = row[column.key] ?? "";
        const display = column.time ? formatDateTime(value) : value;
        const selected = tableState.selected.has(key) ? "selected" : "";
        const dirty = tableState.dirty.has(rowKey(row)) && editable ? "dirty" : "";
        return `<td data-cell="${key}" data-row="${rowIndex}" data-col="${column.index}" class="${selected} ${dirty}" title="${escapeHtml(value)}">
          <div class="cell-text" ${editable ? 'contenteditable="true"' : ""}>${escapeHtml(display)}</div>
        </td>`;
      }).join("")}</tr>`).join("");
      $$("td[data-cell]").forEach(cell => {
        cell.addEventListener("click", event => selectCell(cell, event));
        const editable = cell.querySelector("[contenteditable]");
        if (editable) editable.addEventListener("input", () => markDirty(cell));
      });
      const filters = activeFilterCount();
      $("tableMessage").textContent = `共 ${tableState.totalCount || 0} 条，当前显示 ${tableState.rows.length} 条，${columns.length}/${tableColumns.length} 列${filters ? `，已筛选 ${filters} 列` : ""}。`;
    }

    async function loadTranslations() {
      const params = new URLSearchParams({
        search: tableState.search,
        sort: tableState.sort,
        direction: tableState.direction,
        offset: String(tableState.offset),
        limit: String(tableState.limit)
      });
      appendColumnFilters(params);
      const page = await api("/api/translations?" + params.toString());
      tableState.rows = page.Items || [];
      tableState.selected.clear();
      tableState.anchor = null;
      renderTranslationTable(page);
    }

    function cellKey(row, col) { return row + ":" + col; }
    function parseCellKey(key) { return key.split(":").map(Number); }
    function rowKey(row) {
      return [row.SourceText, row.TargetLanguage, row.SceneName, row.ComponentHierarchy, row.ProviderKind, row.ProviderBaseUrl, row.ProviderEndpoint, row.ProviderModel, row.PromptPolicyVersion].join("\u001f");
    }

    function selectCell(cell, event) {
      const row = Number(cell.dataset.row);
      const col = Number(cell.dataset.col);
      if (event.shiftKey && tableState.anchor) {
        selectRange(tableState.anchor.row, tableState.anchor.col, row, col, event.ctrlKey || event.metaKey);
        return;
      }
      const key = cellKey(row, col);
      if (event.ctrlKey || event.metaKey) {
        if (tableState.selected.has(key)) tableState.selected.delete(key);
        else tableState.selected.add(key);
      } else {
        tableState.selected.clear();
        tableState.selected.add(key);
      }
      tableState.anchor = { row, col };
      paintSelection();
    }

    function selectRange(startRow, startCol, endRow, endCol, additive) {
      if (!additive) tableState.selected.clear();
      const columns = visibleTableColumns();
      const startPosition = columns.findIndex(column => column.index === startCol);
      const endPosition = columns.findIndex(column => column.index === endCol);
      if (startPosition < 0 || endPosition < 0) return;
      const minRow = Math.min(startRow, endRow);
      const maxRow = Math.max(startRow, endRow);
      const minCol = Math.min(startPosition, endPosition);
      const maxCol = Math.max(startPosition, endPosition);
      for (let row = minRow; row <= maxRow; row++) {
        for (let col = minCol; col <= maxCol; col++) tableState.selected.add(cellKey(row, columns[col].index));
      }
      paintSelection();
    }

    function selectAllCells() {
      tableState.selected.clear();
      const columns = visibleTableColumns();
      tableState.rows.forEach((_, row) => columns.forEach(column => tableState.selected.add(cellKey(row, column.index))));
      tableState.anchor = columns.length ? { row: 0, col: columns[0].index } : null;
      paintSelection();
    }

    function paintSelection() {
      $$("td[data-cell]").forEach(cell => cell.classList.toggle("selected", tableState.selected.has(cell.dataset.cell)));
    }

    function markDirty(cell) {
      const rowIndex = Number(cell.dataset.row);
      const colIndex = Number(cell.dataset.col);
      const row = Object.assign({}, tableState.rows[rowIndex]);
      row[tableColumns[colIndex].key] = cell.innerText;
      tableState.rows[rowIndex] = row;
      tableState.dirty.set(rowKey(row), row);
      cell.classList.add("dirty");
    }

    async function copyCells() {
      if (!tableState.selected.size) {
        $("tableMessage").textContent = "没有选中的单元格。";
        return;
      }
      const visibleIndexes = visibleColumnIndexes();
      const cells = Array.from(tableState.selected).map(key => {
        const [row, col] = parseCellKey(key);
        return { row, col, value: tableState.rows[row]?.[tableColumns[col].key] ?? "" };
      }).filter(cell => visibleIndexes.has(cell.col)).sort((a, b) => a.row - b.row || a.col - b.col);
      if (!cells.length) {
        $("tableMessage").textContent = "没有可复制的可见单元格。";
        return;
      }
      const rows = [...new Set(cells.map(cell => cell.row))].sort((a, b) => a - b);
      const cols = [...new Set(cells.map(cell => cell.col))].sort((a, b) => a - b);
      const lines = rows.map(row => cols.map(col => tableState.selected.has(cellKey(row, col)) ? tableState.rows[row]?.[tableColumns[col].key] ?? "" : "").join("\t"));
      await navigator.clipboard.writeText(lines.join("\n"));
      $("tableMessage").textContent = "已复制选区。";
    }

    async function pasteCells() {
      const first = Array.from(tableState.selected)[0];
      if (!first) {
        $("tableMessage").textContent = "请先选择粘贴起点。";
        return;
      }
      const [startRow, startCol] = parseCellKey(first);
      const columns = visibleTableColumns();
      const startColumnPosition = columns.findIndex(column => column.index === startCol);
      if (startColumnPosition < 0) {
        $("tableMessage").textContent = "请先选择可见列中的粘贴起点。";
        return;
      }
      const text = await navigator.clipboard.readText();
      const lines = text.replace(/\r\n/g, "\n").split("\n");
      lines.forEach((line, rowOffset) => {
        line.split("\t").forEach((value, colOffset) => {
          const rowIndex = startRow + rowOffset;
          const column = columns[startColumnPosition + colOffset];
          if (!tableState.rows[rowIndex] || !column || !column.editable) return;
          const row = Object.assign({}, tableState.rows[rowIndex]);
          row[column.key] = value;
          tableState.rows[rowIndex] = row;
          tableState.dirty.set(rowKey(row), row);
        });
      });
      renderTranslationTable();
      $("tableMessage").textContent = "已粘贴，等待保存。";
    }

    function selectedRows() {
      const rowIndexes = [...new Set(Array.from(tableState.selected).map(key => parseCellKey(key)[0]))].sort((a, b) => a - b);
      return rowIndexes.map(index => tableState.rows[index]).filter(Boolean);
    }

    async function retranslateSelectedRows() {
      const rows = selectedRows();
      if (!rows.length) {
        $("tableMessage").textContent = "没有选中的已翻译文本。";
        return;
      }

      const result = await api("/api/translations/retranslate", { method: "POST", body: JSON.stringify(rows) });
      $("tableMessage").textContent = `已提交 ${result.QueuedCount || 0}/${result.RequestedCount || rows.length} 行重新翻译。`;
      await refresh();
    }

    async function highlightSelectedRow() {
      const rows = selectedRows();
      if (!rows.length) {
        $("tableMessage").textContent = "没有选中的已翻译文本。";
        return;
      }

      const result = await api("/api/translations/highlight", { method: "POST", body: JSON.stringify(rows[0]) });
      $("tableMessage").textContent = result.Message || "已发送高亮请求。";
    }

    async function deleteSelectedRows() {
      const rows = selectedRows();
      if (!rows.length) {
        $("tableMessage").textContent = "没有选中的已翻译文本。";
        return;
      }
      if (!confirm(`删除选中的 ${rows.length} 条已翻译文本？`)) return;
      const result = await api("/api/translations", { method: "DELETE", body: JSON.stringify(rows) });
      tableState.dirty.clear();
      await loadTranslations();
      $("tableMessage").textContent = `已删除 ${result.DeletedCount || rows.length} 条已翻译文本。`;
    }

    async function saveRows() {
      for (const row of tableState.dirty.values()) {
        await api("/api/translations", { method: "PATCH", body: JSON.stringify(row) });
      }
      tableState.dirty.clear();
      await loadTranslations();
      $("tableMessage").textContent = "修改已保存。";
    }

    function inferExportFormat(fileName) {
      return String(fileName || "").toLowerCase().endsWith(".csv") ? "csv" : "json";
    }

    function hideExportMenu() {
      $("exportMenu").classList.remove("open");
      $("exportRows").setAttribute("aria-expanded", "false");
    }

    function toggleExportMenu() {
      const menu = $("exportMenu");
      const isOpen = !menu.classList.contains("open");
      menu.classList.toggle("open", isOpen);
      $("exportRows").setAttribute("aria-expanded", isOpen ? "true" : "false");
    }

    async function fetchExportBlob(format) {
      const response = await fetch("/api/translations/export?format=" + format, { cache: "no-store" });
      if (!response.ok) {
        throw new Error(await response.text() || "导出失败。");
      }
      return await response.blob();
    }

    async function saveExportWithPicker() {
      const handle = await window.showSaveFilePicker({
        suggestedName: "translations.json",
        types: exportFileTypes,
        excludeAcceptAllOption: true
      });
      const format = inferExportFormat(handle.name);
      const blob = await fetchExportBlob(format);
      const writable = await handle.createWritable();
      await writable.write(blob);
      await writable.close();
      $("tableMessage").textContent = `已导出 ${handle.name || ("translations." + format)}。`;
    }

    async function downloadExport(format) {
      const blob = await fetchExportBlob(format);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "translations." + format;
      a.click();
      URL.revokeObjectURL(url);
    }

    async function exportRows() {
      hideExportMenu();
      if (window.showSaveFilePicker) {
        try {
          await saveExportWithPicker();
          return;
        } catch (error) {
          if (error && error.name === "AbortError") return;
          $("tableMessage").textContent = error && error.message ? error.message : "导出失败。";
          toggleExportMenu();
          return;
        }
      }
      toggleExportMenu();
    }

    function openImportPicker() {
      $("importFile").value = "";
      $("importFile").click();
    }

    async function importRows() {
      const file = $("importFile").files[0];
      if (!file) return;
      const format = inferExportFormat(file.name);
      const content = await file.text();
      const result = await api("/api/translations/import?format=" + format, { method: "POST", body: content });
      $("tableMessage").textContent = result.Errors && result.Errors.length ? result.Errors.join("; ") : `已导入 ${result.ImportedCount} 条。`;
      await loadTranslations();
    }

    function showContextMenu(event) {
      event.preventDefault();
      const menu = $("tableContextMenu");
      menu.style.left = Math.min(event.clientX, window.innerWidth - 210) + "px";
      menu.style.top = Math.min(event.clientY, window.innerHeight - 190) + "px";
      menu.classList.add("open");
    }

    function hideContextMenu() {
      $("tableContextMenu").classList.remove("open");
    }

    async function handleTableAction(action) {
      hideContextMenu();
      if (action === "refresh") await loadTranslations();
      if (action === "copy") await copyCells();
      if (action === "paste") await pasteCells();
      if (action === "retranslate") await retranslateSelectedRows();
      if (action === "highlight") await highlightSelectedRow();
      if (action === "delete") await deleteSelectedRows();
      if (action === "save") await saveRows();
    }

    function formatNumber(value) {
      return Number(value || 0).toLocaleString("zh-CN");
    }

    function formatDateTime(value) {
      if (!value) return "";
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return String(value);
      const pad = number => String(number).padStart(2, "0");
      return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
    }

    function escapeHtml(value) {
      return String(value ?? "").replace(/[&<>"']/g, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[ch]));
    }

    $$(".nav-item").forEach(button => button.addEventListener("click", () => showPage(button.dataset.page)));
    $("themeCycle").addEventListener("click", cycleTheme);
    $("refresh").addEventListener("click", refresh);
    $("savePlugin").addEventListener("click", saveConfig);
    $("saveAi").addEventListener("click", saveConfig);
    $("saveGlossarySettings").addEventListener("click", saveConfig);
    $("saveKey").addEventListener("click", saveKey);
    $("providerKind").addEventListener("change", () => updateProviderUi(true));
    $("modelPreset").addEventListener("change", () => {
      if ($("modelPreset").value !== "custom") $("model").value = $("modelPreset").value;
    });
    $("model").addEventListener("input", updateModelPresetFromInput);
    $("targetLanguage").addEventListener("change", () => { if (!promptTouched) $("customPrompt").value = buildDefaultPrompt(); });
    $("style").addEventListener("change", () => { if (!promptTouched) $("customPrompt").value = buildDefaultPrompt(); });
    $("customPrompt").addEventListener("input", () => { promptTouched = true; });
    $("fetchModels").addEventListener("click", () => runProviderUtility("获取模型列表", async () => renderModels(await api("/api/provider/models"))));
    $("fetchBalance").addEventListener("click", () => runProviderUtility("查询余额/成本", async () => renderBalance(await api("/api/provider/balance"))));
    $("testProvider").addEventListener("click", () => runProviderUtility("测试连接", async () => { renderProviderTest(await api("/api/provider/test", { method: "POST", body: "{}" })); await refresh(); }));
    $("tableSearch").addEventListener("input", event => { tableState.search = event.target.value; tableState.offset = 0; loadTranslations(); });
    $("glossarySearch").addEventListener("input", event => { glossaryState.search = event.target.value; glossaryState.offset = 0; loadGlossaryTerms(); });
    $("refreshGlossary").addEventListener("click", loadGlossaryTerms);
    $("addGlossaryTerm").addEventListener("click", () => saveGlossaryTerm());
    $("clearGlossaryForm").addEventListener("click", clearGlossaryForm);
    $("clearTableFilters").addEventListener("click", clearAllColumnFilters);
    $("columnMenuButton").addEventListener("click", event => {
      event.stopPropagation();
      toggleColumnChooser();
    });
    $("columnChooser").addEventListener("click", event => event.stopPropagation());
    $("saveRows").addEventListener("click", saveRows);
    $("exportRows").addEventListener("click", event => {
      event.stopPropagation();
      exportRows();
    });
    $("exportMenu").addEventListener("click", event => event.stopPropagation());
    $$("[data-export-format]").forEach(button => {
      button.addEventListener("click", async () => {
        hideExportMenu();
        await downloadExport(button.dataset.exportFormat);
      });
    });
    $("importRows").addEventListener("click", openImportPicker);
    $("importFile").addEventListener("change", importRows);
    $("tableWrap").addEventListener("contextmenu", showContextMenu);
    document.addEventListener("click", event => {
      if (!event.target.closest("#tableContextMenu")) hideContextMenu();
      if (!event.target.closest("#columnChooser") && !event.target.closest("#columnMenuButton")) hideColumnChooser();
      if (!event.target.closest("#exportMenu") && !event.target.closest("#exportRows")) hideExportMenu();
      if (!event.target.closest("#columnFilterMenu") && !event.target.closest("[data-filter-column]")) hideColumnFilterMenu();
    });
    $$("#tableContextMenu [data-table-action]").forEach(button => {
      button.addEventListener("click", () => handleTableAction(button.dataset.tableAction));
    });
    document.addEventListener("keydown", event => {
      if (!document.activeElement.closest("#page-editor")) return;
      if (event.ctrlKey && event.key.toLowerCase() === "a") {
        event.preventDefault();
        selectAllCells();
      } else if (event.ctrlKey && event.key.toLowerCase() === "c") {
        event.preventDefault();
        copyCells();
      } else if (event.ctrlKey && event.key.toLowerCase() === "v") {
        event.preventDefault();
        pasteCells();
      } else if (event.key === "Delete") {
        event.preventDefault();
        deleteSelectedRows();
      }
    });

    applyTheme(localStorage.getItem("hunity.theme") || "system");
    loadColumnLayout();
    loadColumnFilters();
    renderColumnChooser();
    updateProviderUi(false);
    refresh();
    setInterval(refresh, 2000);
  </script>
</body>
</html>
""";
}
