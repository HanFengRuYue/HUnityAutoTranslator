<template>
  <div class="app-shell">
    <aside class="sidebar">
      <div class="brand">
        <strong>HUnityAutoTranslator</strong>
        <span>本机控制面板</span>
      </div>
      <nav class="nav-list" aria-label="功能导航">
        <button class="nav-item active" type="button">运行状态</button>
        <button class="nav-item" type="button">插件设置</button>
        <button class="nav-item" type="button">AI 翻译设置</button>
        <button class="nav-item" type="button">术语库</button>
        <button class="nav-item" type="button">文本编辑</button>
        <button class="nav-item" type="button">版本信息</button>
      </nav>
      <div class="sidebar-footer">
        <div class="connection">正在连接...</div>
        <button class="theme-cycle" type="button">
          <span>主题</span>
          <strong>浅色</strong>
        </button>
      </div>
    </aside>
    <main class="workspace">
      <section class="page active">
        <div class="page-head">
          <h1>运行状态</h1>
          <button class="secondary" type="button">刷新</button>
        </div>
        <div class="band">
          <p>Vue 控制面板已加载。</p>
        </div>
      </section>
    </main>
  </div>
</template>
