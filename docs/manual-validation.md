# 手动验证清单

## 环境

- Unity Mono 游戏或 Unity IL2CPP 游戏各一套，至少覆盖目标用户实际后端。
- BepInEx 6 Bleeding Edge：Mono 游戏安装 Unity Mono 包，IL2CPP 游戏安装 Unity IL2CPP 包。当前构建适配目标为 `6.0.0-be.755+3fab71a`。
- 插件文件位于 `BepInEx/plugins/HUnityAutoTranslator/`。
- BepInEx 5 Unity Mono 游戏使用 `HUnityAutoTranslator-0.1.0-bepinex5.zip`，BepInEx 6 Unity Mono 游戏使用 `HUnityAutoTranslator-0.1.0.zip`，IL2CPP 游戏使用 `HUnityAutoTranslator-0.1.0-il2cpp.zip`，不要混放多个插件 DLL。

## 启动验证

1. 启动游戏。
2. BepInEx 日志中应出现插件已加载和本机控制面板地址，例如 `http://127.0.0.1:48110/`。
3. 用浏览器打开面板，界面应为中文。
4. 在 IL2CPP 游戏中确认日志没有 `BepInEx.Unity.Mono`、`BaseUnityPlugin` 或插件 DLL 载入失败相关错误。

## 配置验证

1. 在面板中配置 OpenAI、DeepSeek 或 OpenAI 兼容服务。
2. 保存 API Key 后，状态应显示“已配置”，但页面和 `/api/state` 不应返回密钥原文。
3. 修改目标语言，例如从 `zh-Hans` 改为 `ja`，保存后新文本应进入新语言缓存。
4. 暂停“启用翻译”后，新捕获文本不应进入翻译队列。

## llama.cpp 本地模型验证

1. 安装基础插件包，再将 CUDA 13.1 或 Vulkan 后端包直接解压到游戏根目录。
2. 确认插件目录中存在 `llama.cpp/backend.json` 和 `llama.cpp/llama-server.exe`，并确认基础插件包本身没有自带 `llama.cpp/`。
3. 在 AI 设置页选择“llama.cpp 本地模型”，选择一个 `.gguf` 模型文件。
4. 点击“启动本地模型”，状态应显示本地模型运行中，端口应为随机分配的本机端口，并且“测试连接”成功。
5. 让一条游戏文本进入翻译流程，确认请求使用本地模型并写回译文。
6. 点击“停止本地模型”后，新任务应保持等待，并显示“本地模型未启动”一类的中文状态。

## 文本组件验证

1. UGUI：菜单、按钮、普通 UI 文本应被捕获并翻译。
2. TextMeshPro：TMP 对话或界面文本应被捕获并翻译。
3. IMGUI：使用 `GUI.Label`、`GUILayout.Button` 等字符串重载的控件应被翻译。
4. 禁用任一模块后，该模块不应继续捕获新文本。
5. 在 IL2CPP 游戏中重复 UGUI、TMP、IMGUI 验证，确认同一套控制面板、缓存、字体替换、热键和定位功能可用。

## 性能验证

1. 队列数应在遇到新文本时上升，并在翻译完成后下降。
2. 缓存数应随成功翻译增长。
3. 多个可见文本同时出现时，应并发请求，翻译完成后分帧回写到游戏内。
4. 调低“每次扫描目标数”和调高“扫描间隔”后，CPU 压力应下降。

## 本机监听验证

1. `http://127.0.0.1:端口/` 可访问。
2. 从其他机器访问同端口应失败。
3. 如果默认端口被占用，日志中应显示自动选择的后续端口。
