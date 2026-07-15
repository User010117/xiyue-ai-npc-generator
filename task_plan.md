# 任务计划：Unity NPC 生成器项目分析

## 目标
把插件统一为“Gemini 生成 Sprite Sheet → 预览/自动确认 → Unity 本地生成 NPC”的单一流程，并让描述、指令、绿幕容差和托管参考图作为可恢复预设长期使用。

## 当前阶段
阶段 16（已完成）

## 各阶段

### 阶段 1：项目盘点
- [x] 统计目录、源码、程序集与依赖
- [x] 阅读 README、包清单和核心入口
- **状态：** complete

### 阶段 2：架构与调用链
- [x] 识别 Editor/Runtime 边界
- [x] 跟踪生成、配置、网络与文件写入链路
- **状态：** complete

### 阶段 3：质量与风险
- [x] 检查测试、生命周期、异常处理和硬编码
- [x] 形成按严重度排序的证据清单
- **状态：** complete

### 阶段 4：交付
- [x] 汇总优势、问题和最小改进路线
- [x] 同步 findings.md 与 progress.md
- **状态：** complete

### 阶段 5：默认参考图设计与调用链确认
- [x] 核对 Gemini 多模态请求格式和现有设置/队列边界
- [x] 确定参考图数量、体积、格式、持久化与失败策略
- **状态：** complete

### 阶段 6：实施与根因优化
- [x] 实现参考图设置、UI、请求序列化和任务快照
- [x] 修复资产回滚、队列保存、空 Key 刷新和 fallback 选择
- [x] 添加简体中文维护注释
- **状态：** complete

### 阶段 7：测试与交付
- [x] 增加最小回归测试并执行可用静态/Unity 验证
- [x] 更新 README、findings.md 和 progress.md
- [x] 复核工作区范围并交付
- **状态：** complete

### 阶段 8：编辑器窗口响应式布局
- [x] 根据用户截图定位低高度重叠根因
- [x] 使用原生 ScrollView 重组设置区与任务区
- [x] 增加克制的标题、分区、状态和按钮视觉层级
- [x] 在 Unity 中验证最小高度布局与 Console
- **状态：** complete

### 阶段 9：功能说明与悬停帮助
- [x] 增加一条常驻使用流程，避免用户面对纯控件猜操作顺序
- [x] 为输入、参考图、生成设置、输出和队列操作补充中文 tooltip
- [x] 在 Unity 最小窗口验证布局与 Console
- **状态：** complete

### 阶段 10：Gemini 多模态 oneof 修复
- [x] 复现并确认 JsonUtility 是否把 `text` 与 `inlineData` 同时写入同一个 Part
- [x] 在共享请求序列化入口保证每个 Part 只包含一种数据
- [x] 增加回归断言并通过 Unity 编译、EditMode 测试与 Console 检查
- **状态：** complete

### 阶段 11：极低窗口布局与角色输入记录
- [x] 根据 1036×440 截图确认任务区被双重最小高度挤压的根因
- [x] 调整设置区与任务区伸缩边界，保证极低窗口仍能操作任务列表
- [x] 新增项目本地 JSON 草稿、生成历史和命名预设存储
- [x] 在面板中实现记录选择、载入、保存预设与删除预设
- [x] 增加回归测试并完成 Unity 编译、最小高度布局与 Console 验证
- **状态：** complete

### 阶段 12：预设托管参考图与 Sprite Sheet 一体化生成
- [x] 修复预设选择即载入，并把描述、生成指令、绿幕容差和参考图绑定为完整快照
- [x] 将预设参考图复制到独立 Assets 目录，覆盖/删除时同步维护，入队时冻结批次副本
- [x] 将 Gemini 调整为只生成 1:1 Sprite Sheet 图片，并修复/测试多模态 oneof 请求
- [x] 实现预览确认、自动绿幕抠除、Rig Profile 点采样切片与 NPC 预制体生成
- [x] 升级队列恢复状态与低高度 UI，保留未来 DeepSeek 元数据接口但本轮不调用
- [x] 完成 Unity 编译、EditMode 测试、Console 和 720×440 布局验收
- **状态：** complete

### 阶段 13：Nano Banana 模型显示名
- [x] 下拉框使用 Nano Banana 2（推荐）和 Nano Banana Pro（高质量）
- [x] 内部继续保存官方 API ID，并兼容旧设置和自定义模型
- [x] 任务卡显示友好名称，完成 Unity 编译与 Console 验证
- **状态：** complete

### 阶段 14：Nano Banana v1 图片配置兼容
- [x] 将旧 `responseModalities/imageConfig` 替换为 v1 `responseFormat.image`
- [x] 更新请求契约测试并完成 Unity 编译、测试和 Console 验证
- **状态：** complete

### 阶段 15：移除 v1 不兼容的图片 generationConfig
- [x] 根据真实 400 响应确认 `responseFormat` 同样不受当前 v1 端点支持
- [x] 请求收敛为仅包含合法 `contents/parts` 的最小 JSON
- [x] 继续通过默认指令和本地验证保证 1:1 与 Rig 布局
- [x] 运行请求契约测试、Unity 编译与 Console 检查
- **状态：** complete

### 阶段 16：Nano Banana 模型端点兼容
- [x] 根据真实 404 与官方 Models/GenerateContent 文档确认模型 ID 正确、API 版本错误
- [x] 将测试和队列共用端点从 v1 切换到 v1beta
- [x] 运行模型映射测试、Unity 编译与 Console 检查
- **状态：** complete

## 已做决策
| 决策 | 理由 |
|------|------|
| 本轮只读分析，不改业务代码 | 用户只要求“分析项目”，未授权重构 |
| 优先仓库内事实，不先启动 Unity | 该目录是 UPM 包，静态结构足以完成第一轮架构审查 |
| 改进顺序先保数据，再补测试，最后清理结构 | 队列与资产一致性比风格和抽象问题风险更高 |
| 默认参考图保存 Unity 资产 GUID，任务保存 GUID 快照 | 不复制图片、不保存绝对路径，移动资产后仍可恢复，旧队列可自然兼容空数组 |
| 参考图限制为 6 张、单张 5MB、原始总量 14MB，仅 PNG/JPEG | Base64 后仍可控制在 Gemini inline 请求约 20MB 边界内，且 Unity 原生稳定导入这些格式 |
| 参考图先于文字写入 parts | 与 Gemini 图像理解建议一致，文字提示作为最后一个 part 明确约束输出 |
| 草稿、历史和预设保存到项目 `Library` 下的 JSON | 数据跟随本机 Unity 项目且不污染资产或版本库；参考图继续保存 GUID，不复制图片 |
| 生成历史在任务成功入队时记录，连续相同输入去重 | 历史代表真实使用过的输入，同时避免重复点击制造无意义记录 |
| 极低窗口优先保留任务面板高度 | 01 设置区已有独立滚动；02 承担开始、暂停和错误恢复，不能再次被压到不可用 |
| 生成流程统一为 Gemini Sprite Sheet → 预览/自动确认 → Rig Profile NPC | 用户明确不要两个工具；现有切片、动画和预制体写入能力继续复用 |
| Gemini 本轮只负责图片，NPC 数据由本地默认值生成 | 避免第二次 API 调用；仅保留可选元数据 Provider 接口供后续 DeepSeek 实现 |
| 预设图片复制到 Assets 托管目录，任务再冻结批次副本 | 解决预设恢复失败，并避免删除预设破坏已排队任务 |

## 遇到的错误
| 错误 | 尝试次数 | 解决方案 |
|------|---------|---------|
| 无 | 0 | - |
| README/版本组合补丁因安全说明原文带列表前缀而上下文不匹配 | 1 | 已读取准确行，拆分为小补丁后继续，不重复原补丁 |
| Roslyn 语法检查首次把 SDK 根目录误当成完整版本目录 | 1 | 改为解析 `dotnet --list-sdks` 的版本号并拼接真实 Roslyn 路径 |
| PowerShell 运行时无法加载 .NET SDK 自带 Roslyn 程序集 | 1 | 不再用 `Add-Type`；改由 `dotnet` 直接执行 `csc.dll` 做无引用语法检查 |
| 阶段记录组合补丁因 `progress.md` 上下文位置不同而未应用 | 1 | 按文件拆分并基于实际尾部内容追加，不重复组合补丁 |
| Unity Test Runner 只发现宿主根节点、包测试程序集未生成 | 2 | 已启用 UPM `testables` 并复编；确认根因是宿主 `Elevation_Entry.cs`/`Elevation_Stay.cs` 乱码编译错误，保持用户脚本不动，改做包程序集与 Editor 烟雾验证 |
| PowerShell 5.1 不支持 `Get-Content -Encoding Ansi` | 1 | 不重复该参数；直接用 .NET `Encoding.GetEncoding(936)` 解码 GBK，再机械转换为 UTF-8 |
| 规划收尾组合补丁因五问段落位于 `progress.md` 而未应用 | 1 | 分文件读取准确上下文并分别收尾 |
| Unity 浮动窗口首次改尺寸时仍保持原尺寸 | 1 | 先取消最大化，再按窗口声明的最小尺寸 720×560 重设并验证布局 |
| `execute_code` 缺少必填 `action` 参数 | 1 | 读取 MCP 工具 schema 后补充 `action: execute` |
| MCP 临时代码未导入 UI Toolkit 查询扩展 | 1 | 改用递归遍历 VisualElement，不再依赖 `Q()` 扩展 |
| PowerShell 的双引号正则命令解析失败 | 1 | 改用单引号模式执行 `rg`，不重复原命令 |
| Windows 窗口截图接口返回“不支持此接口” | 1 | 停止 UI 自动化；以 Unity 内部 resolved layout 边界和 Console 作为验收证据 |
| 读取测试 asmdef 时使用了错误文件名 | 1 | 用 `rg --files Tests` 定位真实文件 `Xiyue.AINpcGenerator.Tests.Editor.asmdef`，不重复原路径 |
| `batch_execute` 不支持会话级 `set_active_instance` | 1 | 将实例选择改为独立 MCP 调用，再执行 Unity 命令 |
| 阶段 11 UXML/USS 组合补丁把顶部提示误当成角色描述后的内容 | 1 | 拆为精确小补丁，按真实 UXML 顺序分别替换提示和插入预设面板 |
| Unity MCP PowerShell 辅助函数把请求参数命名为保留变量 `$args` | 1 | 改用 `$arguments` 构建 `tools/call` 请求体后重发，不重复错误函数 |
| 最终检查把 PowerShell `-or` 放进 `Test-Path` 参数列表 | 1 | 给每个 `Test-Path` 调用加括号后再组合布尔表达式 |
| C# 短路条件在 `profile == null` 时未给 out 错误变量赋值 | 1 | 三个入口先初始化错误文本，再调用 `IsValid(out ...)`，不重复短路声明写法 |
| 新增测试误用不存在的 `Texture2D.GetPixel32` | 1 | 改用 Unity 现有 `GetPixels32()` 并按行宽读取目标像素 |
| Unity Test Runner 按程序集名过滤时返回 0 项 | 1 | 不把空测试计为通过；触发 Package Resolve 后运行全部 EditMode 测试，确认 20/20 |
| MCP 临时代码直接调用 UI Toolkit `Q()` 扩展无法编译 | 2 | 使用 `UQueryExtensions.Q` 静态调用，并递归统计 ObjectField |
| 现有项目保存了未列入首次兼容表的 `gemini-3.1-pro-preview` | 1 | 将该旧 ID 与旧 image preview ID 一并映射到稳定 Nano Banana Pro |

## 最终验证
| 检查 | 结果 |
|------|------|
| Unity 版本与工程 | `D:\UnityProject\MY`，Unity 6000.3.17f1 |
| 本地 UPM 安装 | `com.xiyue.ai-npc-generator@0.2.0` 成功 |
| Unity Console | 0 error |
| EditMode 测试 | 20/20 通过，0 失败、0 跳过；包含完整 Sprite Sheet→双 Prefab 测试 |
| Editor 窗口 | 菜单打开成功，720×560 |
| 最小尺寸布局 | 设置区/任务区、提示框/工具栏、相邻按钮均无重叠；任务列表保留 78px |
| UI 导入后 Console | 0 error，0 warning |
| 功能帮助 | 1 条常驻五步流程、28 条悬停说明 |
| 帮助布局 | 720×560 下流程条高 28px，设置区/任务区和按钮无重叠 |
| Gemini oneof JSON | 图片 Part 仅含 `inlineData`，文字 Part 仅含 `text` |
| Gemini 回归测试 | 定向 1/1、包内全量 10/10 通过 |
| 输入记录回归测试 | JSON 往返/去重/30 条上限均通过；包内全量 12/12 通过 |
| 极低窗口布局 | 720×440 与 1036×440 下设置区 123px、任务区 210px、任务列表 88px，无重叠或越界 |
| 草稿真实恢复 | 写入测试描述、关窗、重开后完整恢复；正式与备份 JSON 随后均恢复原草稿且无测试数据 |
| JSON/UXML/meta/密钥/diff | 全部通过 |
| 真实预设即时恢复 | 清空界面后选择现有预设，描述 15 字、指令 252 字、参考图 1 张均立即恢复；JSON 已原样还原 |
| Sprite Sheet 资产链 | 临时输入成功生成 Sprite、8 个动画、Animator、Definition、NPC/Player Prefab，并由测试清理 |
| 最终 720×440 布局 | 设置区 123px、任务区 210px、换行工具栏 48px、任务列表 88px，无重叠 |
| Nano Banana 模型显示 | 下拉框仅显示 Nano Banana 2（推荐）、Nano Banana Pro（高质量）和自定义；旧项目实际恢复为 Pro |
| Nano Banana v1 请求 | 定向契约测试 1/1 通过；Unity Console 0 error、0 warning |
