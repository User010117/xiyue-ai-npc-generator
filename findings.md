# 发现与决策

## 需求
- 分析当前 Unity 项目（实际仓库形态待确认）。
- 使用简体中文，关注模块化、职责边界、长期维护与技术债。
- 本轮不实施未请求的重构。
- 新请求：优化当前 Unity 包，并参考另一项目的默认参考图体验加入默认参考图能力。

## 研究发现
- 仓库根目录包含 `Runtime`、`Editor`、`Tests`、`Samples~` 和 `package.json`，确认是 Unity Package Manager 包，而非完整 Unity 工程。
- Git 当前位于 `main`，初始检查未显示业务文件改动。
- 包版本 `0.1.0`，目标 Unity `6000.3.17f1`，唯一声明依赖为 `com.unity.2d.sprite@1.0.0`。
- 程序集边界清楚：Runtime 无程序集依赖；Editor 仅依赖 Runtime 与 `Unity.2D.Sprite.Editor`；Editor 测试引用两者且受 `UNITY_INCLUDE_TESTS` 约束。
- 仓库原有非 meta 文件 35 个，本次新增 3 个规划记录；业务 C# 约 24 个，规模较小，适合完整静态审查。
- 产品定位是“AI 生成受约束角色规格 + 本地确定性像素部件拼装”，AI 不直接生成最终 Sprite；这是稳定输出和可测试性的关键边界。
- README 声明 API Key 仅驻留进程内存，队列持久化到 `Library`，活动请求在重载/退出前中止，Runtime 不包含 Editor/Gemini/API Key。
- 业务 C# 共约 3,350 行；最大文件为生成管线 610 行、队列 479 行、窗口 431 行、Gemini Provider 343 行，复杂度集中在 Editor 侧。
- Runtime 共 12 个小型类型，职责大体单一；数据资产（Definition/Rig/Catalog）、NPC 行为、交互、玩家控制和批量生成相互分开。
- Runtime 未引用 `UnityEditor`，符合包的运行时隔离声明。
- 当前代码几乎没有简体中文维护注释，且公开类型/字段/方法大量无注释；这与项目 AGENTS.md 的强制注释规范不一致，后续修改时应增量补齐，不建议一次性制造纯注释大 diff。
- `NpcDefinition.interactionRadius` 有读取属性但未看到运行时使用；`NpcProximityInteractor` 另有独立半径，存在配置来源重复和漂移风险。
- 测试目前 7 个测试方法（含 TestCase 共 8 个运行案例），覆盖档案归一化/拒绝、密钥不序列化、确定性解析、合成尺寸与像素、fallback 校验、颜色解析；尚未覆盖队列恢复/取消/重试、HTTP 错误分类、资产写入回滚和 Runtime 状态机。
- Gemini Provider 使用 `UnityWebRequest`、120 秒超时和 `x-goog-api-key` 请求头；结果句柄支持 Abort/Dispose，队列在域重载和退出前确实会中止并释放活动请求。
- 队列对 408/429/5xx/无响应码执行最多 3 次指数退避；HTTP 2xx 的结构解析失败不会盲目重试，符合“参数/内容问题立即失败”的方向。
- API Key 未出现在任务模型中，`NpcGeneratorSessionSecrets` 只保留静态内存值并回退环境变量；现有测试只证明 JSON 模型无字段，尚未证明日志/manifest/异常路径不泄露。
- 队列存储先删除正式 `QueueState.json` 再移动 `.tmp`，不是崩溃安全替换；删除与移动之间崩溃会丢队列，而且 Load 不尝试 `.tmp` 恢复。这与“可恢复队列”的定位存在中等级数据可靠性风险。
- 队列的本地生成在 Editor update 中一次完整同步执行；像素合成和 AssetDatabase 操作会阻塞编辑器，但当前默认图集规模小，属于可接受的 V1 上限，需用实测再决定是否拆分。
- 部件解析器的“不兼容”处理不完整：先选中部件后才回退，且不会验证 fallback 本身是否同样不兼容；对必选 Body/UpperOutfit，fallback 恰好也是所选项时会直接跳过该槽。管线后续会阻止缺槽资产落盘，但本可处理的任务会被误判为 `NeedsReview`。
- `BuildFingerprint` 接收 `seed` 却未使用；由于指纹当前表达“最终部件组合”而非“生成输入”，行为可能合理，但签名会误导维护者，应明确语义或删掉无用参数。
- Catalog 验证器会把空 tags 数组直接写回 ScriptableObject，却不标脏；验证函数存在隐藏状态修改，结果是否持久化依赖后续别的保存动作。
- 资产写入器内部失败时会删除本次输出文件夹，但质量验证和 manifest 写入发生在 `Write` 返回之后；这两步失败不会回滚已生成资产，任务又不会记录 `outputAssetPath`，重试会生成新目录并留下孤儿资源。这是当前最重要的数据一致性问题。
- 输出目录做了双重校验：ProjectSettings.Persist 与 AssetWriter 均限制在 `Assets/` 且拒绝 `..`，可防止普通 UI 输入把文件写出项目资产目录。
- staging 目录和空输出文件夹在进入 `try` 之前创建；PNG 编码/写入若在此处异常，清理逻辑不会运行，可能残留 `Library/.../Staging` 和空资产目录。
- 质量检查覆盖 Sprite 数量、Definition 引用、Prefab 必要组件/Animator/缺失脚本，但没有检查动画参数/状态、Collider、Definition 与 Prefab 的绑定一致性，也没有对 manifest 内容做最终校验。
- EditorWindow 正确在 `OnDisable` 取消队列事件并 Abort/Dispose API 测试请求；UI 使用虚拟化 ListView，并在重复绑定前移除旧按钮回调，生命周期处理基本稳健。
- 窗口和资产写入多处直接对持久化 `jobId` 调用 `Substring(0, 8)`；队列 JSON 损坏或旧版本迁移产生空/短 ID 时会再次抛错，使恢复 UI 或生成失败。Load 当前只过滤 null，不验证任务字段和文档版本。
- 模型预设直接硬编码在窗口源码，虽然提供“自定义模型”兜底，但默认值/预设随外部服务演进会需要发包更新；当前规模下不值得另建配置系统，保留自定义入口即可。
- 43 个 `.meta` 覆盖主要包资产；仅 `Samples~/Demo/README.md` 无 sidecar。该样例只是复制说明文档，优先级低。
- 静态可执行性检查通过：`package.json` 可解析、UXML 是合法 XML、43 个 meta GUID 无重复。
- 当前机器未发现 Unity 命令或 Unity Hub Editor 安装，且没有 Unity MCP 工具连接；因此本轮不能诚实地声称完成编译、Test Runner 或真实 AssetDatabase 导入验证。
- 队列文档声明了 `documentVersion`，角色与 manifest 也有版本字段，但读取路径没有校验或迁移逻辑；“有版本号”尚未形成真正的版本化恢复机制。
- 全部 Runtime/Editor/Tests 中 XML 文档注释为 0 行、普通独立注释仅 1 行，确认与 AGENTS.md 的简体中文注释规范存在系统性差距。
- `StartRequests` 在存在 Pending 任务但 API Key 为空时，每个 Editor update 都触发 `Changed`；窗口收到事件后会 Rebuild 整个任务列表。大量排队任务等待密钥时会造成无意义的持续 UI 刷新，应只在错误消息发生变化时通知。
- `INpcAssembler`、`INpcPartResolver`、`INpcAssetWriter`、`INpcQualityValidator` 都只有一个实现，且 Pipeline 内部直接 `new` 具体类，接口既不能注入测试替身也没有多实现价值；按最小架构原则应删除这些空抽象，或真正把依赖作为参数传入，二者不要并存。
- README 的“同档案、catalogVersion 和种子得到相同组合”依赖人工保证 catalogVersion 随内容变化；解析器实际不使用版本或部件内容哈希。若版本未升级但部件列表变化，结果仍会变化。
- Gemini 官方当前 `generateContent` 图像理解接口允许同一 contents 中包含多张 inline image 与文字；inline 整体请求建议低于 20MB，并建议单图配文时把文字放在图片后面。
- Unity 侧无需复制或重编码参考图：ProjectSettings 保存 Texture2D GUID，任务创建时复制 GUID 数组，Provider 在真正请求前读取原始 PNG/JPEG 字节并转 Base64。
- 参考任务中的 IndexedDB 是浏览器实现细节，不适合当前 UPM 包；其可复用部分是 6 张/单张 5MB/总量 14MB 的产品边界和“任务快照”语义。
- 默认参考图 UI 采用现有 UI Toolkit：空槽 ObjectField 支持选择/拖放，参考区本身也能批量接收 Project 中的 Texture2D；没有引入新 UI 或状态依赖。
- 队列持久化已改为 `.tmp -> File.Replace -> .bak`，加载按正式、备份、临时顺序恢复；旧 1.0 队列会补空参考图数组和缺失 ID。
- 生成事务现在覆盖 Writer、质量检查和 manifest；任一后置步骤失败都会删除整个本次输出目录，staging 清理错误不再覆盖原始异常。
- 部件解析已改为先过滤 incompatibleTags 再评分，避免不兼容 fallback 被选中或合法候选被错误跳过。

## 技术决策
| 决策 | 理由 |
|------|------|
| 先读程序集定义、包清单、源码清单 | 能最快确认依赖方向和代码边界 |
| 下一步完整跟踪生成链路并抽查 Runtime 生命周期 | 当前规模允许直接验证 README 声明，而不只依赖文档 |
| 不因 610 行文件直接建议拆文件 | 类型职责仍可辨识；先修事务边界与恢复可靠性，收益更高 |
| 将网页参考任务只作为产品意图，不复用 IndexedDB 实现 | 当前仓库是 Unity UPM 包，适合用 Asset GUID + Texture2D 资产 |
| 继续使用现有 generateContent，不迁移到新 API | 默认参考图不需要扩大外部 API 迁移范围，现有结构化输出链路可以直接组合 inlineData |

## 遇到的问题
| 问题 | 解决方案 |
|------|---------|
| 暂无 | - |

## 阶段 6：默认参考图与稳定性优化结论
- 默认参考图应保存为 Unity 资产 GUID，而不是复制图片或保存绝对路径；任务入队时冻结 GUID 快照，保证后续设置变化不影响已排队任务。
- 参考图拖放必须按“非空图片数”而不是列表槽位数判断容量；否则六个槽中存在空槽时仍会被错误拒绝。实现已改为优先填充空槽。
- 队列与生成资产都需要事务边界：队列采用临时文件与备份恢复，生成后置校验或 manifest 失败时删除整次输出目录。
- 参考图使用 Gemini `inlineData`，上限为 6 张、单张 5MiB、原始总量 14MiB，仅 PNG/JPEG；manifest 只记录 GUID，不保存图片字节或 API Key。

## 阶段 7：Unity MCP 验证发现
- `D:\UnityProject\MY` 使用 Unity 6000.3.17f1，已成功通过本地 `file:` UPM 引用加载本包 0.2.0。
- Unity 已真实生成本包 Runtime/Editor DLL，按包名筛选 Console 为 0 错误，说明正式程序集通过 Unity 编译。
- Test Runner 的包测试被宿主工程两个乱码脚本阻塞；错误全部位于 `Assets/Scripts/Elevation_Entry.cs` 与 `Elevation_Stay.cs`，并非本包代码。
- 测试工程原本有场景、动画、素材和 package lock 改动；验证过程仅追加本地包引用和临时 `testables`，未覆盖这些用户改动。
- 用户授权后确认宿主错误是 GBK 源文件被 Unity 6 按 UTF-8 解析，并非脚本逻辑损坏；转换 `Elevation_Entry.cs` 与 `Elevation_Stay.cs` 为 UTF-8 后全量编译恢复。
- 修复后本包 10 个 EditMode 用例全部通过，包含多模态请求顺序、密钥不序列化、结构化数据验证、确定性部件解析、像素合成与不兼容 fallback 回归。
- 生成器窗口可从真实 Unity 菜单打开；本轮未配置 API Key，也未发送任何可能计费的 Gemini 请求。

## 阶段 8：编辑器窗口 UI 截图结论
- 用户截图中的按钮重叠不是按钮自身尺寸问题：根 `.page` 没有滚动容器，设置区、工具栏和最小高度 240px 的任务列表共同参与纵向压缩，UI Toolkit 最终把表单行压到接近 0 高度。
- 最小根因修复是把设置表单放入原生 `ScrollView`，任务区保持独立 `flex-grow`；这样低高度时只滚动设置，不会让操作栏和队列互相覆盖。
- 视觉优化复用现有 UI Toolkit：新增标题横幅、编号分区、任务面板、状态标签和主次按钮颜色即可；无需图片、字体、图标包或新的 C# 状态。
- Unity 实测最小窗口 720×560 时，设置区高 293px、任务面板高 160px、任务列表高 78px；关键区域与相邻按钮的 `worldBound` 均不相交。
- 本次 UXML/USS 导入、关闭重开窗口后，Unity Console 为 0 error、0 warning，说明视觉改造未破坏资源加载和既有控件绑定。

## 阶段 9：帮助信息边界
- 用户需要理解每个功能，但窗口最小高度只有 560px；全部常驻说明会重新增加滚动负担。
- 最小方案是保留一条可扫描的整体流程，并把字段约束、按钮副作用和使用时机放入 UI Toolkit 原生 tooltip。
- 实测 28 条 tooltip 均进入 VisualElement 树；五步流程常驻高度仅 28px，没有改变任务面板和任务列表的最小可用高度。

## 阶段 10：Gemini Part oneof
- Gemini `contents[].parts[]` 的 `text` 与 `inlineData` 属于同一 oneof；即使空值字段被序列化，服务端也会按“已设置”处理并返回 400。
- 当前图片和文字请求都由 `GeminiRequest.Create` 构建，因此应在该共享序列化边界修一次，而不是分别在测试按钮和队列调用处打补丁。
- Unity `JsonUtility` 确实把图片 Part 序列化为 `text:"" + inlineData`，同时把文字 Part 序列化为 `text + 空 inlineData`；这与用户收到的服务端错误完全一致。
- 使用只有单一字段的 `GeminiImagePart`、`GeminiTextPart` 后，仍由 JsonUtility 负责 Base64/文本转义，同时天然满足 oneof，优于对最终 JSON 做字段字符串删除。

## 阶段 11：极低窗口与输入记录
- 新截图高度约 440px；现有 `.settings-scroll` 最小 180px、`.queue-panel` 最小 160px，再加标题、外边距和队列工具栏后已经超过可用高度，UI Toolkit 只能裁掉 02 的任务内容。
- 01 已经是独立 `ScrollView`，因此极低窗口应继续压缩 01 的可视高度，并给 02 保留能容纳标题、工具栏和至少一张任务卡的固定下限。
- 角色描述当前只存在于 `TextField`，`OnDisable` 也没有保存；参考图虽然写入 ProjectSettings，但无法与某段描述形成可重用组合。
- 最小可维护数据模型是一个项目本地 JSON 文档：单份草稿、有限生成历史、命名预设；每条记录只保存描述、时间与参考图 Asset GUID。
- Unity 在 1036×440 和最窄 720×440 下都解析为：设置滚动区 123px、任务区 210px、任务列表 88px；工具栏保持 48px，所有关键区域无重叠且任务列表下边界未越过窗口。
- 现有队列包含可复用的旧描述和参考图快照；首次创建输入库时导入队列历史，能让功能上线后立即看到过去输入。
- 真实关窗测试生成 `Library/XiyueAiNpcGenerator/PromptLibrary.json`，重开后草稿字段完整恢复；测试值随后从正式文件和 `.bak` 备份中一并清理。
- EditMode 全量 12/12 通过，其中新增用例覆盖 JSON 往返、描述/参考图去重、同名预设更新和 30 条历史裁剪。

## 阶段 12：Sprite Sheet 一体化生成边界
- 当前预设确实保存了有效参考图 GUID；UI 下拉选择只调用按钮刷新，必须再点“载入所选”才恢复，属于交互状态根因而非资产丢失。
- 现有 `NpcGenerationPipeline` 已具备点采样 Sprite 导入、四方向切片、Idle/Walk Clip、Animator Controller、Definition 与 Prefab 写入能力，可直接复用资产后半段。
- 当前 `GeminiNpcProvider` 生成结构化角色 JSON，与新目标不符；新流程应将请求职责改为图片响应，同时把本地角色基础数据与图片转换解耦。
- 预设删除会让队列中的 GUID 失效，因此仅复制预设图片仍不够；入队时需要为整个批次冻结一次参考图副本。
- 预设 v2 已把描述、指令、绿幕容差和有序托管 GUID 作为完整快照；旧记录读取时补默认指令，只有再次保存时才迁移图片资产。
- 预设参考图采用 staging/backup/final 目录切换；保存中任一复制或移动失败时不会覆盖旧预设目录。
- 新队列按文档版本识别旧任务，避免字段初始化器把缺少 `workflowVersion` 的 1.x JSON 误判为 v2；旧完成记录保留，未完成部件任务要求重新创建。
- 清理队列记录不删除批次参考图快照，因为生成清单仍保存这些 GUID；保持追溯有效优先于自动回收少量素材。
- Gemini 图片响应解析已独立为纯函数：只接受非空 `image/*` inlineData，非法 Base64、空图片和纯文本响应都会在进入转换前失败。
- 本地端到端测试证明：输入图可按 Rig Profile 生成精确 Sprite 数、四方向 Idle/Walk 共 8 个 Clip、Animator、NpcDefinition、NPC 与 Player Prefab。
- 真实预设选择验证结果为描述 15 字、默认指令 252 字和参考图 1 张即时恢复；测试前后的 `PromptLibrary.json` 由备份原样还原。
- 720×440 最终布局仍为设置区 123px、任务区 210px、任务列表 88px，工具栏换行为 48px 且无区域重叠。

## 阶段 13：Nano Banana 模型命名
- Google 官方当前把 `gemini-3.1-flash-image` 命名为 Nano Banana 2，并推荐作为性能、质量、成本和延迟平衡的通用首选。
- Nano Banana Pro 的稳定 API ID 为 `gemini-3-pro-image`，适合复杂指令与高质量资产；旧界面使用的 preview ID 应迁移为稳定 ID。
- Nano Banana 2 Lite 明确不针对多参考图优化，本插件主要依赖有序参考图，因此不应增加为默认选项。
- 显示名称不能直接发送给 API；最小正确边界是一个共享“显示名 → API ID”映射，任务和资产仍保存可追溯的官方 ID。
- 当前项目曾保存 `gemini-3.1-pro-preview`；它和 `gemini-3-pro-image-preview` 都应只作为读取兼容值，并统一迁移到稳定 `gemini-3-pro-image`。
- 实际打开窗口确认旧设置直接显示为 Nano Banana Pro（高质量），任务卡也复用同一显示转换，不会泄漏原始 ID 造成选择困惑。

## 阶段 14：v1 图片输出配置变化
- 用户真实请求返回 400，服务端明确拒绝 `generation_config.responseModalities` 与 `generation_config.imageConfig`。
- Google 当前 v1 REST 高分辨率示例使用 `generationConfig.responseFormat.image.aspectRatio/imageSize`；基础图片模型本身会输出图片，不需要旧 `responseModalities` 强制声明。
- 端点 `v1/models/{model}:generateContent` 与 multipart 内容均正确；最小根因修复是只替换共享 `GeminiImageRequest.CreateJson` 的配置片段。

## 阶段 15：真实端点的最小兼容请求
- 用户真实请求证明当前 v1 端点也拒绝 `generationConfig.responseFormat`，不能继续依赖不同端点或 SDK 版本的图片配置示例。
- Nano Banana 图片模型本身会返回图片；最小兼容请求只需要 `contents/parts`，无需发送任何图片 `generationConfig`。
- 1:1 继续由不可编辑 Rig 提示约束，并由本地 Sprite Sheet 检查兜底；自动完成遇到警告仍会停在预览。

## 阶段 16：模型 ID 与 API 版本
- `gemini-3-pro-image` 和 `gemini-3.1-flash-image` 是当前官方稳定模型代码；用户 404 明确指出它们不在插件使用的 v1 `generateContent` 中。
- Google 当前 GenerateContent 与 Models REST 文档均使用 `v1beta`；共享 Provider 端点切换一次即可同时修复测试按钮和队列任务。

## 资源
- `package.json`
- `README.md`
- `Runtime/`
- `Editor/`
- `Tests/`
