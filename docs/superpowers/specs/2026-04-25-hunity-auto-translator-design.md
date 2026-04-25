# HUnityAutoTranslator 设计规格

日期：2026-04-25  
状态：已与用户确认，进入实现计划前审阅  
目标：制作一个基于 BepInEx 6 Bleeding Edge、面向 Unity Mono 游戏的自动文本翻译插件。

## 1. 背景与目标

HUnityAutoTranslator 是一个 BepInEx 插件，用于在 Unity Mono 游戏运行时自动捕获游戏内文本，并通过 AI 大模型翻译后写回 UI。首版重点是兼容性、性能稳定性和可实时控制，而不是覆盖所有 Unity 渲染路径。

首版必须支持：

- Unity 后端：Mono。
- 文本组件：UGUI、IMGUI、TextMeshPro。
- 翻译服务：OpenAI 原生、DeepSeek 原生、OpenAI 兼容接口。
- 控制方式：本机 HTTP 网页控制面板，运行时修改配置。
- 性能要求：不能阻塞 Unity 主线程，不能因为网络请求拖慢游戏。
- 兼容性要求：组件或服务失败时局部降级，游戏继续运行并显示原文。

首版不支持：

- Unity IL2CPP。
- OCR、语音翻译、图片翻译。
- 修改游戏资源文件。
- 将控制面板暴露到局域网。

参考资料：

- BepInEx Bleeding Edge 构建页在 2026-04-25 显示最新 BE 构建为 `6.0.0-be.755+3fab71a`，包含 Unity Mono win/linux/macOS 包：https://builds.bepinex.dev/projects/bepinex_be
- BepInEx 文档说明 Unity Mono 插件入口为 `BepInEx.Unity.Mono.BaseUnityPlugin`：https://docs.bepinex.dev/master/api/BepInEx.Unity.Mono.BaseUnityPlugin.html
- OpenAI 文本生成文档推荐新项目优先使用 Responses API：https://developers.openai.com/api/docs/guides/text
- DeepSeek 2026-04-24 更新日志说明 V4-Pro 与 V4-Flash 可通过 OpenAI Chat Completions 接口使用：https://api-docs.deepseek.com/updates

## 2. 总体架构

插件采用“兼容性优先混合方案”：

- UGUI 与 TextMeshPro 使用低频增量扫描捕获文本。
- IMGUI 使用 Harmony patch 拦截常见绘制入口。
- 捕获到的新文本进入纯 C# 文本处理管线。
- 管线先做规范化、去重、缓存查找，再把未命中文本加入后台翻译队列。
- Unity 主线程只负责捕获、缓存命中应用、翻译结果写回。
- 网络请求、限流、批处理、重试、磁盘缓存写入都在后台执行。
- HTTP 控制面板通过插件内置轻量 HTTP 服务提供。

核心模块：

- `Plugin`：BepInEx 入口，负责生命周期、配置绑定、模块启动与关闭。
- `RuntimeConfig`：运行时配置快照，支持从 BepInEx 配置和 HTTP 面板热更新。
- `TextCapture`：UGUI/TMP 扫描器与 IMGUI hook 的协调层。
- `TextPipeline`：规范化、过滤、去重、缓存查询、队列提交。
- `TranslationQueue`：后台批处理、限流、重试和结果回传。
- `TranslationProviders`：OpenAI、DeepSeek、OpenAI 兼容接口适配。
- `TranslationCache`：内存缓存与磁盘缓存。
- `HttpControlPanel`：本机控制面板与 JSON API。
- `SafetyGuards`：占位符保护、标签保护、输出校验、日志限频。

## 3. 文本捕获与替换

### 3.1 UGUI

UGUI 目标类型为 `UnityEngine.UI.Text`。

策略：

- 低频扫描当前场景中可用的 UGUI Text 实例。
- 默认扫描间隔为 0.5 到 1 秒，配置可调。
- 每次扫描有处理预算，避免单帧遍历过多对象。
- 发现文本变化后提交给 `TextPipeline`。
- 缓存命中或翻译完成后在 Unity 主线程写回 `text` 属性。

防重复：

- 每个组件实例记录最后观察到的原文、最后写入的译文、最后目标语言。
- 如果当前文本等于插件刚写入的译文，跳过捕获。
- 如果游戏逻辑后续写入新原文，再重新捕获。

### 3.2 TextMeshPro

TMP 目标类型为 `TMP_Text` 及其常见派生类型，例如 `TextMeshProUGUI`。

策略：

- 不强依赖 TMP 编译期程序集。
- 运行时通过反射检测 TMP 类型是否存在。
- TMP 存在时启用扫描，缺失时静默禁用 TMP 模块。
- 通过反射读取和写回 `text` 属性。
- 反射失败或版本差异导致异常时，只禁用 TMP 捕获，不影响 UGUI/IMGUI。

### 3.3 IMGUI

IMGUI 目标为常见 `UnityEngine.GUI` 与 `UnityEngine.GUILayout` 绘制入口。

首版 patch 范围：

- `GUI.Label`
- `GUI.Button`
- `GUI.Toggle`
- `GUI.TextField`
- `GUILayout.Label`
- `GUILayout.Button`
- `GUILayout.Toggle`
- `GUILayout.TextField`

策略：

- Harmony prefix 在绘制前检查字符串参数。
- 若缓存已有译文，直接替换参数。
- 若无缓存，提交原文到后台队列并继续显示原文。
- OnGUI 路径绝不等待网络请求。
- 某个方法 patch 失败时记录日志并跳过该方法，不影响其他 patch。

## 4. 翻译管线与缓存

### 4.1 文本键

缓存键包含：

- 原文规范化结果。
- 目标语言。
- 翻译服务商。
- 模型名。
- 提示词策略版本。

这样切换目标语言、模型或提示词后，不会误用旧缓存。

### 4.2 队列策略

新文本处理顺序：

1. 跳过空白、纯数字、纯符号、过短控制文本。
2. 保护占位符、控制符、富文本标签。
3. 查询内存缓存。
4. 查询磁盘缓存。
5. 未命中则进入翻译队列。

后台 worker 策略：

- 短文本可批量翻译。
- 长文本单独翻译。
- 并发数、每分钟请求数、批量最大字符数可配置。
- 队列过长时合并重复项。
- 队列满时优先保留当前可见、较新的、非重复文本。
- 翻译失败后指数退避重试。
- 最终失败则保留原文并记录错误。

### 4.3 磁盘缓存

磁盘缓存保存在 BepInEx 插件数据目录下，按目标语言和 provider profile 分组。写入由后台线程完成，避免主线程 IO。

缓存管理：

- 控制面板显示缓存数量和命中率。
- 支持清空当前语言缓存。
- 支持清空全部缓存。
- 支持导出缓存文件用于后续人工校对，首版只保证基础 JSON/文本格式，不做复杂编辑器。

## 5. 翻译服务

### 5.1 OpenAI 原生

OpenAI 原生 provider 使用 Responses API。

默认配置：

- Base URL：`https://api.openai.com`
- Endpoint：`/v1/responses`
- 默认模型：`gpt-5.5`

解析策略：

- 优先读取 Responses 的聚合文本输出。
- 若返回结构包含多个输出项，按官方文档提醒，不假设文本一定在第一个输出项。
- 解析失败时记录原始错误摘要，不记录完整 API Key 或敏感请求头。

### 5.2 DeepSeek 原生

DeepSeek 原生 provider 使用 Chat Completions 形状。

默认配置：

- Base URL：`https://api.deepseek.com`
- Endpoint：`/chat/completions`
- 默认模型：`deepseek-v4-flash`
- 可选模型：`deepseek-v4-pro`

说明：

- DeepSeek 兼容 OpenAI Chat Completions 请求格式。
- 旧模型名 `deepseek-chat`、`deepseek-reasoner` 不作为首选默认值。

### 5.3 OpenAI 兼容

OpenAI 兼容 provider 面向 OpenRouter、本地网关、公司代理等服务。

配置：

- Base URL：用户填写。
- Endpoint 默认：`/v1/chat/completions`。
- 模型名：用户填写。
- API Key：用户填写。

兼容策略：

- 默认使用 Chat Completions 请求体。
- 只依赖通用 `messages`、`model`、`temperature`、`max_tokens` 字段。
- 不默认启用 provider 专有参数。

## 6. 提示词与输出校验

提示词采用“硬规则 + 软风格”的分层策略。

硬规则：

- 只输出译文。
- 不解释。
- 不寒暄。
- 不添加引号或 Markdown。
- 不添加“翻译如下”等前缀。
- 不改变占位符，例如 `{0}`、`{1}`、`%s`、`%d`、`{playerName}`。
- 不改变控制符，例如 `\n`、`\t`、`\\n`。
- 不破坏 Unity 富文本标签和 TMP 标签。
- 批量翻译必须返回 JSON 数组，数量与输入一致。

软风格：

- 根据游戏语境自然本地化。
- 菜单和按钮短而清楚。
- 对话文本符合角色口吻。
- 技能、道具、状态名可在不破坏一致性的前提下意译。
- 避免机器翻译腔。
- 不要求逐字死译。

控制面板提供翻译风格配置：

- 忠实。
- 自然。
- 本地化。
- UI 简洁。
- 自定义附加风格提示。

硬规则始终不可关闭，风格只影响表达方式。

输出校验：

- 单条翻译不能包含解释性前缀。
- 批量翻译数量必须匹配输入数量。
- 占位符集合必须一致。
- 富文本/TMP 标签必须完整。
- 返回空文本视为失败。
- 校验失败时使用更严格的修复提示词重试一次。
- 修复仍失败则保留原文并记录错误。

## 7. HTTP 控制面板

控制面板由插件内置轻量 HTTP 服务提供，默认只监听：

- Host：`127.0.0.1`
- Port：可配置，默认使用固定端口加冲突自动递增策略。

安全策略：

- 默认不绑定 `0.0.0.0`。
- API Key 页面只显示“已设置/未设置”。
- API Key 不在页面明文回显。
- 日志不输出 API Key。
- 支持本地访问令牌，减少其他本机进程误操作。
- 静态页面不依赖外部 CDN。

页面功能：

- 状态总览：启用状态、队列数量、缓存命中率、最近错误。
- 翻译设置：目标语言、翻译风格、扫描间隔、模块开关。
- 服务商配置：OpenAI、DeepSeek、OpenAI 兼容。
- 缓存管理：清空当前语言缓存、清空全部缓存、查看缓存统计。
- 日志与错误：最近错误、provider 连接测试、失败计数。

实时行为：

- 修改目标语言后，新捕获文本立即使用新语言。
- 旧缓存按语言隔离，不误用。
- 修改 provider 或模型后，新请求使用新配置。
- 运行中可暂停/恢复翻译。
- 可单独启用/禁用 UGUI、TMP、IMGUI 捕获模块。

## 8. 性能与兼容性

性能保护：

- Unity 主线程不执行网络请求。
- Unity 主线程不执行磁盘缓存写入。
- UGUI/TMP 扫描低频运行。
- 每轮扫描有对象数量预算。
- 每帧写回有数量预算。
- 后台翻译有并发限制。
- 日志限频，避免刷屏。
- 缓存命中立即返回，不进入网络队列。

兼容性保护：

- 不依赖目标游戏私有类型。
- TMP 缺失时自动禁用 TMP 模块。
- Harmony patch 局部失败不阻止插件启动。
- 每个捕获模块可单独关闭。
- Provider 失败不影响文本捕获。
- 所有运行时异常都尽量局部捕获，记录日志后回退原文。

默认原则：

- 宁可漏翻一部分文本，也不让游戏明显掉帧。
- 宁可保留原文，也不把格式破坏的译文写回游戏。
- 宁可某个模块禁用，也不因为一个模块失败影响整个插件。

## 9. 测试策略

自动化测试优先覆盖纯 C# 核心逻辑：

- 文本规范化。
- 占位符提取与还原。
- 富文本/TMP 标签校验。
- 缓存 key 生成。
- 队列去重。
- 批量翻译请求构造。
- OpenAI Responses 返回解析。
- Chat Completions 返回解析。
- 提示词输出校验。
- 配置热更新。

Unity 相关部分通过接口隔离，尽量把扫描决策和写回策略放到可测试类中。真实 Unity 行为用手动验证清单覆盖：

- UGUI 静态文本。
- UGUI 动态变化文本。
- TMP 静态文本。
- TMP 动态变化文本。
- IMGUI Label/Button/Toggle/TextField。
- 切换目标语言。
- 暂停/恢复翻译。
- Provider 失败回退原文。
- 缓存命中立即显示译文。
- API Key 不在控制面板明文显示。

## 10. 验收标准

首版完成后应满足：

- 能构建出 BepInEx 6 Unity Mono 插件 DLL。
- 插件能在 Mono Unity + BepInEx BE 环境启动。
- 控制面板可在本机浏览器打开。
- 控制面板能实时修改目标语言、provider、模型、模块开关。
- UGUI、TMP、IMGUI 至少各有一条验证路径。
- 翻译请求不会阻塞 UI。
- 缓存命中后可快速显示译文。
- AI 返回非译文或破坏占位符时不会写回错误文本。
- Provider 失败时游戏继续显示原文。
- 自动化测试覆盖核心翻译管线。

## 11. 已确认决策

- 选择方案 1：兼容性优先混合方案。
- UGUI/TMP 使用低频扫描。
- IMGUI 使用轻量 Harmony patch。
- 源语言自动检测，不要求用户手动指定源语言。
- 默认目标语言为简体中文 `zh-Hans`，可在控制面板实时改成其他语言。
- 翻译采用后台异步模式：先显示原文，翻译完成后替换。
- HTTP 控制面板只监听本机 `127.0.0.1`。
- 提示词必须强约束输出格式，同时允许自然、本地化、不死板的表达。
