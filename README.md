# HUnityAutoTranslator

基于 BepInEx 6 Bleeding Edge 的游戏内文本自动翻译插件。支持 Unity Mono 与 Unity IL2CPP 两种后端，首版覆盖 UGUI、IMGUI、TextMeshPro，使用 OpenAI 原生 Responses、DeepSeek、OpenAI 兼容接口，或可选的 llama.cpp 本地模型进行 AI 翻译。

## 当前能力

- Unity 后端：Mono、IL2CPP。
- BepInEx 版本：按最新 Bleeding Edge 构建适配，当前验证目标为 `6.0.0-be.755+3fab71a`。
- 文本组件：UGUI `UnityEngine.UI.Text`、TextMeshPro `TMP_Text`、IMGUI 常见字符串控件。
- 源语言：由提示词要求模型自动判断源语言。
- 目标语言：默认 `zh-Hans`，可在网页面板实时切换。
- 控制面板：默认只监听 `127.0.0.1:48110`，端口占用时自动尝试后续端口。
- 性能策略：后台多并发翻译、请求限速、缓存去重、主线程分帧回写。
- 兼容策略：UGUI/TMP 通过反射可选检测，未找到组件时自动跳过；IMGUI 使用 Harmony patch，失败时禁用该模块。

## 构建与打包

```powershell
.\build\package.ps1
```

默认会生成 Mono 与 IL2CPP 两个基础插件包，并额外生成两个只包含 llama.cpp 后端安装路径的可选本地后端包。只想生成插件包时：

```powershell
.\build\package.ps1 -LlamaCppVariant None
```

只构建某一种 Unity 后端时：

```powershell
.\build\package.ps1 -Runtime Mono -LlamaCppVariant None
.\build\package.ps1 -Runtime IL2CPP -LlamaCppVariant None
```

只想生成其中一个 llama.cpp 后端包时：

```powershell
.\build\package.ps1 -LlamaCppVariant Cuda13
.\build\package.ps1 -LlamaCppVariant Vulkan
```

打包结果：

- `build\HUnityAutoTranslator\BepInEx\plugins\HUnityAutoTranslator`
- `build\HUnityAutoTranslator-0.1.0.zip`：Unity Mono 插件包。
- `build\HUnityAutoTranslator-il2cpp\BepInEx\plugins\HUnityAutoTranslator`
- `build\HUnityAutoTranslator-0.1.0-il2cpp.zip`：Unity IL2CPP 插件包。
- `build\HUnityAutoTranslator-0.1.0-llamacpp-cuda13.zip`
- `build\HUnityAutoTranslator-0.1.0-llamacpp-vulkan.zip`

基础插件包默认不自带 llama.cpp。llama.cpp 后端包只包含固定 release 的运行后端，不包含 `.gguf` 模型文件；后端包内已经套好 `BepInEx\plugins\HUnityAutoTranslator\llama.cpp\` 路径，直接在游戏根目录解压即可。模型需要在控制面板中选择，并由用户点击启动后才会加载。

## 使用

1. 按游戏后端安装对应的 BepInEx 6 Bleeding Edge：Mono 游戏使用 Unity Mono 包，IL2CPP 游戏使用 Unity IL2CPP 包。
2. 解压对应插件包，把 `BepInEx\plugins\HUnityAutoTranslator` 放到游戏根目录下同名位置。
3. 启动游戏后查看 BepInEx 日志，找到类似 `http://127.0.0.1:48110/` 的控制面板地址。
4. 在浏览器打开控制面板。
5. 选择服务商，填写 Base URL、Endpoint、模型和 API Key；如果选择 llama.cpp 本地模型，则选择 `.gguf` 文件并手动启动本地模型。
6. 目标语言可随时改，例如 `zh-Hans`、`en`、`ja`、`ko`。

## 注意

- Mono 包和 IL2CPP 包不要同时放进同一个游戏；按游戏实际后端选择一个。
- API Key 会写入 BepInEx `.cfg` 后由插件加密重写，状态接口不会返回明文。
- llama.cpp 不需要 API Key；插件只会启动和停止自己创建的 `llama-server.exe` 进程，不会监听外网地址。
- 首次看到文本时会先显示原文，翻译完成后立即回写；之后命中缓存会直接显示译文。
- 如果游戏文本控件被其他插件或游戏逻辑持续覆盖，可能需要调低扫描间隔或禁用冲突模块。
