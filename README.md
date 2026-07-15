# Xiyue AI NPC Generator

面向 Unity 6000.3 的编辑器插件：让 Gemini 生成 1:1 像素角色 Sprite Sheet，在预览确认后由 Unity 本地完成点采样规范化、绿幕抠除、切片、四方向动画、Animator、NpcDefinition 和 NPC/Player Prefab。

## 安装

1. 在 Unity 打开 **Window > Package Manager**。
2. 点击 `+`，选择 **Add package from disk**。
3. 选择本目录的 `package.json`。
4. 打开 **Window > Xiyue > AI NPC Generator**。

目标版本为 Unity `6000.3.17f1`。包依赖 Unity 2D Sprite Editor Data Provider API。

## 五分钟上手

1. 在插件窗口点击 **创建示例 Rig**，或选择自己的四方向 `NpcRigProfile`。
2. 输入角色描述；展开 **生成指令** 可以调整全局绘图约束。
3. 输入 Gemini API Key。密钥只保存在当前编辑器进程内存；也可以预先设置环境变量 `GEMINI_API_KEY`。
4. 模型默认选择 **Nano Banana 2（推荐）**；复杂角色可选择 **Nano Banana Pro（高质量）**。接口仍在内部使用官方模型 ID。
5. 可按顺序添加参考图，然后输入角色描述，例如：

   `中国古代风格，蓝色长发的年轻女剑客，冷静谨慎，穿蓝白轻甲。`

6. 点击 **开始生成**。
7. 图片生成后检查预览，选择 **确认并生成 NPC**；也可以勾选 **生成后自动完成 NPC**，无警告时自动转换。
8. 完成后在任务卡中选择 **添加 NPC** 或 **添加可控角色**。

### 默认参考图

- 可在角色描述下方选择或直接拖入 Project 中的 PNG/JPEG `Texture2D` 资产。
- 最多 6 张，单张原始文件不超过 5 MB，全部参考图合计不超过 14 MB。
- 保存命名预设时，参考图会按顺序复制到 `Assets/XiyueGenerated/AINpcPresets/{预设名}_{短ID}/References/`，不依赖原始素材继续存在。
- 每个新批次会把预设图片再次冻结到 `Assets/XiyueGenerated/AINpcJobs/{批次ID}/InputReferences/`；之后覆盖或删除预设不会改变已排队任务。
- JSON 和队列只保存托管副本 GUID，不保存绝对路径或图片字节。

### 描述记录与预设

- 关闭窗口或离开输入框时，会自动保存当前“角色描述 + 生成指令 + 绿幕容差 + 参考图”为草稿；下次打开窗口自动恢复。
- 成功创建任务后会保存最近 30 种生成输入；连续使用相同描述与参考图时只保留最近一条。
- 可在面板中为当前完整输入保存预设；下拉框选择预设会立即恢复描述、指令、容差和托管参考图。
- 记录保存在项目 `Library/XiyueAiNpcGenerator/PromptLibrary.json`，采用 v2 文档；旧记录缺少指令时会读取默认值，旧参考图会在下次保存预设时迁移。

生成资源位于 `Assets/XiyueGenerated/NPCs`。每个角色目录包含规范化图集、按 Rig Profile 数量切分的 Sprite、四方向 Idle/Walk 动画、Animator、NpcDefinition、NPC/玩家 Prefab 和 `generation-manifest.json`。

## 批量与恢复

- 批量只是一次创建多个独立任务；每个任务可以单独失败、重试或放弃。
- 默认并发为 4，可配置为 1–20。
- HTTP 408、429 和 5xx 会最多重试三次；认证和请求参数错误会立即失败。
- 队列保存在项目 `Library/XiyueAiNpcGenerator/QueueState.json`。
- 队列使用临时文件原子替换，并保留备份；主文件损坏时会尝试从备份或临时文件恢复。
- 脚本重载或退出 Unity 前，活动请求会被 Abort/Dispose 并标记为 `Interrupted`。
- 已生成预览的待确认任务在域重载或重启后继续显示，可直接确认转换，不重复发出 Gemini 请求。

## Sprite Sheet 规范化

所有成品都按当前 `NpcRigProfile` 处理：

- 图集尺寸为 `frameWidth × framesPerDirection` 和 `frameHeight × directions`。
- V1 固定四方向，默认每方向四帧。
- 生成提示固定要求 1:1；本地会验证方形输出，并按单元格使用最近邻点采样缩放到精确图集尺寸，不使用平滑插值。
- 模型图从上到下的四行会映射到 `directionNames`；Unity 图集使用底部原点时会调整行位置，但不会上下翻转单帧。
- 第一帧生成待机动画，整行生成行走循环。
- 绿色背景按高级设置中的容差转为透明；自动完成遇到比例、网格或空帧警告时会停在预览，人工确认仍可继续。

## 运行时

- `NpcBrain2D`：待机、区域内随机游荡、交互和情绪气泡。
- `NpcClickableInteraction`：点击 NPC 触发对话。
- `NpcProximityInteractor`：玩家靠近后按 `E` 触发最近 NPC；使用新输入系统时也可直接调用 `InteractClosest()`。
- `TopDownPlayerController`：旧 Input Manager 可直接使用；新输入系统调用 `SetMoveInput(Vector2)`。
- `NpcBatchSpawner`：将若干生成 Prefab 按网格实例化。

Runtime 程序集不包含 UnityEditor、Gemini 或 API Key。

## 安全说明

- API Key 不写入 EditorPrefs、ProjectSettings、队列 JSON、生成资产或日志。
- 不要把密钥写进角色提示词。
- “测试 API”按钮会真实使用一次 Gemini 请求。
- `generation-manifest.json` 会记录角色提示词、图片模型、成品图指纹和参考图 GUID，但不记录 API Key 或图片字节。

## 当前 V1 边界

- 只支持 2D 俯视四方向角色。
- 图片模型不能绝对保证帧数和布局，因此自动完成始终先经过本地验证。
- 当前不请求名字、性格或对话；本地使用安全默认数据，仅保留未来 `INpcMetadataProvider` 扩展接口。
- 不包含运行时 AI 对话、复杂记忆、任务系统、行为树、高级寻路或多人同步。
- 旧版已完成任务仍可查看；旧版未完成的部件拼装任务需要重新创建。

## 测试

包内 `Tests/Editor` 覆盖：请求 oneof 契约、图片响应解析、预设 v2 往返、托管参考图、点采样/绿幕/行映射，以及完整 Sprite Sheet 到 Sprite、动画、控制器、定义和双预制体的生成。安装 Unity Test Framework 后可在 Test Runner 中运行。
