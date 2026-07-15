# 进度日志

## 会话：2026-07-15

### 阶段 1：项目盘点
- **状态：** complete
- **开始时间：** 2026-07-15
- 执行的操作：
  - 读取 planning-with-files-zh、Unity MCP 与 Ponytail 规范。
  - 检查仓库根目录与 Git 状态。
  - 读取文件清单、`package.json`、`README.md` 与全部程序集定义。
  - 统计源码规模、生命周期方法和常见技术债标记。
  - 完整阅读 Runtime 与现有 Editor 测试。
  - 完整阅读 Gemini 请求、队列状态机、队列存储、档案验证、部件认证、确定性解析和像素合成。
  - 完整阅读资产生成管线、质量检查、演示内容/场景、EditorWindow、UXML/USS 与样例文档。
  - 校验 JSON/XML、meta GUID、注释覆盖、版本字段使用，并检查本机 Unity/Unity MCP 可用性。
  - 完成风险分级与最小改进顺序。
- 创建/修改的文件：
  - `task_plan.md`
  - `findings.md`
  - `progress.md`

## 测试结果
| 测试 | 输入 | 预期结果 | 实际结果 | 状态 |
|------|------|---------|---------|------|
| Git 工作区基线 | `git status --short --branch` | 识别分析前状态 | `main...origin/main`，无已显示业务改动 | 通过 |
| 包清单解析 | PowerShell `ConvertFrom-Json` | 合法 JSON | 无错误 | 通过 |
| UXML 解析 | PowerShell XML parser | 合法 XML | 无错误 | 通过 |
| meta GUID 唯一性 | 43 个 `.meta` | 无重复 GUID | 0 组重复 | 通过 |
| API Key 静态扫描 | 常见 Gemini key 模式 | 仓库无硬编码密钥 | 未发现密钥值 | 通过 |
| Unity 编译与 Test Runner | Unity 6000.3 host | 编译并执行 8 个测试案例 | 本机无 Unity/无 MCP 连接 | 未执行 |

## 错误日志
| 时间戳 | 错误 | 尝试次数 | 解决方案 |
|--------|------|---------|---------|
| - | 无 | 0 | - |

## 五问重启检查
| 问题 | 答案 |
|------|------|
| 我在哪里？ | 默认参考图、稳定性修复与 Unity 验证均已完成 |
| 我要去哪里？ | 交付变更范围、验证证据和使用说明 |
| 目标是什么？ | 优化 Unity NPC 生成包并加入可持久化的默认参考图功能 |
| 我学到了什么？ | 见 findings.md |
| 我做了什么？ | 见上方记录 |

### 阶段 5：默认参考图与稳定性优化
- **状态：** complete
- 执行的操作：
  - 恢复上一轮项目分析记录并确认 Git 仅有三份未跟踪规划文件。
  - 将参考任务的浏览器存储方案转换为 Unity Asset GUID 方案。
  - 核对 Gemini 官方多图 inlineData、20MB 请求边界和图片/文字 parts 顺序。
- 预计修改范围：
  - Editor 设置、窗口、队列任务、Gemini 请求、生成管线与 Editor 测试。

### 阶段 6：实施与根因优化
- **状态：** complete
- 已确认实施边界：6 张 PNG/JPEG、单张 5MB、总量 14MB，任务冻结 GUID 快照。
- 已实现设置 GUID、任务快照、Gemini inlineData、窗口选择/拖放和 manifest 溯源字段。
- 已修复队列原子保存与恢复、写盘错误展示、空 Key 每帧刷新、生成资产完整回滚及部件 fallback 兼容过滤。
- 文档/版本首次组合补丁因 README 上下文少了列表前缀而未应用；已改为读取准确位置后分段更新。
- Roslyn 的 PowerShell 加载方案受运行时版本限制而失败；已改由 `dotnet` 直接运行 `csc.dll`，所有 C# 文件未发现 CS1xxx 语法/解析错误。缺少 Unity 引用产生的类型错误属于预期，不等同于 Unity 编译通过。
- 代码审查修复了“六个参考图槽中含空槽时无法继续拖入图片”的边界问题：拖拽现在优先填充空槽，再占用新名额。

### 阶段 7：测试与交付
- **状态：** complete
- 正在执行 JSON/UXML、meta GUID、差异空白、密钥扫描、C# 无引用语法检查和最终工作区复核。
- 用户启用了 `D:\UnityProject\MY` 的 Unity MCP；已通过标准 MCP 会话确认 Unity 6000.3.17f1、Editor 空闲，并由 Package Manager 安装本地包 0.2.0。
- Unity 真实编译已生成 `Xiyue.AINpcGenerator.Runtime.dll` 与 `Xiyue.AINpcGenerator.Editor.dll`，包路径 Console 错误为 0。
- 宿主工程已有 `Assets/Scripts/Elevation_Entry.cs` 与 `Elevation_Stay.cs` 乱码语法错误（共 125 条），导致启用 `testables` 后包测试程序集仍未生成；为保护用户改动，本轮不修改或临时移走这两个无关脚本。
- Unity Test Runner 调用本身成功，但仅运行宿主根节点且报告 0 个具体测试，不能计作包测试通过；将补充无副作用 Editor 烟雾验证并明确交付限制。
- 用户随后明确授权修复宿主错误；已确认两份脚本实际为 GBK 编码的合法中文 C#，机械转换为 UTF-8 无 BOM，代码逻辑和中文序列化字段名保持不变。
- 修复后 Unity 全量编译成功、Console 为 0 error，包测试树发现 10 个 EditMode 用例；测试结果 10/10 通过、0 失败、0 跳过。
- 已通过 `Window/Xiyue/AI NPC Generator` 打开真实 EditorWindow，窗口尺寸 720×560，未发起 Gemini 请求。
- 临时 `testables` 已移除，本地包引用保留；最终域重载完成且 Editor `ready_for_tools=true`。

### 阶段 8：编辑器窗口响应式布局
- **状态：** complete
- 已查看用户的 1036×534 截图并完整核对 UXML/USS；确认低高度重叠来自根布局缺少滚动边界和表单控件被纵向压缩。
- 采用仅修改 `NpcGeneratorWindow.uxml` 与 `NpcGeneratorWindow.uss` 的最小方案：设置区滚动、任务区伸缩，增加原生样式层级，不改业务事件和生成状态。
- 已完成标题横幅、状态标签、编号分区、设置子面板、任务面板和主次按钮样式；25 个原有控件名称全部保留。
- Unity 在 970×813 下解析为设置区 330px、任务区 376px、任务列表 254px；在最小 720×560 下解析为设置区 293px、任务区 160px、任务列表 78px。
- 最小尺寸下设置区与任务区、提示输入与工具栏、相邻按钮均无矩形重叠；导入并打开窗口后 Console 为 0 error、0 warning。
- Windows 窗口截图接口返回“不支持此接口”，已停止 UI 自动化，没有使用其他前台脚本绕过；Unity 内部 resolved layout 验证已覆盖用户报告的高度不足问题。

### 阶段 9：功能说明与悬停帮助
- **状态：** complete
- 采用 UI Toolkit 原生 `tooltip`：常驻区域只显示一条五步流程，其余解释在鼠标悬停时出现，不增加 C# 状态和额外帮助窗口。
- 已为角色描述、参考图、API、模型、批量设置、Unity 输出和六个队列操作补齐 28 条中文 tooltip。
- Unity 最小窗口 720×560 下流程条高 28px，设置区与任务区、相邻按钮均无重叠，任务列表仍保留 78px；Console 为 0 error、0 warning。

### 阶段 10：Gemini 多模态 oneof 修复
- **状态：** complete
- 用户真实 API 测试返回 400：图片 Part 同时设置了 oneof 中的 `data` 与 `inlineData`。
- 已定位所有请求均经过 `GeminiRequest.Create`；当前 `GeminiPart` 同时公开 `text` 和 `inlineData`，测试未断言互斥字段缺失。
- Unity 复现确认旧 JSON 同时包含空 `text` 和空 `inlineData`；已改为图片/文字两个最小 DTO 分别序列化，由共享 `CreateJson` 组装请求。
- 修复后 Unity 实际 JSON 为图片 Part 仅含 `inlineData`、文字 Part 仅含 `text`；定向测试 1/1、包内全量测试 10/10 通过。
- 临时 `testables` 已从宿主 manifest 移除；最终域重载完成，Editor ready，Console 为 0 error、0 warning。

### 阶段 11：极低窗口布局与角色输入记录
- **状态：** complete
- 已核对用户 1036×440 截图与当前 USS：设置区 180px 和任务区 160px 的双重最小高度在极低窗口无法同时满足，导致 02 列表被裁切。
- 已确定使用一个项目 `Library` 下的 JSON 文档保存草稿、有限历史和命名预设；参考图沿用 Asset GUID，不保存绝对路径或图片副本。
- 下一步复用现有窗口生命周期与任务入队边界接入自动草稿、历史去重和预设面板。
- 已新增 `NpcPromptRecordStore`：项目 Library JSON、原子替换、备份恢复、草稿、最近 30 条去重历史和按名称更新的预设。
- 已在窗口接入历史/预设下拉框、载入、保存、删除；参考图仍使用 Asset GUID，缺失资产会跳过并提示。
- 已将窗口最小高度降为 440px，设置区最小 84px 可滚动、任务区固定保留 210px；720×440 与 1036×440 实测任务列表均有 88px 且无重叠。
- Unity 编译和 Console 检查为 0 error、0 warning；EditMode 12/12 通过。
- 已真实验证关窗写入与重开恢复，并把测试草稿从正式/备份 JSON 中清理；临时 `testables` 已从宿主 manifest 移除。

### 阶段 12：预设托管参考图与 Sprite Sheet 一体化生成
- **状态：** complete
- 已确认预设 JSON 中的参考图 GUID 有效，未恢复的根因是下拉框只更新按钮状态、没有调用载入逻辑。
- 已锁定单一流程：Gemini 只生成 Sprite Sheet；用户确认或自动验证后，按 Rig Profile 生成四方向待机/行走动画与 NPC 预制体。
- 已锁定绿幕自动抠除且容差随预设保存；NPC 文本数据使用本地默认值，本轮不产生第二次模型请求。
- Unity 首次编译发现 3 处 `out` 变量受空值短路影响未赋值；已统一改为预先初始化错误文本。
- 第二次 Unity 编译通过且 Console 0 error/0 warning；首次 Test Runner 因宿主未启用包 `testables` 而没有具体用例，开始临时启用后重跑。
- 启用测试后包测试程序集被发现；新增行映射测试误用 `GetPixel32` 导致测试程序集编译失败，已改为 `GetPixels32`。
- EditMode 全量 15/15 通过，覆盖图片 oneof 请求、预设托管副本、绿幕选择性和顶部行到 Unity Atlas 的映射；宿主临时 `testables` 已移除。
- 已修复旧队列版本识别：按文档版本补 1.x/2.x 工作流标记，不依赖字段初始化器；清理完成记录不再删除成品清单仍引用的批次图片。
- 已抽出 Gemini 图片响应解析器，并补充合法图片、纯文本、空数据和非法 Base64 的无网络测试。
- 已增加完整本地资产测试：Sprite Sheet 成功生成精确 Sprite、8 个动画、Animator、NpcDefinition、NPC/Player Prefab，临时资产全部自动清理。
- 最终 EditMode 全量 20/20 通过，0 失败、0 跳过；Unity Console 0 error、0 warning。
- 真实现有预设选择验证通过：描述、252 字默认指令和 1 张参考图均即时恢复；测试 JSON 已从备份原样还原。
- 720×440 最终布局：设置区 123px、任务区 210px、工具栏 48px、任务列表 88px，无重叠。
- 包版本升级为 0.3.0，README 与 CHANGELOG 已改为图片优先一体化流程；宿主临时 `testables` 已移除。

### 阶段 13：Nano Banana 模型显示名
- **状态：** complete
- 已核对 Google 官方模型对应关系；采用 Nano Banana 2（推荐）和 Nano Banana Pro（高质量），内部保留官方 API ID。
- 按 Ponytail 最小实现：复用现有 DropdownField，只增加一份共享映射，不增加配置文件或模型注册系统。
- 下拉框已改为 Nano Banana 2（推荐）、Nano Banana Pro（高质量）和自定义模型；任务卡使用相同友好名称。
- 新任务内部仍发送稳定官方 ID：`gemini-3.1-flash-image` 与 `gemini-3-pro-image`。
- 映射定向测试 1/1 通过；现有旧设置 `gemini-3.1-pro-preview` 实际打开后恢复为 Nano Banana Pro（高质量）。
- Unity 编译通过，Console 0 error、0 warning；宿主临时 `testables` 已移除。

### 阶段 14：Nano Banana v1 图片配置兼容
- **状态：** complete
- 已根据用户 400 错误与 Google 当前 REST 示例定位根因：旧图片配置字段已不兼容 v1，改用 `responseFormat.image`。
- 共享请求 JSON 已移除 `responseModalities` 和 `imageConfig`，保留 `responseFormat.image.aspectRatio=1:1` 与 `imageSize=1K`。
- 请求契约定向测试 1/1 通过，Unity 编译通过，Console 0 error、0 warning；未调用真实 API。
- 宿主临时 `testables` 已移除。

### 阶段 15：移除 v1 不兼容的图片 generationConfig
- **状态：** complete
- 用户真实请求返回 `responseFormat` 未知字段；已删除共享请求中的全部图片 `generationConfig`，保留合法的图片 inlineData 和文字 Part。
- 回归契约改为明确禁止 `generationConfig/responseModalities/imageConfig/responseFormat` 回流，并确认默认指令仍包含 1:1 约束。
- 请求契约定向测试 1/1 通过，Unity 编译通过且 Console 0 error/0 warning；未调用真实 Nano Banana API。
- 宿主临时 `testables` 已移除。

### 阶段 16：Nano Banana 模型端点兼容
- **状态：** complete
- 用户真实请求在 v1 返回模型 404；官方模型代码无需回退，已将共享生成端点切换为 v1beta。
- 模型映射测试增加端点版本断言，防止以后误改回不支持当前图片模型的 v1。
- 定向测试 1/1 通过，Unity 编译通过且 Console 0 error/0 warning；未调用真实 Nano Banana API。
- 宿主临时 `testables` 已移除。
