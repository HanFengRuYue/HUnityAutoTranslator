<p align="center">
  <img src="src/HUnityAutoTranslator.ControlPanel/public/branding/hunity-icon-blue-white-128.png" width="96" height="96" alt="HUnityAutoTranslator logo">
</p>

<h1 align="center">HUnityAutoTranslator</h1>

<p align="center">
  <strong>面向 Unity / BepInEx 6 的游戏文本、字体与贴图本地化插件。</strong>
</p>

<p align="center">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/License-MIT-2f6fed"></a>
  <img alt="BepInEx 6" src="https://img.shields.io/badge/BepInEx-6%20Bleeding%20Edge-5b6ee1">
  <img alt="Unity Mono and IL2CPP" src="https://img.shields.io/badge/Unity-Mono%20%7C%20IL2CPP-222222">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-SDK%2010-512bd4">
  <img alt="Vue" src="https://img.shields.io/badge/Control%20Panel-Vue%203-42b883">
</p>

HUnityAutoTranslator 是一个面向 Unity 游戏的运行时自动翻译插件。它通过 BepInEx 6 加载，在游戏运行时捕获 UGUI、TextMeshPro 和 IMGUI 文本，交给在线 AI 服务或本地 llama.cpp 模型翻译，再把译文写回游戏界面。项目同时提供本机中文控制面板、SQLite 缓存编辑、术语库、字体辅助、贴图导出导入和贴图文字翻译工作流。

> 当前版本仍处于早期迭代阶段，重点目标是可检查、可回退、兼容优先。建议先在单个游戏目录中验证，再复制到长期使用的游戏环境。

## 核心亮点

| 能力 | 说明 |
| --- | --- |
| 运行时文本翻译 | 覆盖 Unity UGUI `Text`、TextMeshPro `TMP_Text`、常见 IMGUI 字符串重载。 |
| 多服务商 AI 配置 | 支持 OpenAI Responses、DeepSeek、OpenAI 兼容接口和 llama.cpp 本地模型。 |
| 中文控制面板 | 默认只监听 `127.0.0.1:48110`，可查看状态、调整配置、编辑缓存和管理术语。 |
| 本地可审计缓存 | 翻译结果保存在本地 SQLite，支持表格编辑、筛选、导入导出和手动覆盖 AI 结果。 |
| 术语与上下文 | 可维护固定译名，提示词会结合场景、组件层级、同界面文本和历史样例。 |
| 字体辅助 | 支持 UGUI、TMP、IMGUI 的按需中文字体辅助，优先保留原字体；IMGUI 只在当前绘制项缺字时临时辅助并立即恢复。 |
| 贴图本地化 | 可扫描贴图、导出源图、导入覆盖图，并支持图片模型辅助识别和翻译贴图文字。 |
| 本地模型工作流 | 可选打包 llama.cpp CUDA 13 / Vulkan 后端，控制面板内选择 `.gguf` 模型并启动。 |

## 功能概览

- Unity 后端：Unity Mono 与 Unity IL2CPP 分别打包，按游戏实际后端选择安装。
- BepInEx 版本：面向 BepInEx 6 Bleeding Edge 构建，当前验证目标为 `6.0.0-be.755+3fab71a`。
- 默认目标语言：`zh-Hans`，可在控制面板实时改为其他语言。
- 默认快捷键：`Alt+H` 打开控制面板，`Alt+F` 暂停/恢复翻译，`Alt+G` 强制扫描，`Alt+D` 切换字体辅助。
- 请求策略：后台并发翻译、限速、批量字符上限、超时控制、失败切换和主线程分帧写回。
- 安全边界：API Key 写入本地 BepInEx `.cfg` 后由插件加密重写，状态接口不会返回明文；控制面板默认只在本机回环地址开放。

## 快速开始

1. 按游戏后端安装对应的 BepInEx 6 Bleeding Edge：
   - Unity Mono 游戏使用 BepInEx Unity Mono。
   - Unity IL2CPP 游戏使用 BepInEx Unity IL2CPP。
2. 从源码打包，或使用你已经构建好的 zip。
3. 解压对应插件包，把 `BepInEx/plugins/HUnityAutoTranslator` 放到游戏根目录下的同名位置。
4. 启动游戏，查看 BepInEx 日志中的控制面板地址，通常是 `http://127.0.0.1:48110/`。
5. 在浏览器打开控制面板，配置服务商、模型、Base URL、Endpoint 和 API Key。
6. 回到游戏等待文本被捕获。首次出现时可能先显示原文，翻译完成后会写回译文；后续命中缓存会直接显示译文。

## AI 服务

| 服务类型 | 默认接口 | 适用场景 |
| --- | --- | --- |
| OpenAI | `https://api.openai.com` + `/v1/responses` | 通用质量优先，默认模型为 `gpt-5.5`。 |
| DeepSeek | `https://api.deepseek.com` + `/chat/completions` | 成本、速度和中文能力均衡。 |
| OpenAI 兼容接口 | 用户自定义 Base URL / Endpoint | 第三方网关、自建代理或兼容服务。 |
| llama.cpp | 本机 `llama-server.exe` | 离线/本地模型翻译，不需要 API Key。 |

服务商可以在控制面板中按配置文件管理，并可设置优先级和失败切换。贴图文字翻译使用独立的图片服务商配置，方便把文本翻译和图片编辑分开管理。

## 构建

### 环境要求

- Windows PowerShell
- .NET SDK `10.0.202` 或兼容的 `latestFeature` 版本
- Node.js 与 npm，用于构建 Vue 控制面板
- 已能访问 `https://api.nuget.org/v3/index.json` 与 `https://nuget.bepinex.dev/v3/index.json`

### 打包全部产物

```powershell
.\build\package.ps1
```

默认会生成 Mono 与 IL2CPP 两个基础插件包，并额外生成两个可选 llama.cpp 后端包。

### 只打包基础插件

```powershell
.\build\package-plugin.ps1
```

它会调用 `package.ps1 -LlamaCppVariant None`，只生成基础插件目录和 zip，不重新打包 llama.cpp 后端。

### 只打包某个 Unity 后端

```powershell
.\build\package-plugin.ps1 -Runtime BepInEx5
.\build\package-plugin.ps1 -Runtime Mono
.\build\package-plugin.ps1 -Runtime IL2CPP
```

### 只打包某个 llama.cpp 后端

```powershell
.\build\package.ps1 -LlamaCppVariant Cuda13
.\build\package.ps1 -LlamaCppVariant Vulkan
```

### 只刷新嵌入式控制面板

```powershell
.\build\package.ps1 -GeneratePanelOnly
```

## 打包输出

| 路径 | 内容 |
| --- | --- |
| `build/HUnityAutoTranslator-bepinex5/BepInEx/plugins/HUnityAutoTranslator` | BepInEx 5 Unity Mono 插件目录。 |
| `build/HUnityAutoTranslator-0.1.0-bepinex5.zip` | BepInEx 5 Unity Mono 插件包。 |
| `build/HUnityAutoTranslator/BepInEx/plugins/HUnityAutoTranslator` | Unity Mono 插件目录。 |
| `build/HUnityAutoTranslator-0.1.0.zip` | Unity Mono 插件包。 |
| `build/HUnityAutoTranslator-il2cpp/BepInEx/plugins/HUnityAutoTranslator` | Unity IL2CPP 插件目录。 |
| `build/HUnityAutoTranslator-0.1.0-il2cpp.zip` | Unity IL2CPP 插件包。 |
| `build/HUnityAutoTranslator-0.1.0-llamacpp-cuda13.zip` | llama.cpp CUDA 13 后端包。 |
| `build/HUnityAutoTranslator-0.1.0-llamacpp-vulkan.zip` | llama.cpp Vulkan 后端包。 |

基础插件包默认不自带 llama.cpp。llama.cpp 后端包内已经包含 `BepInEx/plugins/HUnityAutoTranslator/llama.cpp/` 路径，直接在游戏根目录解压即可；`.gguf` 模型文件需要用户在控制面板中选择或下载。

## 控制面板

控制面板由 Vue 构建后嵌入插件 DLL，运行时由插件本地 HTTP 服务提供。默认只绑定 `127.0.0.1`，端口从 `48110` 开始，端口占用时会尝试后续端口。

主要页面：

- 运行状态：查看插件启停、队列、并发、缓存、最近翻译和当前服务商。
- 插件设置：调整扫描、写回、快捷键、字体辅助和基础运行行为。
- AI 设置：维护多个文本服务商、提示词、上下文、请求参数、本地模型和贴图图片服务商。
- 翻译缓存：像表格一样筛选、编辑、复制、粘贴、导入、导出已捕获文本。
- 术语库：维护固定译名，手动术语优先于 AI 自动提取。
- 贴图：扫描、筛选、导出、导入、对比和翻译含文字贴图。

## 贴图翻译

贴图工作流分为四步：

1. 在控制面板扫描当前场景贴图。
2. 导出源图，或在面板内筛选疑似含文字贴图。
3. 手动导入覆盖图，或使用图片模型生成翻译后的贴图。
4. 插件在运行时按贴图哈希应用覆盖，并保留源图与覆盖图的可检查记录。

贴图覆盖文件保存在 BepInEx 配置目录下，不会写入游戏原始资源包。

## 本地 llama.cpp 模型

llama.cpp 是可选能力：

- 基础插件包不会携带 llama.cpp 后端。
- CUDA 13 和 Vulkan 后端包只包含运行后端，不包含模型。
- 控制面板可以选择本地 `.gguf` 文件，或使用内置 ModelScope 预设下载。
- 插件只启动自己创建的 `llama-server.exe` 进程，并使用本机地址通信。

## 项目结构

```text
src/
  HUnityAutoTranslator.Core/          核心配置、缓存、队列、提示词、服务商、术语与贴图逻辑
  HUnityAutoTranslator.Plugin/        BepInEx 插件、Unity 捕获、写回、本地 HTTP 服务
  HUnityAutoTranslator.ControlPanel/  Vue 控制面板源码
tests/
  HUnityAutoTranslator.Core.Tests/    核心逻辑、打包脚本和源代码契约测试
build/
  package.ps1                         打包与控制面板嵌入入口
docs/
  manual-validation.md                手动验证清单
```

## 验证

```powershell
dotnet test
```

```powershell
Push-Location src\HUnityAutoTranslator.ControlPanel
npm run verify
Pop-Location
```

```powershell
.\build\package.ps1 -LlamaCppVariant None -SkipNpmInstall
```

更完整的游戏内验证流程见 [docs/manual-validation.md](docs/manual-validation.md)。

## 注意事项

- Mono 包和 IL2CPP 包不要同时放进同一个游戏；按游戏实际后端选择一个。
- API Key、翻译缓存、术语库和贴图覆盖都保存在本地 BepInEx 配置目录中，请自行备份重要数据。
- 首次捕获文本时显示原文是正常行为，译文写回依赖 AI 服务响应和主线程写回节奏。
- 如果游戏或其他插件持续覆盖 UI 文本，可能需要调整扫描间隔、写回策略或关闭冲突模块。
- 贴图翻译涉及图片模型能力和成本，建议先用少量贴图验证效果。

## 许可证

本项目使用 [MIT License](LICENSE)。
