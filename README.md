# Xiyue AI NPC Generator

面向 Unity 6000.3 的编辑器插件：让 Gemini 生成受约束的角色设定，再由 Unity 从认证过的模块化像素部件中确定性拼装角色。AI 不直接生成最终 Sprite，因此尺寸、帧数、方向、锚点和动画结构保持稳定。

## 安装

1. 在 Unity 打开 **Window > Package Manager**。
2. 点击 `+`，选择 **Add package from disk**。
3. 选择本目录的 `package.json`。
4. 打开 **Window > Xiyue > AI NPC Generator**。

目标版本为 Unity `6000.3.17f1`。包依赖 Unity 2D Sprite Editor Data Provider API。

## 五分钟上手

1. 在插件窗口点击 **创建示例部件库**。
2. 示例资源会生成到 `Assets/XiyueGenerated/Demo`，并自动填入 Rig Profile 和部件库。
3. 输入 Gemini API Key。密钥只保存在当前编辑器进程内存；也可以预先设置环境变量 `GEMINI_API_KEY`。
4. 从模型下拉框选择 Gemini 模型；列表外模型可选择 **自定义模型…** 并填写模型 ID。模型切换只影响新建任务。
5. 输入角色描述，例如：

   `中国古代风格，蓝色长发的年轻女剑客，冷静谨慎，穿蓝白轻甲。`

6. 点击 **开始生成**。
7. 完成后在任务卡中选择 **添加 NPC** 或 **添加可控角色**。
8. 生成若干角色后，可以点击 **用已生成角色创建演示场景**，自动建立相机、玩家和最多 20 个 NPC 的演示场景。

生成资源位于 `Assets/XiyueGenerated/NPCs`。每个角色目录包含图集、16 个 Sprite、四方向 Idle/Walk 动画、Animator、NpcDefinition、NPC/玩家 Prefab 和 `generation-manifest.json`。

## 批量与恢复

- 批量只是一次创建多个独立任务；每个任务可以单独失败、重试或放弃。
- 默认并发为 4，可配置为 1–20。
- HTTP 408、429 和 5xx 会最多重试三次；认证和请求参数错误会立即失败。
- 队列保存在项目 `Library/XiyueAiNpcGenerator/QueueState.json`。
- 脚本重载或退出 Unity 前，活动请求会被 Abort/Dispose 并标记为 `Interrupted`。
- 已取得角色档案的任务恢复时直接从本地拼装，不重复发出 Gemini 请求。

## 角色稳定性

所有正式部件必须符合同一 `NpcRigProfile`：

- 图集尺寸为 `frameWidth × framesPerDirection` 和 `frameHeight × directions`。
- V1 固定四方向，默认每方向四帧。
- Texture 必须启用 Read/Write。
- 每一帧必须包含可见像素。
- `Body` 和 `UpperOutfit` 必须各有一个认证 fallback。
- `partId` 在部件库中必须唯一。

生成前会重新认证整个部件库。AI 只返回 `long_hair`、`light_armor`、`blue` 等语义标签；`DeterministicNpcPartResolver` 才能选择真实 partId。相同档案、部件库版本和种子会得到相同组合指纹。

## 自定义部件库

1. 复制示例 `NpcRigProfile` 和 `NpcPartCatalog`。
2. 按相同图集布局制作透明 PNG。
3. Import Settings 设置为 Point、Uncompressed、Read/Write Enabled、Mip Maps Off。
4. 为部件填写唯一 ID、槽位、标签、权重、Tint Mode 和兼容标签。
5. 确保 Body 与 UpperOutfit 存在 fallback。
6. 在插件窗口选择新部件库；开始生成时会自动执行认证。

部件渲染顺序由 `NpcPartSlot` 固定，AI 无法修改。肤色、发色和服装色通过 Tint Mode 应用到灰白模板像素。

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
- `generation-manifest.json` 会记录角色提示词、模型、种子和部件 ID，但不记录 API Key。

## 当前 V1 边界

- 只支持 2D 俯视四方向角色。
- AI 不直接绘制最终 Sprite 或新部件。
- 不包含运行时 AI 对话、复杂记忆、任务系统、行为树、高级寻路或多人同步。
- 演示部件是用于验证流水线的程序化占位美术，应替换为正式美术资源。

## 测试

包内 `Tests/Editor` 覆盖：结构化档案验证、API Key 不进入任务 JSON、确定性部件选择、部件库 fallback 认证、调色解析和像素图集合成。安装 Unity Test Framework 后可在 Test Runner 中运行。
