# Mock AI 协议与动作白名单

## 1. 阶段目标

在暂不连接 Dify、Ollama和正式知识库的情况下，先完成全部 VR 交互。Mock、Dify 和本地 Ollama共用同一套输入输出协议，后期只替换 AI 服务，不重写 UI、导航、场景或 Avatar 逻辑。

当前阶段状态：**已完成并通过编译。**

## 2. 统一输出协议

AI 返回 `DifyActionPayload`，当前字段如下：

```json
{
  "speech": "给用户显示和播报的回答",
  "emotion": "neutral",
  "action": "none",
  "target_id": "",
  "page_id": "",
  "scene_index": -1,
  "comfort_mode": false,
  "exhibit_id": "",
  "suggestions": ["后续问题1", "后续问题2"]
}
```

## 3. Unity 动作白名单

`CampusAICommandExecutor` 只允许以下动作：

| action | 功能 | 二次校验 |
|---|---|---|
| `none` | 只回答，不改变世界 | 无 |
| `navigate` | 开始地点导航 | `target_id` 必须存在于地点注册表 |
| `open_page` | 打开通知书页面 | 页面必须为 main、guide、map、collection、tasks |
| `switch_scene` | 切换场景 | `scene_index` 只能是 0、1、2 |
| `set_comfort_mode` | 切换舒适移动 | 只修改平滑移动，保留瞬移和分段转向 |
| `start_exhibit` | 启动展项 | `exhibit_id` 不得为空，具体展项后续绑定 |
| `reset_experience` | 重置本轮展示 | 只在新生展示模式开启时执行 |

任何未知动作都会被拦截并输出警告，模型不能调用任意 Unity 方法。

## 4. 当前 Mock 行为

Mock 会模拟约 `1.1 秒`的 AI 等待时间，并支持：

### 闲聊

测试词：你好、您好、谢谢、再见。

行为：简短回应，并引导用户继续询问校园内容。

### 无关问题

测试词：股票、投资、电影剧情、做饭、彩票、医疗诊断、写代码。

行为：不回答具体问题，将话题引导回学校、专业、设施或新生生活。

### 不确定问题

测试词：明天有多少人、实时人数、最新排名、明年新增、一定会。

行为：明确说明测试知识库没有足够资料，不进行猜测。

### 打开地图

测试说法：打开地图、校园地图、看看地图。

输出动作：

```json
{
  "action": "open_page",
  "page_id": "map"
}
```

### 舒适模式

测试说法：我容易晕、打开舒适模式、不要平动、只用瞬移。

输出动作：

```json
{
  "action": "set_comfort_mode",
  "comfort_mode": true
}
```

### 地点导航

测试说法：带我去、导航到、怎么去、前往，加上已注册地点名称。

行为：地点合法时开始导航；无法确定目标时要求用户说出具体建筑或展项。

## 5. 交互链路

```text
用户语音或测试文本
    ↓
CampusContextService 添加场景与注视上下文
    ↓
MockDifyChatService / DifyChatService / LocalOllamaService
    ↓
DifyActionPayload
    ↓
CampusAICommandExecutor 白名单验证
    ↓
通知书 UI / 导航 / 场景 / 移动 / 展项 / 展示复位
```

## 6. 后期替换原则

正式接入 Dify 时：

- 不修改 `CampusAICommandExecutor`；
- 不修改地图、导航和场景代码；
- 不修改通知书页面 ID；
- 只让 Dify 返回相同 JSON 字段；
- 知识库和提示词负责生成回答，Unity 继续负责安全执行。

## 7. 下一阶段

下一项交互为：**兴趣方向选择与未来课表**。

第一版继续使用测试文本和程序生成 UI，后期只替换正式图片、字体、课程资料和 Dify 内容。
