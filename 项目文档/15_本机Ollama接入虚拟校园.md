# 本机 Ollama 接入虚拟校园

## 当前接入状态

通知书 AI 问答现已直接接入本机 Ollama，不依赖 Dify 即可测试完整交互逻辑。

调用链路：

```text
Pico 4 麦克风
→ FunASR 语音识别
→ AdmissionLetterGuide
→ OllamaChatService
→ 本机 Ollama qwen3.5:9b
→ 通知书回答 UI / AI 动作事件
```

聊天服务优先级：

```text
本机 Ollama → 已配置的 Dify → Mock 模拟服务
```

## 已实现规则

- 自动附带当前 Unity 场景。
- 自动附带最近注视的建筑名称和测试说明。
- 自动附带用户选择的专业兴趣。
- 支持少量连续对话历史，默认保留最近 4 轮。
- 校园事实只允许依据传入的测试资料回答。
- 资料不足时回答“目前资料中无法确定”，禁止自行编造。
- 区分校园问题、普通问候和明显无关问题。
- 无关问题不直接展开回答，而是引导回校园、专业或参观内容。
- 当前确认的专业只有：
  - 数据科学与大数据技术
  - 人工智能
  - 机器人工程
  - 机电工程
- 请求期间启用忙碌锁，避免连续点击产生并发堆积。
- 模型服务异常时在通知书中显示错误，不会阻塞 VR 主线程。

## 当前默认配置

首次运行时会在 Unity 的 `Application.persistentDataPath` 下创建：

```text
ollama_config.json
```

默认内容：

```json
{
  "enabled": true,
  "base_url": "http://localhost:11434",
  "model": "qwen3.5:9b",
  "timeout_seconds": 120,
  "max_history_turns": 4
}
```

修改配置后需要重新进入 Play 模式。

## 测试前启动顺序

1. 启动 Ollama，确认任务栏存在 Ollama 图标。
2. 在 PowerShell 中执行 `ollama list`，确认存在 `qwen3.5:9b`。
3. 先执行一次 `ollama run qwen3.5:9b` 进行模型预热，可减少 VR 中第一次回答的等待时间。
4. 启动 FunASR 服务。
5. 打开 Unity 并启动 PDC 串流。
6. 进入 Play 模式。
7. 打开通知书的 AI 导览/问答页面，按现有录音交互提问。

建议测试问题：

```text
你好
请介绍我现在看着的建筑
学校有哪些专业
人工智能专业和机器人工程有什么不同
学校是哪一年建立的
帮我分析一下股票
```

预期结果：

- 问候可以自然回应。
- 建筑介绍只能引用当前建筑的测试说明。
- 能准确列出四个专业。
- 没有专业培养方案数据时，应明确资料不足，不编造具体课程。
- 建校时间没有录入时，应回答无法确定。
- 股票等无关问题应被引导回校园话题。

## PDC 与 Pico APK 的地址区别

当前 PDC 串流中 Unity 程序运行在电脑上，因此可以使用：

```text
http://localhost:11434
```

如果将来构建成 Pico 4 独立 APK，`localhost` 会变成头显自身，必须改为电脑局域网地址，例如：

```text
http://192.168.1.105:11434
```

届时还需要让 Ollama 监听局域网地址、允许 Windows 防火墙端口 `11434`，并保证电脑和头显连接同一网络。正式部署前再完成这一步，当前 PDC 测试无需修改。

## 相关代码

- `Assets/AdmissionLetter/OllamaChatService.cs`
- `Assets/AdmissionLetter/AdmissionLetterGuide.cs`
- `Assets/AdmissionLetter/CampusContextService.cs`
- `Assets/Avator/tmp/VoiceChatClient.cs`

## 后续接入 Dify

当前 UI、语音、上下文、忙碌状态和回答事件均通过统一的 `IDifyChatService` 接口工作。后续接入 Dify 时只需切换服务和配置 API，不需要重做通知书交互或 UI。
