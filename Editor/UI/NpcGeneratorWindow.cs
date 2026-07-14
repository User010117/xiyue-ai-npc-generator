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
        private const string DefaultModel = "gemini-2.5-flash";
        private const string CustomModelChoice = "自定义模型…";
        private static readonly List<string> ModelChoices = new()
        {
            DefaultModel,
            "gemini-2.5-flash-lite",
            "gemini-2.5-pro",
            "gemini-3.5-flash",
            "gemini-3.1-pro-preview",
            "gemini-3-flash-preview",
            CustomModelChoice
        };
        private readonly List<NpcGenerationJob> displayedJobs = new();

        private TextField promptField;
        private TextField apiKeyField;
        private DropdownField modelDropdown;
        private TextField customModelField;
        private Toggle batchToggle;
        private IntegerField countField;
        private IntegerField concurrencyField;
        private Toggle autoNpcToggle;
        private ObjectField profileField;
        private ObjectField catalogField;
        private TextField outputField;
        private HelpBox messageBox;
        private Label summaryLabel;
        private ListView jobList;
        private NpcAiRequestHandle testHandle;

        [MenuItem("Window/Xiyue/AI NPC Generator")]
        public static void Open()
        {
            NpcGeneratorWindow window = GetWindow<NpcGeneratorWindow>();
            window.titleContent = new GUIContent("AI NPC Generator");
            window.minSize = new Vector2(720f, 560f);
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
            NpcGenerationQueue.Instance.Changed -= RefreshJobs;
            testHandle?.Abort();
            testHandle?.Dispose();
            testHandle = null;
        }

        private void QueryControls()
        {
            promptField = rootVisualElement.Q<TextField>("prompt");
            apiKeyField = rootVisualElement.Q<TextField>("api-key");
            modelDropdown = rootVisualElement.Q<DropdownField>("model-preset");
            customModelField = rootVisualElement.Q<TextField>("custom-model");
            batchToggle = rootVisualElement.Q<Toggle>("batch");
            countField = rootVisualElement.Q<IntegerField>("count");
            concurrencyField = rootVisualElement.Q<IntegerField>("concurrency");
            autoNpcToggle = rootVisualElement.Q<Toggle>("auto-npc");
            profileField = rootVisualElement.Q<ObjectField>("rig-profile");
            catalogField = rootVisualElement.Q<ObjectField>("part-catalog");
            outputField = rootVisualElement.Q<TextField>("output-root");
            messageBox = rootVisualElement.Q<HelpBox>("message");
            summaryLabel = rootVisualElement.Q<Label>("summary");
            jobList = rootVisualElement.Q<ListView>("job-list");
        }

        private void ConfigureControls()
        {
            messageBox.style.display = DisplayStyle.None;
            apiKeyField.isPasswordField = true;
            modelDropdown.choices = ModelChoices;
            modelDropdown.tooltip = "选择 Gemini 模型；列表外的模型可使用自定义模型 ID。";
            modelDropdown.RegisterValueChangedCallback(_ => UpdateModelControls());
            profileField.objectType = typeof(NpcRigProfile);
            profileField.allowSceneObjects = false;
            catalogField.objectType = typeof(NpcPartCatalog);
            catalogField.allowSceneObjects = false;
            countField.RegisterValueChangedCallback(evt => countField.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, 1, 200)));
            concurrencyField.RegisterValueChangedCallback(evt => concurrencyField.SetValueWithoutNotify(Mathf.Clamp(evt.newValue, 1, 20)));
            batchToggle.RegisterValueChangedCallback(evt => countField.SetEnabled(evt.newValue));

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
            if (ModelChoices.Contains(savedModel) && savedModel != CustomModelChoice)
            {
                modelDropdown.SetValueWithoutNotify(savedModel);
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
            outputField.value = settings.outputRoot;
            apiKeyField.value = NpcGeneratorSessionSecrets.ApiKey;
            countField.SetEnabled(batchToggle.value);
            profileField.value = AssetDatabase.LoadAssetAtPath<NpcRigProfile>(AssetDatabase.GUIDToAssetPath(settings.rigProfileGuid));
            catalogField.value = AssetDatabase.LoadAssetAtPath<NpcPartCatalog>(AssetDatabase.GUIDToAssetPath(settings.partCatalogGuid));
        }

        private void StartGeneration()
        {
            var profile = profileField.value as NpcRigProfile;
            var catalog = catalogField.value as NpcPartCatalog;
            if (string.IsNullOrWhiteSpace(promptField.value) || profile == null || catalog == null)
            {
                ShowMessage("请输入角色描述，并选择 Rig Profile 和部件库。", HelpBoxMessageType.Error);
                return;
            }

            NpcCatalogValidationReport report = NpcPartCatalogValidator.Validate(profile, catalog);
            if (!report.IsValid)
            {
                ShowMessage("部件库认证失败：\n" + string.Join("\n", report.errors), HelpBoxMessageType.Error);
                return;
            }

            string selectedModel = GetSelectedModel();
            if (string.IsNullOrWhiteSpace(selectedModel))
            {
                ShowMessage("请输入有效的自定义 Gemini 模型 ID。", HelpBoxMessageType.Error);
                return;
            }

            SaveSettings(profile, catalog);
            NpcGeneratorSessionSecrets.ApiKey = apiKeyField.value;
            int count = batchToggle.value ? Mathf.Clamp(countField.value, 1, 200) : 1;
            NpcGenerationQueue.Instance.Enqueue(
                promptField.value,
                count,
                selectedModel,
                profile,
                catalog,
                outputField.value,
                autoNpcToggle.value);
            ShowMessage($"已创建 {count} 个独立角色任务。", HelpBoxMessageType.Info);
        }

        private void CreateDemoContent()
        {
            try
            {
                NpcDemoContentCreator.CreateOrUpdate(out NpcRigProfile profile, out NpcPartCatalog catalog);
                profileField.value = profile;
                catalogField.value = catalog;
                ShowMessage("示例 Rig 和认证部件库已创建到 Assets/XiyueGenerated/Demo。", HelpBoxMessageType.Info);
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
                ShowMessage("请输入有效的自定义 Gemini 模型 ID。", HelpBoxMessageType.Error);
                return;
            }

            try
            {
                testHandle = new GeminiNpcProvider().BeginGenerate(
                    "A friendly 24 year old village baker wearing a blue apron.",
                    selectedModel,
                    NpcGeneratorSessionSecrets.ApiKey);
                ShowMessage("正在测试 Gemini；该操作会使用一次模型请求。", HelpBoxMessageType.Info);
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

            testHandle.TryGetResult(out NpcCharacterSpec spec, out long code, out string error);
            testHandle.Dispose();
            testHandle = null;
            ShowMessage(
                string.IsNullOrWhiteSpace(error) && spec != null
                    ? $"Gemini 连接成功，测试角色：{spec.displayName}"
                    : $"Gemini 测试失败{(code > 0 ? $" ({code})" : string.Empty)}：{error}",
                string.IsNullOrWhiteSpace(error) && spec != null ? HelpBoxMessageType.Info : HelpBoxMessageType.Error);
        }

        private void SaveSettings(NpcRigProfile profile, NpcPartCatalog catalog)
        {
            NpcGeneratorProjectSettings settings = NpcGeneratorProjectSettings.instance;
            settings.model = GetSelectedModel();
            settings.maxConcurrency = Mathf.Clamp(concurrencyField.value, 1, 20);
            settings.autoAddAsNpc = autoNpcToggle.value;
            settings.outputRoot = outputField.value;
            settings.rigProfileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            settings.partCatalogGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog));
            settings.Persist();
        }

        private string GetSelectedModel()
        {
            if (modelDropdown.value == CustomModelChoice)
            {
                return customModelField.value?.Trim() ?? string.Empty;
            }

            return string.IsNullOrWhiteSpace(modelDropdown.value) ? DefaultModel : modelDropdown.value.Trim();
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
                $"总计 {displayedJobs.Count}　排队 {Count(NpcGenerationStatus.Pending)}　运行 {Count(NpcGenerationStatus.GeneratingData) + Count(NpcGenerationStatus.Assembling) + Count(NpcGenerationStatus.Importing)}　" +
                $"完成 {Count(NpcGenerationStatus.Completed)}　失败/审核 {Count(NpcGenerationStatus.Failed) + Count(NpcGenerationStatus.NeedsReview)}";

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
            private readonly Label details;
            private readonly Label error;
            private readonly Button regenerate;
            private readonly Button reroll;
            private readonly Button rebuild;
            private readonly Button retry;
            private readonly Button addNpc;
            private readonly Button addPlayer;
            private Action regenerateAction;
            private Action rerollAction;
            private Action rebuildAction;
            private Action retryAction;
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
                details = new Label();
                details.AddToClassList("job-detail");
                error = new Label();
                error.AddToClassList("job-error");
                var buttons = new VisualElement();
                buttons.AddToClassList("job-buttons");
                regenerate = new Button { text = "重生成设定" };
                reroll = new Button { text = "重抽外观" };
                rebuild = new Button { text = "同种子重建" };
                retry = new Button { text = "重试" };
                addNpc = new Button { text = "添加 NPC" };
                addPlayer = new Button { text = "添加可控角色" };
                buttons.Add(regenerate);
                buttons.Add(reroll);
                buttons.Add(rebuild);
                buttons.Add(retry);
                buttons.Add(addNpc);
                buttons.Add(addPlayer);
                foldout.Add(status);
                foldout.Add(progress);
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
                    ? $"任务 {job.jobId.Substring(0, 8)}"
                    : displayName;
                status.text = $"{job.status}　{job.statusMessage}";
                progress.value = job.progress * 100f;
                progress.title = $"{Mathf.RoundToInt(job.progress * 100f)}%";
                details.text = job.character == null
                    ? $"{job.createdUtc}\n{job.prompt}"
                    : $"{job.character.age} 岁 · {job.character.gender} · {job.character.occupation} · {job.character.faction}\n" +
                      $"种子 {job.seed} · 指纹 {job.resolvedAppearance?.fingerprint ?? "待生成"}\n{job.character.biography}";
                error.text = job.error ?? string.Empty;
                bool completed = job.status == NpcGenerationStatus.Completed;
                bool hasData = job.HasCharacterData;
                addNpc.SetEnabled(completed);
                addPlayer.SetEnabled(completed);
                reroll.SetEnabled(hasData && job.status != NpcGenerationStatus.GeneratingData);
                rebuild.SetEnabled(hasData && job.status != NpcGenerationStatus.GeneratingData);
                retry.SetEnabled(job.status is NpcGenerationStatus.Failed or NpcGenerationStatus.NeedsReview or NpcGenerationStatus.Interrupted);

                regenerateAction = () => NpcGenerationQueue.Instance.RegenerateCharacterData(job.jobId);
                rerollAction = () => NpcGenerationQueue.Instance.RerollAppearance(job.jobId, false);
                rebuildAction = () => NpcGenerationQueue.Instance.RerollAppearance(job.jobId, true);
                retryAction = () => NpcGenerationQueue.Instance.Retry(job.jobId);
                addNpcAction = () => NpcGenerationQueue.Instance.AddToScene(job.jobId, false);
                addPlayerAction = () => NpcGenerationQueue.Instance.AddToScene(job.jobId, true);
                regenerate.clicked += regenerateAction;
                reroll.clicked += rerollAction;
                rebuild.clicked += rebuildAction;
                retry.clicked += retryAction;
                addNpc.clicked += addNpcAction;
                addPlayer.clicked += addPlayerAction;
            }

            private void UnbindActions()
            {
                if (regenerateAction != null) regenerate.clicked -= regenerateAction;
                if (rerollAction != null) reroll.clicked -= rerollAction;
                if (rebuildAction != null) rebuild.clicked -= rebuildAction;
                if (retryAction != null) retry.clicked -= retryAction;
                if (addNpcAction != null) addNpc.clicked -= addNpcAction;
                if (addPlayerAction != null) addPlayer.clicked -= addPlayerAction;
            }
        }
    }
}
