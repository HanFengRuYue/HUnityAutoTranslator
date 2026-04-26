# HUnityAutoTranslator

基于 BepInEx 6 Bleeding Edge Unity Mono 的游戏内文本自动翻译插件。首版支持 UGUI、IMGUI、TextMeshPro，使用 OpenAI 原生 Responses、DeepSeek 或 OpenAI 兼容接口进行 AI 翻译。

## 当前能力

- Unity 后端：Mono。
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

打包结果：

- `build\HUnityAutoTranslator\BepInEx\plugins\HUnityAutoTranslator`
- `build\HUnityAutoTranslator-0.1.0.zip`

把 `BepInEx\plugins\HUnityAutoTranslator` 复制到游戏目录下同名位置即可。

## 使用

1. 游戏需要先安装 BepInEx 6 Bleeding Edge Unity Mono。
2. 启动游戏后查看 BepInEx 日志，找到 `Control panel: http://127.0.0.1:48110/` 这样的地址。
3. 在浏览器打开控制面板。
4. 选择服务商，填写 Base URL、Endpoint、模型和 API Key。
5. 目标语言可随时改，例如 `zh-Hans`、`en`、`ja`、`ko`。

常用默认值：

- OpenAI：`https://api.openai.com` + `/v1/responses` + `gpt-5.5`
- DeepSeek：`https://api.deepseek.com` + `/chat/completions` + `deepseek-v4-flash`
- OpenAI 兼容：通常是你的服务地址 + `/v1/chat/completions`

## 注意

- API Key 只保存在当前进程内存，不会从状态接口返回，也不会写入仓库文件。
- 首次看到文本时会先显示原文，翻译完成后立即回写；之后命中缓存会直接显示译文。
- 如果游戏文本控件被其他插件或游戏逻辑持续覆盖，可能需要调低扫描间隔或禁用冲突模块。
