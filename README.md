<p align="center">
  <img src="src/HUnityAutoTranslator.ControlPanel/public/branding/hunity-icon-blue-white-128.png" width="96" height="96" alt="HUnityAutoTranslator logo">
</p>

<h1 align="center">HUnityAutoTranslator</h1>

<p align="center">
  <strong>面向 Unity / BepInEx 5 与 BepInEx 6 的运行时文本、字体、贴图本地化插件。</strong>
</p>

<p align="center">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/License-MIT-2f6fed"></a>
  <img alt="BepInEx" src="https://img.shields.io/badge/BepInEx-5.4%20%7C%206%20Bleeding%20Edge-5b6ee1">
  <img alt="Unity runtime" src="https://img.shields.io/badge/Unity-Mono%20%7C%20IL2CPP-222222">
  <img alt=".NET SDK" src="https://img.shields.io/badge/.NET%20SDK-10.0.202-512bd4">
  <img alt="Control panel" src="https://img.shields.io/badge/Control%20Panel-Vue%203-42b883">
</p>

HUnityAutoTranslator 是一个给 Unity 游戏使用的运行时自动本地化插件。它通过 BepInEx 加载，在游戏运行时捕获 UGUI、TextMeshPro 和 IMGUI 文本，交给在线 AI 服务或本机 llama.cpp 模型翻译，再把译文写回游戏界面。插件还提供本机中文控制面板，用来管理服务商、提示词、术语库、SQLite 缓存、字体替换、贴图替换和贴图文字翻译。

## 核心能力

| 能力 | 说明 |
| --- | --- |
| 运行时文本捕获 | 支持 UGUI `Text`、TextMeshPro `TMP_Text`、常见 IMGUI 字符串，并同时使用即时变更捕获和周期扫描兜底。 |
| AI 服务商配置 | 支持 OpenAI Responses、DeepSeek、OpenAI 兼容接口和 llama.cpp，本地加密保存多套配置，按优先级失败切换。 |
| 中文控制面板 | 默认只监听 `127.0.0.1:48110`，可查看状态、运行自检、调整设置、编辑缓存、管理术语和贴图。 |
| 可审计缓存 | 译文保存到本地 SQLite，保留原文、场景、组件层级、组件类型、模型和提示词策略信息。 |
| 上下文与术语 | 提示词可注入同场景、同组件、同父级文本和历史样例；手动术语优先于 AI 自动抽取。 |
| 质量修复 | 内置译文校验和二次修复，尽量避免 UI 标记丢失、版本号误翻、短设置值混淆和英文 UI 漏翻。 |
| 字体替换 | 支持 UGUI、TMP、IMGUI 的中文字体兜底，支持组件级替换字体、Windows 字体选择器和译文字号调整。 |
| 贴图本地化 | 扫描贴图引用、导出源图、导入覆盖图、对比源图/覆盖图，并可用图片模型识别和翻译贴图文字。 |
| 本地模型流程 | 可选打包 llama.cpp CUDA 13 / Vulkan 后端，控制面板支持选择或下载 `.gguf` 模型、启动/停止服务和运行基准测试。 |

## 支持的运行环境

- BepInEx 5 / Unity Mono：使用 `HUnityAutoTranslator-0.1.1-bepinex5.zip`。插件本身按 `.NET Framework 4.6.2` 构建，Unity 2018.4 LTS 起的 Mono 游戏都能正确实例化 `MonoBehaviour`。**注意**：部分 Unity 2019.x 游戏在打包时会裁掉 `System.Net.Http.dll`（典型表现是 `Managed/` 下没有该文件），此时插件可以加载并打开控制面板，但需要 HTTP 请求的功能（在线服务商翻译、本地 llama.cpp 启动状态查询）会报 `MissingMethodException`。把游戏 `Managed/` 下补上一份匹配的 `System.Net.Http.dll`，或换用同 Unity 版本的官方完整运行时即可恢复。
- BepInEx 6 / Unity Mono：使用 `HUnityAutoTranslator-0.1.1.zip`。
- BepInEx 6 / Unity IL2CPP：使用 `HUnityAutoTranslator-0.1.1-il2cpp.zip`。打包脚本里已经把 BepInEx 6 Mono/IL2CPP pin 升到 [`builds.bepinex.dev`](https://builds.bepinex.dev/projects/bepinex_be) 上的 bleeding-edge 构建（当前 be.755），其中的 Cpp2IL 支持 IL2CPP 元数据 v31+，Unity 2022.3 LTS、Unity 2023 与 Unity 6 的 IL2CPP 游戏都能正常加载。需要继续升级时编辑 `build/lib/AssetCache.ps1` 的 BepInEx 6 pin 即可。
- llama.cpp 后端：按需要额外解压 CUDA 13 或 Vulkan 后端包。

不要把 Mono、IL2CPP、BepInEx5 三种插件包同时放进同一个游戏目录。先确认游戏使用的 BepInEx 和 Unity 后端，再选择对应包。

## 快速开始

1. 给游戏安装匹配的 BepInEx 运行环境。
2. 解压对应的 HUnityAutoTranslator 插件包到游戏根目录，使文件落在 `BepInEx/plugins/HUnityAutoTranslator/`。
3. 如果要使用本地模型，额外解压 `HUnityAutoTranslator-0.1.1-llamacpp-cuda13.zip` 或 `HUnityAutoTranslator-0.1.1-llamacpp-vulkan.zip` 到同一个游戏根目录。
4. 启动游戏，查看 BepInEx 日志中的控制面板地址，通常是 `http://127.0.0.1:48110/`。
5. 打开控制面板，添加或选择翻译服务商配置，填写模型、接口地址和 API Key。
6. 回到游戏触发 UI 文本。首次出现时可能先显示原文，译文完成后会写回；后续命中缓存会直接复用译文。

默认快捷键可以在控制面板里修改：

| 快捷键 | 行为 |
| --- | --- |
| `Alt+H` | 打开控制面板 |
| `Alt+F` | 切换显示译文 / 原文 |
| `Alt+G` | 强制全量扫描并重新应用已记住译文 |
| `Alt+D` | 临时切换字体替换 |

## 控制面板

控制面板由 Vue 构建并嵌入插件 DLL，运行时由插件本机 HTTP 服务提供。默认只绑定 loopback 地址，端口从配置的 `48110` 开始；如果端口被占用，会尝试后续端口。

主要页面：

| 页面 | 用途 |
| --- | --- |
| 运行状态 | 查看插件开关、队列、当前 AI 槽位、缓存数量、Token 估算、当前服务商、最近译文和本地自检。 |
| 插件设置 | 调整目标语言、游戏标题、扫描/写回节奏、快捷键、文本捕获类型、缓存策略和字体替换。 |
| AI 翻译设置 | 管理多服务商配置、优先级、失败切换、请求参数、提示词模板、质量检查、本地模型和贴图图片服务。 |
| 术语库 | 管理固定译名，支持搜索、排序、列筛选、复制粘贴、批量启停和可选 AI 自动术语抽取。 |
| 贴图替换 | 扫描贴图、导出/导入贴图包、查看目录、筛选文字状态、对比图片和生成翻译贴图。 |
| 文本编辑 | 像表格一样浏览和编辑翻译缓存，支持列显示、列宽、排序、分列筛选、CSV 导入导出和定位目标。 |
| 版本信息 | 查看插件版本、配置路径和本地数据位置。 |

## AI 服务商

文本翻译支持四类服务商：

| 类型 | 默认接口 | 说明 |
| --- | --- | --- |
| OpenAI | `https://api.openai.com` + `/v1/responses` | 默认模型 `gpt-5.5`，使用 Responses API。 |
| DeepSeek | `https://api.deepseek.com` + `/chat/completions` | 默认模型 `deepseek-v4-flash`，使用 Chat Completions 格式。 |
| OpenAI 兼容 | 自定义 Base URL / Endpoint | 适合代理网关、自建服务或其他兼容接口，支持自定义 Header 和额外 JSON Body。 |
| llama.cpp | 本机 `llama-server.exe` | 使用控制面板启动的本地模型服务，不需要 API Key。 |

服务商配置可以新增、导入、导出、禁用、排序和删除。每个配置可以单独设置并发、限速、超时、模型、推理强度、输出详细程度、温度等参数。在线服务商 API Key 会加密保存；状态接口只显示是否已配置和脱敏预览，不返回明文。

运行时会按启用配置的优先级选择可用服务商。某个配置连续失败后会短暂冷却，队列会继续尝试下一个可用配置，避免单个服务商异常拖死翻译池。

## 提示词、上下文和质量检查

- 内置提示词面向游戏 UI 本地化，会保留占位符、控制字符、Unity 富文本标签、TextMeshPro 标签和 UI 前后缀符号。
- 控制面板可以直接编辑完整提示词模板，并保留必须存在的占位符校验。
- 上下文可包含当前场景、组件层级、父级/同级 UI 文本和历史译文样例，用来改善短按钮和设置项翻译。
- 术语库约束会优先于上下文样例，手动术语优先于自动术语。
- 质量检查默认开启，会拒绝常见坏译文并按配置触发修复请求。
- 已经是目标语言、版本号、路径、邮箱、URL、文件名、技术标识等文本会尽量跳过或保留，减少误翻。

## 缓存和文本编辑

翻译缓存使用本地 SQLite 文件，路径为 `BepInEx/config/HUnityAutoTranslator/translation-cache.sqlite`。缓存记录包括：

- `source_text`、`translated_text`、`target_language`
- `scene_name`、`component_hierarchy`、`component_type`
- `replacement_font`
- `provider_kind`、`provider_base_url`、`provider_endpoint`、`provider_model`
- `prompt_policy_version`
- `created_utc`、`updated_utc`

控制面板的文本编辑页支持表格编辑、单元格复制粘贴、批量清空、服务端分列筛选、列宽保存、CSV 导入导出和对当前游戏目标的临时定位高亮。

## 字体替换

字体替换默认启用，用来处理译文写回后中文缺字或 TMP 字体显示异常的问题。

- UGUI：当原字体不能显示译文或某行指定了替换字体时，临时替换为可用中文字体。
- TMP：优先安装中文后备字体并保留原材质效果；必要时为组件创建可渲染中文的 TMP 字体资产。
- IMGUI：只在当前绘制项需要中文字体时临时切换，绘制完成后恢复。
- 控制面板可选择字体文件，插件会读取字体家族名并可复制到配置目录。
- 文本编辑页可给单个组件记录 `ReplacementFont`，用于局部修正特定控件的字体。

## 贴图替换和贴图文字翻译

贴图流程独立于文本翻译管线，适合处理 UI 图片、按钮图、图标文字和少量纹理文本。

支持扫描和替换的目标包括：

- `UnityEngine.UI.RawImage.texture`
- `UnityEngine.UI.Image.sprite`
- `SpriteRenderer.sprite`
- `Renderer.sharedMaterial.mainTexture`

基本流程：

1. 在控制面板扫描当前场景贴图，默认延迟超大贴图以降低卡顿风险。
2. 导出贴图包，或在面板内按场景、文字状态筛选贴图。
3. 手动编辑 PNG 后导入，或使用图片模型生成翻译后的覆盖图。
4. 插件按源图哈希在运行时应用覆盖图，源图和覆盖图记录都保存在本地配置目录。

贴图覆盖不会写入游戏原始资源包。持久化文件位于 `BepInEx/config/HUnityAutoTranslator/texture-overrides/`，贴图目录和文字检测结果位于 `BepInEx/config/HUnityAutoTranslator/texture-catalog/`。

贴图文字翻译使用独立的图片服务商配置，默认接口为：

| 用途 | 默认值 |
| --- | --- |
| 图片编辑 | `/v1/images/edits` + `gpt-image-2` |
| 视觉确认 | `/v1/responses` + `gpt-5.4-mini` |
| 质量 | `medium` |
| 并发 | `1` |


## 本地 llama.cpp

llama.cpp 是可选能力，不随基础插件包一起内置。项目可以额外打包 Windows CUDA 13.1 或 Vulkan 后端，解压后会放到 `BepInEx/plugins/HUnityAutoTranslator/llama.cpp/`。

控制面板支持：

- 选择已有 `.gguf` 文件。
- 从内置 ModelScope 预设下载模型并校验 SHA256。
- 启动、停止插件管理的 `llama-server.exe`。
- 设置上下文长度、GPU 层数、并行槽位、batch、ubatch 和 Flash Attention。
- 运行 CUDA 基准测试并给出参数建议。
- 记住上次手动启动状态，下次游戏启动且服务商仍为 llama.cpp 时自动启动本地模型。

内置模型预设包含英翻中和日翻中方向的 Qwen / Sakura GGUF 文件。模型文件不会打进插件包，需要用户在控制面板下载或自行准备。

## 本地文件

插件运行数据默认放在 BepInEx 配置目录：

| 路径 | 内容 |
| --- | --- |
| `BepInEx/config/com.hanfeng.hunityautotranslator.cfg` | 主要运行设置，带中文注释，可手动编辑。 |
| `BepInEx/config/HUnityAutoTranslator/translation-cache.sqlite` | 文本翻译缓存。 |
| `BepInEx/config/HUnityAutoTranslator/translation-glossary.sqlite` | 术语库。 |
| `BepInEx/config/HUnityAutoTranslator/providers/` | 加密后的文本服务商配置。 |
| `BepInEx/config/HUnityAutoTranslator/texture-image-providers/` | 加密后的贴图图片服务商配置。 |
| `BepInEx/config/HUnityAutoTranslator/texture-overrides/` | 持久化贴图覆盖 PNG 和索引。 |
| `BepInEx/config/HUnityAutoTranslator/texture-catalog/` | 贴图目录、源图快照和文字检测记录。 |
| `BepInEx/config/HUnityAutoTranslator/self-check-last.json` | 最近一次本地自检报告。 |

API Key 可以写入 `.cfg` 方便首次配置，但插件加载后会加密并重写，避免明文长期保留在主配置文件里。

## 构建

### 环境要求

- Windows PowerShell
- .NET SDK `10.0.202`，或符合 `latestFeature` 回滚策略的兼容版本
- Node.js 与 npm
- 可访问 NuGet 源：`https://api.nuget.org/v3/index.json`、`https://nuget.bepinex.dev/v3/index.json`、`https://nuget.samboy.dev/v3/index.json`

### 打包全部产物

```powershell
.\build\package.ps1
```

默认会构建 BepInEx5、BepInEx6 Mono、BepInEx6 IL2CPP 三个插件包，并额外生成 CUDA 13 与 Vulkan 两个 llama.cpp 后端包。

### 只打包插件

```powershell
.\build\package-plugin.ps1
```

等价于调用 `package.ps1 -LlamaCppVariant None`，不会下载或打包 llama.cpp 后端。

### 只打包某个 Unity / BepInEx 运行时

```powershell
.\build\package-plugin.ps1 -Runtime BepInEx5
.\build\package-plugin.ps1 -Runtime Mono
.\build\package-plugin.ps1 -Runtime IL2CPP
```

### 指定某个 llama.cpp 后端

```powershell
.\build\package.ps1 -Runtime Mono -LlamaCppVariant Cuda13
.\build\package.ps1 -Runtime Mono -LlamaCppVariant Vulkan
```

`package.ps1` 会同时按 `-Runtime` 构建对应插件包；`-LlamaCppVariant` 只控制额外生成哪个 llama.cpp 后端包。

### 只刷新嵌入式控制面板

```powershell
.\build\package.ps1 -GeneratePanelOnly
```

控制面板源码位于 `src/HUnityAutoTranslator.ControlPanel/`。构建时会把 Vue 产物内联到 `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`，插件运行时不依赖远程前端资源。

## 打包输出

| 路径 | 内容 |
| --- | --- |
| `build/HUnityAutoTranslator-bepinex5/BepInEx/plugins/HUnityAutoTranslator` | BepInEx 5 Unity Mono 插件目录。 |
| `build/HUnityAutoTranslator-0.1.1-bepinex5.zip` | BepInEx 5 Unity Mono 插件包。 |
| `build/HUnityAutoTranslator/BepInEx/plugins/HUnityAutoTranslator` | BepInEx 6 Unity Mono 插件目录。 |
| `build/HUnityAutoTranslator-0.1.1.zip` | BepInEx 6 Unity Mono 插件包。 |
| `build/HUnityAutoTranslator-il2cpp/BepInEx/plugins/HUnityAutoTranslator` | BepInEx 6 Unity IL2CPP 插件目录。 |
| `build/HUnityAutoTranslator-0.1.1-il2cpp.zip` | BepInEx 6 Unity IL2CPP 插件包。 |
| `build/HUnityAutoTranslator-llamacpp-cuda13/BepInEx/plugins/HUnityAutoTranslator/llama.cpp` | llama.cpp CUDA 13.1 后端目录。 |
| `build/HUnityAutoTranslator-0.1.1-llamacpp-cuda13.zip` | llama.cpp CUDA 13.1 后端包。 |
| `build/HUnityAutoTranslator-llamacpp-vulkan/BepInEx/plugins/HUnityAutoTranslator/llama.cpp` | llama.cpp Vulkan 后端目录。 |
| `build/HUnityAutoTranslator-0.1.1-llamacpp-vulkan.zip` | llama.cpp Vulkan 后端包。 |

基础插件包不包含 llama.cpp 后端，也不包含 `.gguf` 模型文件。llama.cpp 后端包只包含运行程序和依赖库，模型需要通过控制面板下载或手动选择。

## 项目结构

```text
src/
  HUnityAutoTranslator.Core/          配置、缓存、队列、服务商、提示词、术语、质量检查、贴图和控制面板状态
  HUnityAutoTranslator.Plugin/        BepInEx 插件、Unity 捕获与写回、本机 HTTP 服务、字体和贴图运行时
  HUnityAutoTranslator.Plugin.BepInEx5/ BepInEx 5 Unity Mono 兼容打包项目
  HUnityAutoTranslator.ControlPanel/  Vue 控制面板源码
tests/
  HUnityAutoTranslator.Core.Tests/    核心逻辑、运行时契约、打包脚本和控制面板源断言测试
build/
  package.ps1                         主打包脚本
  package-plugin.ps1                  只打包插件的便捷入口
```

## 验证

后端测试：

```powershell
dotnet test
```

控制面板类型检查和构建：

```powershell
Push-Location src\HUnityAutoTranslator.ControlPanel
npm run verify
Pop-Location
```

基础插件打包：

```powershell
.\build\package.ps1 -LlamaCppVariant None -SkipNpmInstall
```

真实游戏验证建议至少检查：

- BepInEx 日志中插件加载成功，控制面板地址已输出。
- 控制面板能刷新状态并运行本地自检。
- 目标场景中的 UGUI / TMP / IMGUI 文本能被捕获并写回。
- SQLite 缓存中能看到真实原文、译文和上下文。
- 字体替换没有让原 UI 样式大面积劣化。
- 贴图导出/导入只影响本地覆盖目录，不改游戏原始资源。

## 注意事项

- 这是运行时插件，不会替换游戏原始资源包；文本、术语、服务商和贴图覆盖都保存在本地配置目录。
- 控制面板默认只对本机开放，不建议改成局域网可访问，除非你明确知道风险。
- 在线 AI 服务的质量、速度和成本取决于服务商；贴图图片翻译尤其建议先用少量贴图验证。
- 某些游戏或其他插件会持续重写 UI 文本，可能需要调整扫描间隔、写回策略或临时关闭冲突模块。
- IL2CPP、旧版 Unity、裁剪过的 TextMeshPro 或特殊 UI 框架可能需要额外兼容处理，请以 BepInEx 日志和控制面板自检结果为准。

## 许可证

本项目使用 [MIT License](LICENSE)。
