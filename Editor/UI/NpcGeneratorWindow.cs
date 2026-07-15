using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Xiyue.AINpcGenerator.Editor
{
    public sealed class NpcGeneratorWindow : EditorWindow
    {
        private const string PackageRoot = "Packages/com.xiyue.ai-npc-generator/Editor/UI/";
        private const string DefaultModel = SpriteSheetGenerationDefaults.ImageModel;
        /// <summary>Nano Banana Pro 的稳定 API ID；旧 preview ID 只在恢复设置时兼容。</summary>
        private const string ProModel = "gemini-3-pro-image";
        /// <summary>面向用户的推荐模型名称，避免暴露难以理解的 API ID。</summary>
        private const string NanoBanana2Choice = "Nano Banana 2（推荐）";
        /// <summary>面向用户的高质量模型名称。</summary>
        private const string NanoBananaProChoice = "Nano Banana Pro（高质量）";
        private const string CustomModelChoice = "自定义模型…";
        /// <summary>显示名与官方 API ID 的唯一映射；任务仍保存 ID 以支持请求和追溯。</summary>
        private static readonly IReadOnlyDictionary<string, string> ModelIds = new Dictionary<string, string>
        {
            [NanoBanana2Choice] = DefaultModel,
            [NanoBananaProChoice] = ProModel
        };
        private readonly List<NpcGenerationJob> displayedJobs = new();
        /// <summary>窗口只保留 Texture2D 编辑状态；持久化和队列边界统一转换为 GUID。</summary>
        private readonly List<Texture2D> referenceImages = new();
        /// <summary>与记录下拉框索引一一对应；首项为 null，代表尚未选择记录。</summary>
        private readonly List<NpcPromptRecord> selectablePromptRecords = new();
        /// <summary>用于区分可删除的命名预设和只读生成历史。</summary>
        private readonly HashSet<string> presetRecordIds = new(StringComparer.Ordinal);

        private TextField promptField;
        /// <summary>类似 Gem 的可编辑长期指令，随草稿和预设保存。</summary>
        private TextField instructionField;
        private TextField apiKeyField;
        private DropdownField modelDropdown;
        private TextField customModelField;
        private Toggle batchToggle;
        private IntegerField countField;
        private IntegerField concurrencyField;
        private Toggle autoNpcToggle;
        /// <summary>图片检查通过后是否跳过人工确认。</summary>
        private Toggle autoCompleteToggle;
        /// <summary>绿幕转透明容差。</summary>
        private Slider greenToleranceSlider;
        private ObjectField profileField;
        private TextField outputField;
        /// <summary>选择已有生成历史或命名预设。</summary>
        private DropdownField promptRecordDropdown;
        /// <summary>输入新预设名称；同名保存会更新原预设。</summary>
        private TextField presetNameField;
        /// <summary>保存当前描述与参考图为命名预设。</summary>
        private Button savePresetButton;
        /// <summary>仅删除当前选择的命名预设。</summary>
        private Button deletePresetButton;
        /// <summary>承载可选择或拖放的默认参考图槽位。</summary>
        private VisualElement referenceList;
        /// <summary>在未达到数量上限时创建新的参考图槽位。</summary>
        private Button addReferenceButton;
        private HelpBox messageBox;
        private Label summaryLabel;
        private ListView jobList;
        private SpriteSheetImageRequestHandle testHandle;
        /// <summary>窗口会话中的输入库镜像；每次有效修改后原子写回项目 JSON。</summary>
        private NpcPromptRecordDocument promptRecordDocument;

        [MenuItem("Window/Xiyue/AI NPC Generator")]
        public static void Open()
        {
            NpcGeneratorWindow window = GetWindow<NpcGeneratorWindow>();
            window.titleContent = new GUIContent("AI NPC Generator");
            // 设置区已有独立滚动且任务区保留固定操作高度，因此 440px 仍可完整操作。
            window.minSize = new Vector2(720f, 440f);
        }

        public void CreateGUI()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PackageRoot + "NpcGeneratorWindow.uxml");
            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackageRoot + "NpcGeneratorWindow.uss");
            if (tree == null)
            {
                rootVisualElement.Add(new HelpBox("Could not load the package UI. Reinstall com.xiyue.ai-npc-generator.", HelpBoxMessageType.Error));
                return;
            }

            tree.CloneTree(rootVisualElement);
            if (style != null)
            {
                rootVisualElement.styleSheets.Add(style);
            }

            QueryControls();
            ConfigureControls();
            RestoreSettings();
            NpcGenerationQueue.Instance.Changed += RefreshJobs;
            rootVisualElement.schedule.Execute(PollApiTest).Every(100);
            RefreshJobs();
        }

        private void OnDisable()
        {
            // 窗口关闭和脚本域重载都会经过这里，保存草稿可覆盖用户未点击生成的编辑内容。
            SavePromptDraft(false);
            NpcGenerationQueue.Instance.Changed -= RefreshJobs;
            testHandle?.Abort();
            testHandle?.Dispose();
            testHandle = null;
        }

        private void QueryControls()
        {
            promptField = rootVisualElement.Q<TextField>("prompt");
            instructionField = rootVisualElement.Q<TextField>("instruction");
            apiKeyField = rootVisualElement.Q<TextField>("api-key");
            modelDropdown = rootVisualElement.Q<DropdownField>("model-preset");
            customModelField = rootVisualElement.Q<TextField>("custom-model");
            batchToggle = rootVisualElement.Q<Toggle>("batch");
            countField = rootVisualElement.Q<IntegerField>("count");
            concurrencyField = rootVisualElement.Q<IntegerField>("concurrency");
            autoNpcToggle = rootVisualElement.Q<Toggle>("auto-npc");
            autoCompleteToggle = rootVisualElement.Q<Toggle>("auto-complete");
            greenToleranceSlider = rootVisualElement.Q<Slider>("green-tolerance");
            profileField = rootVisualElement.Q<ObjectField>("rig-profile");
            outputField = rootVisualElement.Q<TextField>("output-root");
            promptRecordDropdown = rootVisualElement.Q<DropdownField>("prompt-record");
            presetNameField = rootVisualElement.Q<TextField>("preset-name");
            savePresetButton = rootVisualElement.Q<Button>("save-preset");
            deletePresetButton = rootVisualElement.Q<Button>("delete-preset");
            referenceList = rootVisualElement.Q<VisualElement>("reference-list");
            addReferenceButton = rootVisualElement.Q<Button>("add-reference");
            messageBox = rootVisualElement.Q<HelpBox>("message");
            summaryLabel = rootVisualElement.Q<Label>("summary");
            jobList = rootVisualElement.Q<ListView>("job-list");
        }

        private void ConfigureControls()
        {
            messageBox.style.display = DisplayStyle.None;
            apiKeyField.isPasswordField = true;
            modelDropdown.choices = ModelIds.Keys.Append(CustomModelChoice).ToList();
            modelDropdown.tooltip = "优先选择 Nano Banana 2；复杂角色和高质量资产可使用 Nano Banana Pro。";
            modelDropdown.RegisterValueChangedCallback(_ => UpdateModelControls());
            profileField.objectType = typeof(NpcRigProfile);
            profileField.allowSceneObjects = false;
            countField.RegisterValueChangedCallback(evt => countField.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, 1, 200)));
            concurrencyField.RegisterValueChangedCallback(evt => concurrencyField.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, 1, 20)));
            batchToggle.RegisterValueChangedCallback(evt => countField.SetEnabled(evt.newValue));
            // 不在每次按键时写磁盘；输入框失焦和窗口关闭足以保证正常编辑流程中的草稿恢复。
            promptField.RegisterCallback<FocusOutEvent>(_ => SavePromptDraft(true));
            instructionField.RegisterCallback<FocusOutEvent>(_ => SavePromptDraft(true));
            greenToleranceSlider.RegisterValueChangedCallback(_ => SavePromptDraft(false));
            promptRecordDropdown.RegisterValueChangedCallback(_ =>
            {
                UpdatePromptRecordButtons();
                if (GetSelectedPromptRecord() != null) LoadSelectedPromptRecord();
            });
            rootVisualElement.Q<Button>("reset-instruction").clicked += () => instructionField.value = SpriteSheetGenerationDefaults.Instruction;
            savePresetButton.clicked += SaveCurrentPromptPreset;
            deletePresetButton.clicked += DeleteSelectedPromptPreset;
            deletePresetButton.SetEnabled(false);
            addReferenceButton.clicked += AddReferenceSlot;
            // 允许把 Project 窗口中的 Texture2D 直接拖入整个参考图区，不必先创建空槽位。
            referenceList.RegisterCallback<DragUpdatedEvent>(OnReferenceDragUpdated);
            referenceList.RegisterCallback<DragPerformEvent>(OnReferenceDragPerform);

            rootVisualElement.Q<Button>("start").clicked += StartGeneration;
            rootVisualElement.Q<Button>("pause").clicked += NpcGenerationQueue.Instance.Pause;
            rootVisualElement.Q<Button>("resume").clicked += NpcGenerationQueue.Instance.Resume;
            rootVisualElement.Q<Button>("stop").clicked += NpcGenerationQueue.Instance.CancelOutstanding;
            rootVisualElement.Q<Button>("retry-all").clicked += NpcGenerationQueue.Instance.RetryAllFailed;
            rootVisualElement.Q<Button>("clear-completed").clicked += NpcGenerationQueue.Instance.ClearCompleted;
            rootVisualElement.Q<Button>("create-demo").clicked += CreateDemoContent;
            rootVisualElement.Q<Button>("create-demo-scene").clicked += CreateDemoScene;
            rootVisualElement.Q<Button>("test-api").clicked += StartApiTest;

            jobList.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            jobList.selectionType = SelectionType.None;
            jobList.makeItem = () => new JobCard().Root;
            jobList.bindItem = (element, index) => ((JobCard)element.userData).Bind(displayedJobs[index]);
        }

        private void RestoreSettings()
        {
            NpcGeneratorProjectSettings settings = NpcGeneratorProjectSettings.instance;
            string savedModel = string.IsNullOrWhiteSpace(settings.model) ? DefaultModel : settings.model.Trim();
            string savedChoice = GetModelDisplayName(savedModel);
            if (ModelIds.ContainsKey(savedChoice))
            {
                modelDropdown.SetValueWithoutNotify(savedChoice);
                customModelField.SetValueWithoutNotify(string.Empty);
            }
            else
            {
                modelDropdown.SetValueWithoutNotify(CustomModelChoice);
                customModelField.SetValueWithoutNotify(savedModel);
            }
            UpdateModelControls();
            concurrencyField.value = settings.maxConcurrency;
            autoNpcToggle.value = settings.autoAddAsNpc;
            autoCompleteToggle.value = settings.autoCompleteNpc;
            greenToleranceSlider.SetValueWithoutNotify(settings.greenScreenTolerance <= 0f
                ? SpriteSheetGenerationDefaults.GreenScreenTolerance
                : settings.greenScreenTolerance);
            outputField.value = settings.outputRoot;
            apiKeyField.value = NpcGeneratorSessionSecrets.ApiKey;
            countField.SetEnabled(batchToggle.value);
            profileField.value = AssetDatabase.LoadAssetAtPath<NpcRigProfile>(AssetDatabase.GUIDToAssetPath(settings.rigProfileGuid));

            promptRecordDocument = NpcPromptRecordStore.Load(out string recordWarning);
            bool importedQueueHistory = false;
            if ((promptRecordDocument.history ?? Array.Empty<NpcPromptRecord>()).Length == 0)
            {
                // 首次启用记录功能时导入现有队列，让用户立刻能选择改版前已经生成过的描述。
                foreach (NpcGenerationJob job in NpcGenerationQueue.Instance.Jobs.OrderBy(job => job.createdUtc))
                {
                    importedQueueHistory |= NpcPromptRecordStore.RecordHistory(
                        promptRecordDocument,
                        job.prompt,
                        job.referenceImageGuids,
                        job.createdUtc,
                        job.instruction,
                        job.greenScreenTolerance) != null;
                }
            }
            if (importedQueueHistory)
            {
                NpcPromptRecordStore.TrySave(promptRecordDocument, out _);
            }

            NpcPromptRecord draft = promptRecordDocument.draft;
            promptField.SetValueWithoutNotify(draft?.prompt ?? string.Empty);
            instructionField.SetValueWithoutNotify(draft?.instruction ?? SpriteSheetGenerationDefaults.Instruction);
            greenToleranceSlider.SetValueWithoutNotify(draft?.greenScreenTolerance > 0f
                ? draft.greenScreenTolerance
                : settings.greenScreenTolerance);
            int missingReferenceCount = RestoreReferenceImages(
                draft != null ? draft.referenceImageGuids : settings.referenceImageGuids);
            RefreshPromptRecordChoices();

            if (missingReferenceCount > 0)
            {
                ShowMessage($"草稿中有 {missingReferenceCount} 张参考图已被移动到项目外或删除，已跳过。", HelpBoxMessageType.Warning);
            }
            else if (!string.IsNullOrWhiteSpace(recordWarning))
            {
                ShowMessage(recordWarning, HelpBoxMessageType.Warning);
            }
        }

        private void StartGeneration()
        {
            var profile = profileField.value as NpcRigProfile;
            if (string.IsNullOrWhiteSpace(promptField.value) || profile == null)
            {
                ShowMessage("请输入角色描述，并选择 Rig Profile。", HelpBoxMessageType.Error);
                return;
            }
            if (!profile.IsValid(out string profileError))
            {
                ShowMessage("Rig Profile 无效：" + profileError, HelpBoxMessageType.Error);
                return;
            }

            string selectedModel = GetSelectedModel();
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                ShowMessage("请输入有效的自定义图片模型 API ID。", HelpBoxMessageType.Error);
                return;
            }

            string[] referenceGuids;
            try
            {
                referenceGuids = GetReferenceImageGuids();
                GeminiReferenceImageLoader.Validate(referenceGuids);
            }
            catch (Exception exception)
            {
                ShowMessage("默认参考图无效：" + exception.Message, HelpBoxMessageType.Error);
                return;
            }

            try
            {
                SaveSettings(profile, referenceGuids);
                NpcGeneratorSessionSecrets.ApiKey = apiKeyField.value;
                int count = batchToggle.value ? Mathf.Clamp(countField.value, 1, 200) : 1;
                NpcPromptRecord selected = GetSelectedPromptRecord();
                string presetName = selected != null && presetRecordIds.Contains(selected.id) ? selected.name : presetNameField.value;
                NpcGenerationQueue.Instance.Enqueue(
                    promptField.value,
                    instructionField.value,
                    presetName,
                    count,
                    selectedModel,
                    profile,
                    outputField.value,
                    autoCompleteToggle.value,
                    autoNpcToggle.value,
                    greenToleranceSlider.value,
                    referenceGuids);
                NpcPromptRecordStore.SetDraft(promptRecordDocument, promptField.value, referenceGuids, instructionField.value, greenToleranceSlider.value);
                NpcPromptRecord historyRecord = NpcPromptRecordStore.RecordHistory(
                    promptRecordDocument,
                    promptField.value,
                    referenceGuids,
                    null,
                    instructionField.value,
                    greenToleranceSlider.value);
                bool savedRecord = NpcPromptRecordStore.TrySave(promptRecordDocument, out string recordError);
                RefreshPromptRecordChoices(historyRecord?.id);
                ShowMessage(
                    savedRecord ? $"已创建 {count} 个 Sprite Sheet 任务，并记录本次输入。" : $"任务已创建，但{recordError}",
                    savedRecord ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning);
            }
            catch (Exception exception)
            {
                ShowMessage("无法创建任务：" + exception.Message, HelpBoxMessageType.Error);
            }
        }

        private void CreateDemoContent()
        {
            try
            {
                NpcDemoContentCreator.CreateOrUpdate(out NpcRigProfile profile, out _);
                profileField.value = profile;
                ShowMessage("示例 Rig 已创建到 Assets/XiyueGenerated/Demo。", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                ShowMessage(exception.Message, HelpBoxMessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void CreateDemoScene()
        {
            try
            {
                NpcDemoSceneCreator.Create();
                ShowMessage("演示场景已创建到 Assets/XiyueGenerated/Demo/XiyueNpcDemo.unity。", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                ShowMessage(exception.Message, HelpBoxMessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void StartApiTest()
        {
            if (testHandle != null)
            {
                return;
            }

            NpcGeneratorSessionSecrets.ApiKey = apiKeyField.value;
            string selectedModel = GetSelectedModel();
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                ShowMessage("请输入有效的自定义图片模型 API ID。", HelpBoxMessageType.Error);
                return;
            }

            try
            {
                var profile = profileField.value as NpcRigProfile;
                if (profile == null) throw new InvalidOperationException("请先选择 Rig Profile，再测试图片模型。");
                string[] referenceGuids = GetReferenceImageGuids();
                GeminiReferenceImageLoader.Validate(referenceGuids);
                testHandle = new GeminiSpriteSheetImageProvider().BeginGenerate(new SpriteSheetImageRequest
                {
                    prompt = SpriteSheetPromptBuilder.Build(instructionField.value, promptField.value, profile, 1),
                    model = selectedModel,
                    apiKey = NpcGeneratorSessionSecrets.ApiKey,
                    referenceImageGuids = referenceGuids
                });
                ShowMessage("正在测试 Nano Banana；该操作会产生一次生图请求。", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                ShowMessage(exception.Message, HelpBoxMessageType.Error);
            }
        }

        private void PollApiTest()
        {
            if (testHandle == null || !testHandle.IsDone)
            {
                return;
            }

            testHandle.TryGetResult(out SpriteSheetImageResult result, out long code, out string error);
            testHandle.Dispose();
            testHandle = null;
            ShowMessage(
                string.IsNullOrWhiteSpace(error) && result?.bytes?.Length > 0
                    ? $"Nano Banana 连接成功，返回 {result.bytes.Length / 1024f:0.0} KB 图片。"
                    : $"Nano Banana 测试失败{(code > 0 ? $" ({code})" : string.Empty)}：{error}",
                string.IsNullOrWhiteSpace(error) && result?.bytes?.Length > 0 ? HelpBoxMessageType.Info : HelpBoxMessageType.Error);
        }

        /// <summary>
        /// 保存当前未生成草稿。关闭窗口时静默保存，主动离开描述输入框时则向用户显示写盘失败。
        /// </summary>
        private void SavePromptDraft(bool showError)
        {
            if (promptRecordDocument == null || promptField == null)
            {
                return;
            }

            try
            {
                NpcPromptRecordStore.SetDraft(
                    promptRecordDocument,
                    promptField.value,
                    GetReferenceImageGuids(),
                    instructionField.value,
                    greenToleranceSlider.value);
                if (!NpcPromptRecordStore.TrySave(promptRecordDocument, out string error) && showError)
                {
                    ShowMessage(error, HelpBoxMessageType.Warning);
                }
            }
            catch (Exception exception)
            {
                // 关闭窗口时 UI 已不可交互，只记录日志；正常编辑时给出可操作的界面提示。
                if (showError)
                {
                    ShowMessage("无法保存当前草稿：" + exception.Message, HelpBoxMessageType.Warning);
                }
                else
                {
                    Debug.LogWarning("无法保存 AI NPC 当前草稿：" + exception.Message);
                }
            }
        }

        /// <summary>把当前描述与参考图保存为命名预设；同名更新避免产生难以分辨的重复项。</summary>
        private void SaveCurrentPromptPreset()
        {
            string presetName = presetNameField.value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(presetName) || string.IsNullOrWhiteSpace(promptField.value))
            {
                ShowMessage("请输入预设名称和角色描述后再保存。", HelpBoxMessageType.Warning);
                return;
            }

            try
            {
                string[] sourceGuids = GetReferenceImageGuids();
                GeminiReferenceImageLoader.Validate(sourceGuids);
                NpcPromptRecord existing = (promptRecordDocument.presets ?? Array.Empty<NpcPromptRecord>())
                    .FirstOrDefault(item => string.Equals(item.name, presetName, StringComparison.OrdinalIgnoreCase));
                string recordId = existing?.id ?? Guid.NewGuid().ToString("N");
                // 先保存仍指向原图的有效 JSON；后续资产复制或二次写盘失败时不会留下失效记录。
                NpcPromptRecordStore.UpsertPreset(
                    promptRecordDocument,
                    presetName,
                    promptField.value,
                    sourceGuids,
                    instructionField.value,
                    greenToleranceSlider.value,
                    recordId);
                if (!NpcPromptRecordStore.TrySave(promptRecordDocument, out string error))
                {
                    ShowMessage(error, HelpBoxMessageType.Warning);
                    return;
                }

                string[] managedGuids = NpcReferenceAssetStore.ReplacePresetReferences(recordId, presetName, sourceGuids);
                NpcPromptRecord saved = NpcPromptRecordStore.UpsertPreset(
                    promptRecordDocument,
                    presetName,
                    promptField.value,
                    managedGuids,
                    instructionField.value,
                    greenToleranceSlider.value,
                    recordId);
                NpcPromptRecordStore.SetDraft(promptRecordDocument, promptField.value, managedGuids, instructionField.value, greenToleranceSlider.value);
                if (!NpcPromptRecordStore.TrySave(promptRecordDocument, out error))
                {
                    ShowMessage(error, HelpBoxMessageType.Warning);
                    return;
                }

                RestoreReferenceImages(managedGuids);
                RefreshPromptRecordChoices(saved.id);
                ShowMessage($"预设“{saved.name}”及 {managedGuids.Length} 张托管参考图已保存。", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                ShowMessage("无法保存预设：" + exception.Message, HelpBoxMessageType.Error);
            }
        }

        /// <summary>载入选择的历史或预设，并同时恢复它保存的参考图 GUID。</summary>
        private void LoadSelectedPromptRecord()
        {
            NpcPromptRecord selected = GetSelectedPromptRecord();
            if (selected == null)
            {
                ShowMessage("请先选择一条生成历史或命名预设。", HelpBoxMessageType.Warning);
                return;
            }

            promptField.SetValueWithoutNotify(selected.prompt ?? string.Empty);
            instructionField.SetValueWithoutNotify(selected.instruction ?? SpriteSheetGenerationDefaults.Instruction);
            greenToleranceSlider.SetValueWithoutNotify(selected.greenScreenTolerance > 0f
                ? selected.greenScreenTolerance
                : SpriteSheetGenerationDefaults.GreenScreenTolerance);
            int missingReferenceCount = RestoreReferenceImages(selected.referenceImageGuids);
            SavePromptDraft(false);
            ShowMessage(
                missingReferenceCount == 0
                    ? "已载入角色描述、生成指令、绿幕参数和对应参考图。"
                    : $"已载入预设；有 {missingReferenceCount} 张参考图已不存在，已跳过。",
                missingReferenceCount == 0 ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning);
        }

        /// <summary>删除当前命名预设；历史项保持只读，避免误删生成追溯记录。</summary>
        private void DeleteSelectedPromptPreset()
        {
            NpcPromptRecord selected = GetSelectedPromptRecord();
            if (selected == null || !presetRecordIds.Contains(selected.id))
            {
                ShowMessage("当前选择不是可删除的命名预设。", HelpBoxMessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("删除角色预设", $"确定删除预设“{selected.name}”吗？", "删除", "取消"))
            {
                return;
            }

            NpcPromptRecordStore.DeletePreset(promptRecordDocument, selected.id);
            if (!NpcPromptRecordStore.TrySave(promptRecordDocument, out string error))
            {
                ShowMessage(error, HelpBoxMessageType.Warning);
                return;
            }
            if (!NpcReferenceAssetStore.DeletePresetReferences(selected.id, out string deleteError))
            {
                // 资产删除失败时恢复 JSON 记录，保证用户仍能看到并再次处理该预设。
                NpcPromptRecordStore.UpsertPreset(
                    promptRecordDocument,
                    selected.name,
                    selected.prompt,
                    selected.referenceImageGuids,
                    selected.instruction,
                    selected.greenScreenTolerance,
                    selected.id);
                NpcPromptRecordStore.TrySave(promptRecordDocument, out _);
                RefreshPromptRecordChoices(selected.id);
                ShowMessage(deleteError, HelpBoxMessageType.Error);
                return;
            }

            presetNameField.SetValueWithoutNotify(string.Empty);
            RefreshPromptRecordChoices();
            ShowMessage($"预设“{selected.name}”已删除。", HelpBoxMessageType.Info);
        }

        /// <summary>重建历史/预设下拉选项，并尽量保持刚保存或刚使用的记录被选中。</summary>
        private void RefreshPromptRecordChoices(string selectedRecordId = null)
        {
            selectablePromptRecords.Clear();
            presetRecordIds.Clear();
            var choices = new List<string> { "选择历史或命名预设…" };
            selectablePromptRecords.Add(null);

            foreach (NpcPromptRecord preset in promptRecordDocument?.presets ?? Array.Empty<NpcPromptRecord>())
            {
                choices.Add("预设 · " + preset.name);
                selectablePromptRecords.Add(preset);
                presetRecordIds.Add(preset.id);
            }
            foreach (NpcPromptRecord history in promptRecordDocument?.history ?? Array.Empty<NpcPromptRecord>())
            {
                string savedTime = DateTime.TryParse(history.savedUtc, out DateTime parsed)
                    ? parsed.ToLocalTime().ToString("MM-dd HH:mm")
                    : "未知时间";
                choices.Add($"历史 · {savedTime} · {CreatePromptSummary(history.prompt)}");
                selectablePromptRecords.Add(history);
            }

            promptRecordDropdown.choices = choices;
            int selectedIndex = string.IsNullOrWhiteSpace(selectedRecordId)
                ? 0
                : selectablePromptRecords.FindIndex(record => record?.id == selectedRecordId);
            selectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
            promptRecordDropdown.SetValueWithoutNotify(choices[selectedIndex]);
            UpdatePromptRecordButtons();
        }

        /// <summary>按下拉框当前值取得平行记录，避免依赖可能重复的描述文字。</summary>
        private NpcPromptRecord GetSelectedPromptRecord()
        {
            int selectedIndex = promptRecordDropdown.choices.IndexOf(promptRecordDropdown.value);
            return selectedIndex >= 0 && selectedIndex < selectablePromptRecords.Count
                ? selectablePromptRecords[selectedIndex]
                : null;
        }

        /// <summary>只有命名预设允许删除；选中预设时把名称带入输入框，便于直接更新。</summary>
        private void UpdatePromptRecordButtons()
        {
            NpcPromptRecord selected = GetSelectedPromptRecord();
            bool isPreset = selected != null && presetRecordIds.Contains(selected.id);
            deletePresetButton.SetEnabled(isPreset);
            if (isPreset)
            {
                presetNameField.SetValueWithoutNotify(selected.name);
            }
        }

        /// <summary>把长描述压缩成单行下拉摘要，完整内容仍由记录对象保留。</summary>
        private static string CreatePromptSummary(string prompt)
        {
            string summary = (prompt ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return summary.Length <= 28 ? summary : summary.Substring(0, 28) + "…";
        }

        /// <summary>按 GUID 恢复参考图；丢失资产不会阻止载入其他仍有效的图片。</summary>
        private int RestoreReferenceImages(IReadOnlyList<string> referenceImageGuids)
        {
            referenceImages.Clear();
            int missingCount = 0;
            foreach (string guid in (referenceImageGuids ?? Array.Empty<string>())
                         .Where(guid => !string.IsNullOrWhiteSpace(guid))
                         .Distinct(StringComparer.Ordinal)
                         .Take(GeminiReferenceImageLoader.MaxImageCount))
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
                if (texture == null)
                {
                    missingCount++;
                    continue;
                }
                referenceImages.Add(texture);
            }
            RefreshReferenceImages();
            return missingCount;
        }

        /// <summary>
        /// 保存窗口配置；参考图只写 GUID，图片内容仍由 Unity 资产数据库管理。
        /// </summary>
        private void SaveSettings(NpcRigProfile profile, string[] referenceGuids)
        {
            NpcGeneratorProjectSettings settings = NpcGeneratorProjectSettings.instance;
            settings.model = GetSelectedModel();
            settings.maxConcurrency = Mathf.Clamp(concurrencyField.value, 1, 20);
            settings.autoAddAsNpc = autoNpcToggle.value;
            settings.autoCompleteNpc = autoCompleteToggle.value;
            settings.greenScreenTolerance = greenToleranceSlider.value;
            settings.outputRoot = outputField.value;
            settings.rigProfileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            settings.referenceImageGuids = referenceGuids ?? Array.Empty<string>();
            settings.Persist();
        }

        /// <summary>
        /// 新增一个可拖放/选择 Texture2D 的空槽；数量上限由共享加载器统一定义。
        /// </summary>
        private void AddReferenceSlot()
        {
            if (referenceImages.Count >= GeminiReferenceImageLoader.MaxImageCount)
            {
                ShowMessage("默认参考图最多 6 张。", HelpBoxMessageType.Warning);
                return;
            }

            referenceImages.Add(null);
            RefreshReferenceImages();
        }

        /// <summary>
        /// 重建参考图行；列表最多六项，直接重建比维护复杂的虚拟化绑定更可靠。
        /// </summary>
        private void RefreshReferenceImages()
        {
            referenceList.Clear();
            if (referenceImages.Count == 0)
            {
                referenceList.Add(new Label("暂无参考图，可拖入 Project 中的 PNG/JPEG 纹理。") { pickingMode = PickingMode.Ignore });
            }

            for (int index = 0; index < referenceImages.Count; index++)
            {
                int capturedIndex = index;
                Texture2D current = referenceImages[index];
                var row = new VisualElement();
                row.AddToClassList("reference-row");
                var field = new ObjectField($"参考图 {index + 1}")
                {
                    objectType = typeof(Texture2D),
                    allowSceneObjects = false,
                    value = current
                };
                field.RegisterValueChangedCallback(evt => ChangeReferenceImage(capturedIndex, evt.newValue as Texture2D, current, field));
                var remove = new Button(() =>
                {
                    referenceImages.RemoveAt(capturedIndex);
                    RefreshReferenceImages();
                    SavePromptDraft(true);
                }) { text = "移除" };
                row.Add(field);
                row.Add(remove);
                referenceList.Add(row);
            }

            addReferenceButton.SetEnabled(referenceImages.Count < GeminiReferenceImageLoader.MaxImageCount);
        }

        /// <summary>
        /// 应用单项修改并立即验证全部参考图；失败时回滚原值，不让无效状态进入设置文件。
        /// </summary>
        private void ChangeReferenceImage(int index, Texture2D next, Texture2D previous, ObjectField field)
        {
            if (next == null)
            {
                referenceImages.RemoveAt(index);
                RefreshReferenceImages();
                SavePromptDraft(true);
                return;
            }
            if (referenceImages.Where((texture, itemIndex) => itemIndex != index).Contains(next))
            {
                field.SetValueWithoutNotify(previous);
                ShowMessage("同一张默认参考图不需要重复添加。", HelpBoxMessageType.Warning);
                return;
            }

            referenceImages[index] = next;
            try
            {
                GeminiReferenceImageLoader.Validate(GetReferenceImageGuids());
                RefreshReferenceImages();
                SavePromptDraft(true);
            }
            catch (Exception exception)
            {
                referenceImages[index] = previous;
                field.SetValueWithoutNotify(previous);
                ShowMessage("默认参考图无效：" + exception.Message, HelpBoxMessageType.Error);
            }
        }

        /// <summary>
        /// 拖动 Texture2D 到参考图区时显示复制反馈；其他对象保持默认拒绝状态。
        /// </summary>
        private void OnReferenceDragUpdated(DragUpdatedEvent evt)
        {
            // 空槽不占用实际图片名额，达到六行时仍允许用户把图片拖入空槽。
            bool hasAvailableSlot = referenceImages.Contains(null) ||
                                    referenceImages.Count < GeminiReferenceImageLoader.MaxImageCount;
            if (hasAvailableSlot &&
                DragAndDrop.objectReferences.Any(item => item is Texture2D))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            }
        }

        /// <summary>
        /// 批量接收拖入纹理，并在一次统一校验失败时回滚整个拖放操作。
        /// </summary>
        private void OnReferenceDragPerform(DragPerformEvent evt)
        {
            var previous = new List<Texture2D>(referenceImages);
            foreach (Texture2D texture in DragAndDrop.objectReferences.OfType<Texture2D>())
            {
                if (referenceImages.Contains(texture)) continue;

                // 优先填充用户已创建的空槽；只有没有空槽时才占用新的数量名额。
                int emptyIndex = referenceImages.IndexOf(null);
                if (emptyIndex >= 0) referenceImages[emptyIndex] = texture;
                else if (referenceImages.Count < GeminiReferenceImageLoader.MaxImageCount) referenceImages.Add(texture);
                else break;
            }

            try
            {
                GeminiReferenceImageLoader.Validate(GetReferenceImageGuids());
                DragAndDrop.AcceptDrag();
                evt.StopPropagation();
                RefreshReferenceImages();
                SavePromptDraft(true);
            }
            catch (Exception exception)
            {
                referenceImages.Clear();
                referenceImages.AddRange(previous);
                RefreshReferenceImages();
                ShowMessage("默认参考图无效：" + exception.Message, HelpBoxMessageType.Error);
            }
        }

        /// <summary>
        /// 把当前 Texture2D 列表转换为稳定 GUID；非项目资产会明确失败而不是静默丢失。
        /// </summary>
        private string[] GetReferenceImageGuids()
        {
            var guids = new List<string>();
            foreach (Texture2D texture in referenceImages.Where(texture => texture != null))
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(texture));
                if (string.IsNullOrWhiteSpace(guid))
                {
                    throw new InvalidOperationException($"参考图 '{texture.name}' 必须是 Project 中的图片资产。");
                }
                if (!guids.Contains(guid))
                {
                    guids.Add(guid);
                }
            }
            return guids.ToArray();
        }

        private string GetSelectedModel()
        {
            return GetModelId(modelDropdown.value, customModelField.value);
        }

        /// <summary>把界面显示名转换为接口模型 ID；自定义值原样去除首尾空白。</summary>
        internal static string GetModelId(string choice, string customModel)
        {
            if (ModelIds.TryGetValue(choice ?? string.Empty, out string modelId)) return modelId;
            return choice == CustomModelChoice ? customModel?.Trim() ?? string.Empty : DefaultModel;
        }

        /// <summary>把稳定或旧版模型 ID转换为用户可理解的 Nano Banana 名称。</summary>
        internal static string GetModelDisplayName(string modelId)
        {
            string normalized = modelId is "gemini-3-pro-image-preview" or "gemini-3.1-pro-preview"
                ? ProModel
                : modelId;
            return ModelIds.FirstOrDefault(pair => string.Equals(pair.Value, normalized, StringComparison.Ordinal)).Key
                ?? modelId;
        }

        private void UpdateModelControls()
        {
            bool useCustomModel = modelDropdown.value == CustomModelChoice;
            customModelField.style.display = useCustomModel ? DisplayStyle.Flex : DisplayStyle.None;
            customModelField.SetEnabled(useCustomModel);
        }

        private void RefreshJobs()
        {
            displayedJobs.Clear();
            displayedJobs.AddRange(NpcGenerationQueue.Instance.Jobs.Reverse());
            jobList.itemsSource = displayedJobs;
            jobList.Rebuild();

            var groups = NpcGenerationQueue.Instance.Jobs.GroupBy(job => job.status).ToDictionary(group => group.Key, group => group.Count());
            int Count(NpcGenerationStatus status) => groups.TryGetValue(status, out int value) ? value : 0;
            summaryLabel.text =
                $"总计 {displayedJobs.Count}　排队 {Count(NpcGenerationStatus.Pending)}　运行 {Count(NpcGenerationStatus.GeneratingImage) + Count(NpcGenerationStatus.ConvertingSpriteSheet) + Count(NpcGenerationStatus.Importing)}　" +
                $"待确认 {Count(NpcGenerationStatus.AwaitingImageReview)}　完成 {Count(NpcGenerationStatus.Completed)}　失败/审核 {Count(NpcGenerationStatus.Failed) + Count(NpcGenerationStatus.NeedsReview)}";

            if (!string.IsNullOrWhiteSpace(NpcGenerationQueue.Instance.LastQueueError))
            {
                ShowMessage(NpcGenerationQueue.Instance.LastQueueError, HelpBoxMessageType.Warning);
            }
        }

        private void ShowMessage(string message, HelpBoxMessageType type)
        {
            messageBox.text = message ?? string.Empty;
            messageBox.messageType = type;
            messageBox.style.display = string.IsNullOrWhiteSpace(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private sealed class JobCard
        {
            private readonly Foldout foldout;
            private readonly Label status;
            private readonly ProgressBar progress;
            /// <summary>显示已保存的原始 Sprite Sheet，域重载后仍可由资产路径恢复。</summary>
            private readonly Image preview;
            private readonly Label details;
            private readonly Label error;
            private readonly Button confirm;
            private readonly Button regenerate;
            private readonly Button addNpc;
            private readonly Button addPlayer;
            private Action confirmAction;
            private Action regenerateAction;
            private Action addNpcAction;
            private Action addPlayerAction;

            public JobCard()
            {
                Root = new VisualElement();
                Root.AddToClassList("job-card");
                Root.userData = this;
                foldout = new Foldout();
                foldout.AddToClassList("job-title");
                status = new Label();
                progress = new ProgressBar { lowValue = 0f, highValue = 100f };
                preview = new Image { scaleMode = ScaleMode.ScaleToFit };
                preview.AddToClassList("job-preview");
                details = new Label();
                details.AddToClassList("job-detail");
                error = new Label();
                error.AddToClassList("job-error");
                var buttons = new VisualElement();
                buttons.AddToClassList("job-buttons");
                confirm = new Button { text = "确认并生成 NPC" };
                confirm.AddToClassList("primary");
                regenerate = new Button { text = "重新生成 Sprite Sheet" };
                addNpc = new Button { text = "添加 NPC" };
                addPlayer = new Button { text = "添加可控角色" };
                buttons.Add(confirm);
                buttons.Add(regenerate);
                buttons.Add(addNpc);
                buttons.Add(addPlayer);
                foldout.Add(status);
                foldout.Add(progress);
                foldout.Add(preview);
                foldout.Add(details);
                foldout.Add(error);
                foldout.Add(buttons);
                Root.Add(foldout);
            }

            public VisualElement Root { get; }

            public void Bind(NpcGenerationJob job)
            {
                UnbindActions();
                string displayName = job.character?.displayName;
                foldout.text = string.IsNullOrWhiteSpace(displayName)
                    ? $"任务 {job.ShortJobId}"
                    : displayName;
                status.text = $"{job.status}　{job.statusMessage}";
                progress.value = job.progress * 100f;
                progress.title = $"{Mathf.RoundToInt(job.progress * 100f)}%";
                Texture2D previewTexture = string.IsNullOrWhiteSpace(job.generatedSpriteSheetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(job.generatedSpriteSheetPath);
                preview.image = previewTexture;
                preview.style.display = previewTexture == null ? DisplayStyle.None : DisplayStyle.Flex;
                details.text = $"{job.createdUtc}\n模型 {GetModelDisplayName(job.model)} · 种子 {job.seed} · 指纹 {job.resolvedAppearance?.fingerprint ?? "待生成"}\n{job.prompt}";
                error.text = job.error ?? string.Empty;
                bool completed = job.status == NpcGenerationStatus.Completed;
                bool canConfirm = previewTexture != null && job.status is NpcGenerationStatus.AwaitingImageReview or NpcGenerationStatus.NeedsReview;
                bool canRegenerate = !completed && job.status is NpcGenerationStatus.AwaitingImageReview or NpcGenerationStatus.NeedsReview or NpcGenerationStatus.Failed or NpcGenerationStatus.Interrupted;
                confirm.style.display = canConfirm ? DisplayStyle.Flex : DisplayStyle.None;
                regenerate.style.display = canRegenerate ? DisplayStyle.Flex : DisplayStyle.None;
                addNpc.SetEnabled(completed);
                addPlayer.SetEnabled(completed);
                addNpc.style.display = completed ? DisplayStyle.Flex : DisplayStyle.None;
                addPlayer.style.display = completed ? DisplayStyle.Flex : DisplayStyle.None;

                confirmAction = () => NpcGenerationQueue.Instance.ConfirmImage(job.jobId);
                regenerateAction = () => NpcGenerationQueue.Instance.RegenerateImage(job.jobId);
                addNpcAction = () => NpcGenerationQueue.Instance.AddToScene(job.jobId, false);
                addPlayerAction = () => NpcGenerationQueue.Instance.AddToScene(job.jobId, true);
                confirm.clicked += confirmAction;
                regenerate.clicked += regenerateAction;
                addNpc.clicked += addNpcAction;
                addPlayer.clicked += addPlayerAction;
            }

            private void UnbindActions()
            {
                if (confirmAction != null) confirm.clicked -= confirmAction;
                if (regenerateAction != null) regenerate.clicked -= regenerateAction;
                if (addNpcAction != null) addNpc.clicked -= addNpcAction;
                if (addPlayerAction != null) addPlayer.clicked -= addPlayerAction;
            }
        }
    }
}
