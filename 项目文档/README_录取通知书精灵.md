# 录取通知书 AI 引导精灵功能说明

## 1. 功能定位

录取通知书是虚拟校园中的统一交互入口，同时承担以下职责：

- 跟随用户的引导精灵；
- 展示当前场景和最近注视的建筑；
- 承载 AI 对话状态与字幕；
- 后续承载校园导航、探索图鉴和任务 UI；
- 将 Unity 场景上下文整理为 Dify Chatflow 所需的数据。

当前版本以两个扁方块代替通知书底页和封面，以运行时生成的 World Space UI 代替最终图片和版式。所有原型对象均由代码生成，后续可以替换模型和图片而不改变状态机与对话逻辑。

## 2. 当前已实现功能

### 2.1 自动创建

进入任意带有 XR Origin 和 Main Camera 的场景后，系统自动创建：

- `Campus Context Service`：跨场景保存当前校园上下文；
- `Admission Letter Guide`：通知书模型、临时 UI 和交互状态；
- `MockDifyChatService`：暂时代替 Dify API 返回模拟结果。

通知书与上下文服务使用 `DontDestroyOnLoad`，切换主场景、教室和展厅时不会重复创建。

### 2.2 通知书外观

当前占位外观由两个 Cube 组成：

- 红色扁方块：通知书底页；
- 金色扁方块：通知书封面；
- 封面以左侧为转轴进行开合；
- 页面内使用临时 World Space Canvas 显示文字。

通知书默认停靠在头显视野左下方，避开用户观察建筑时的中央视野。它使用平滑延迟跟随，并带有轻微上下漂浮效果；吸附到左手后改为跟随左手位置。

### 2.3 PICO 4 操作

| 操作 | 功能 |
|---|---|
| 左手柄 Grip | 打开或关闭录取通知书 |
| 右手柄 Grip 按下 | 进入“聆听中”状态 |
| 右手柄 Grip 松开 | 提交占位问题并模拟 Dify 回复 |
| 左手射线命中通知书并按一次 Trigger | 通知书吸附到左手上方并朝向头显 |
| 已吸附时再按一次左 Trigger | 解除吸附，恢复漂浮跟随 |
| 头显朝向建筑 | 更新当前注视上下文 |

右 Grip 当前只模拟“语音识别完成后提交文本”的流程。接入现有 FunASR 时，应在按下时开始录音，在松开时结束录音，并将识别文字传给同一个聊天请求入口。

通知书带有独立的 BoxCollider。左手射线命中碰撞箱时，通知书会轻微放大提示可吸附；按一次左手食指 Trigger，通知书会吸附到左手上方并自动朝向头显。吸附后不需要持续按键，再按一次左 Trigger 即可解除吸附。操作不判断手柄与通知书的距离。左 Grip 始终只控制开合，右 Grip 始终只控制对话。

吸附到左手后，通知书本体的 BoxCollider 会暂时关闭，避免挡住右手射向悬浮菜单的射线；解除吸附时碰撞箱自动恢复，之后仍可再次用左手射线选中通知书。

### 2.4 翻开后的主菜单

通知书关闭时不显示 UI。按左 Grip 翻开封面时，通知书上方投影出独立的悬浮全息主菜单。菜单随开合进度缩放并淡入；关闭时缩回通知书并淡出。菜单跟随通知书的位置，但会沿用户视线向外偏移约 35 厘米，避免贴近面部；同时与通知书和手柄的旋转解耦，始终保持世界竖直并水平朝向用户。当前临时主菜单包括：

- AI 校园向导；
- 校园地图；
- 探索图鉴；
- 任务中心。

主菜单已经实现右手射线交互：按钮被右手射线命中时高亮，按右手 Trigger 确认。通知书纸面作为开合和投影载体，不再承担大面积菜单排版。

主菜单包含四个可切换页面：

- `AI 校园向导`：显示使用说明、识别/思考/回答状态，为 FunASR 和 Dify 预留；
- `校园地图`：可直接进入主场景、教室或展厅；
- `探索图鉴`：显示三个场景的印章占位和后续访问记录入口；
- `任务中心`：显示虚拟校园体验任务占位；
- 每个子页面均提供右手射线可点击的“返回”按钮。

### 2.5 UI 图片替换规范

菜单布局、碰撞箱、悬停、点击和页面切换均由代码维护。后期只需要将图片以 Sprite 类型导入以下目录：

```text
Assets/Resources/AdmissionLetterUI/
```

支持的固定资源名如下：

| 文件名（不含扩展名） | 用途 |
|---|---|
| `menu_background` | 整个悬浮菜单背景 |
| `button_default` | 所有按钮的通用备用背景 |
| `button_ai_guide` | AI 校园向导入口 |
| `button_map` | 校园地图入口 |
| `button_collection` | 探索图鉴入口 |
| `button_tasks` | 任务中心入口 |
| `button_scene_main` | 主场景按钮 |
| `button_scene_classroom` | 教室按钮 |
| `button_scene_exhibition` | 展厅按钮 |
| `button_back` | 返回按钮 |

支持 PNG、JPG 等 Unity 可导入格式，但 Texture Type 必须设置为 `Sprite (2D and UI)`。没有对应图片时自动使用当前蓝色临时样式，不影响功能。

### 2.6 状态机

通知书包含以下状态：

| 状态 | 含义 |
|---|---|
| `Following` | 关闭并跟随头显 |
| `Notifying` | 发现新的建筑上下文 |
| `Opening` | 正在打开或关闭 |
| `Open` | 页面和 UI 已打开 |
| `Listening` | 正在等待用户说话 |
| `Thinking` | 等待 AI 返回结果 |
| `Speaking` | 展示或播放 AI 回答 |
| `Guiding` | 预留的路线引导状态 |

后期动画、粒子和音效应监听这些状态，不要直接依赖 Dify 网络代码。

## 3. 注视上下文

现有 `GazeDetector` 仍负责从头显中心发射射线并识别 `BuildingInfo`。当建筑开始被注视时，`BuildingInfo` 将以下信息写入 `CampusContextService`：

- 当前 Unity 场景名；
- 地点稳定 ID；
- 建筑显示名称；
- 建筑描述；
- 最近关注时间。

最近地点会保留 15 秒。这样用户从建筑转头看向通知书后，仍然可以问“这里是什么”“它有什么特色”。

建议后续为每个 `BuildingInfo` 手工填写稳定的 `locationId`，例如：

```text
main_library
main_gate
classroom_101
exhibition_ai_history
```

如果没有填写，系统会暂时根据建筑名称生成 ID。

## 4. Dify 预留逻辑

### 4.1 当前状态

当前版本不访问网络，也不需要 API Key。`MockDifyChatService` 延迟约 1.1 秒后返回一条模拟回复，用于验证完整状态流程：

```text
Listening → Thinking → Speaking → Open
```

### 4.2 请求模型

系统已经整理好与 Dify Chatflow 对应的请求字段：

```json
{
  "inputs": {
    "scene_name": "Main",
    "location_id": "main_library",
    "location_name": "图书馆",
    "location_description": "图书馆简介",
    "interaction_type": "voice"
  },
  "query": "请介绍一下这里",
  "response_mode": "blocking",
  "conversation_id": "",
  "user": "本机持久用户ID"
}
```

首次对话的 `conversation_id` 为空，模拟服务返回后会保存会话 ID，后续问题继续使用同一会话。用户 ID 使用 `PlayerPrefs` 生成并持久保存。

### 4.3 期望的结构化回答

预留的 Unity 回答结构为：

```json
{
  "speech": "当然可以，我带你去人工智能展厅。",
  "emotion": "happy",
  "action": "navigate",
  "target_id": "exhibition_ai",
  "suggestions": [
    "这个展厅有什么？",
    "带我去教室"
  ]
}
```

建议只允许以下动作白名单：

- `none`
- `navigate`
- `open_page`
- `award_stamp`

Unity 收到结果后必须校验动作和目标 ID，不能让模型直接调用任意方法或生成场景坐标。

### 4.4 正式接入方法

正式接入时新增一个实现 `IDifyChatService` 的网络类，并将 `MockDifyChatService` 替换为正式实现即可。其余状态、UI、上下文和控制器输入不需要重写。

推荐网络结构：

```text
Unity / PICO
    ↓ 不携带 Dify 密钥
本地或云端代理服务
    ↓ Authorization: Bearer API_KEY
Dify Chatflow
```

不要将正式 Dify API Key 写入 Unity 脚本、场景或 APK。

## 5. 后期替换美术资源

临时模型集中在 `AdmissionLetterGuide.BuildPlaceholderModel()` 中创建。替换时建议制作一个 Prefab，保留以下层级语义：

```text
Admission Letter Guide
├── Back Page
├── Cover Hinge
│   └── Front Cover
└── Letter UI
```

后期可以替换：

- 两个 Cube → 通知书模型或带贴图的 Plane；
- 临时颜色材质 → 录取通知书正反面图片；
- Unity UI Text → TextMesh Pro；
- 简单开合 → Animator 动画；
- 临时 UI → 正式双页布局；
- 正弦漂浮 → 粒子、拖尾和提示音效。

替换时应继续以 `Cover Hinge` 作为封面旋转轴，以免重写开合逻辑。

## 6. 代码文件

| 文件 | 职责 |
|---|---|
| `AdmissionLetterGuide.cs` | 模型、跟随、输入、开合、UI 和状态机 |
| `CampusContextService.cs` | 当前场景与最近注视地点 |
| `DifyChatContracts.cs` | Dify 请求、响应和服务接口 |
| `MockDifyChatService.cs` | 无网络的占位回复 |
| `PicoLivePreviewBootstrap.cs` | 自动创建通知书系统 |
| `BuildingInfo.cs` | 将注视建筑写入上下文 |

## 7. 后续开发顺序

1. 将右 Grip 与 FunASR 的开始录音、结束录音连接；
2. 把识别出的真实文本替换当前占位问题；
3. 编写服务端 Dify 代理和正式 `IDifyChatService`；
4. 将回答交给现有 TTS 与 Avatar 口型；
5. 建立 `target_id` 到场景/导航点的白名单映射；
6. 实现 `Guiding` 状态和预设路径点；
7. 添加探索徽章和通知书双页正式 UI；
8. 最后替换通知书图片、材质、字体和动画。

## 8. 当前原型测试流程

1. 启动 PDC 和 Unity Play；
2. 等待头显画面与控制器追踪正常；
3. 朝带有 `BuildingInfo` 的建筑看去；
4. 按左 Grip 打开通知书，检查场景名和关注地点；
5. 按住右 Grip，页面显示“正在聆听”；
6. 松开右 Grip，页面显示“正在思考”；
7. 约 1.1 秒后出现带有当前地点的模拟回答；
8. 再按左 Grip 关闭通知书。
