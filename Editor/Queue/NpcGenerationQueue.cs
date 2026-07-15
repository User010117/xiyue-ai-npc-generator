using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    /// <summary>持久化处理 Gemini 图片请求、人工确认和 Unity 本地资产转换。</summary>
    [InitializeOnLoad]
    public sealed class NpcGenerationQueue
    {
        /// <summary>全编辑器共享一条队列，域重载时由 JSON 恢复。</summary>
        private static readonly NpcGenerationQueue instance = new();
        /// <summary>当前项目的全部任务。</summary>
        private readonly List<NpcGenerationJob> jobs = new();
        /// <summary>正在执行的网络请求；API Key 不进入该字典键值之外的持久化对象。</summary>
        private readonly Dictionary<string, SpriteSheetImageRequestHandle> activeRequests = new();
        /// <summary>默认图片 Provider；测试可替换为无网络实现。</summary>
        private ISpriteSheetImageProvider provider = new GeminiSpriteSheetImageProvider();
        /// <summary>暂停只阻止领取新任务，不强制中断已经发送的请求。</summary>
        private bool paused;
        /// <summary>防止域重载与退出重复释放请求。</summary>
        private bool shuttingDown;
        /// <summary>请求错误与持久化错误分开保存。</summary>
        private string lastRequestError = string.Empty;
        /// <summary>最近一次队列写盘错误。</summary>
        private string lastPersistenceError = string.Empty;
        /// <summary>限制网络进度 UI 刷新频率。</summary>
        private double lastUiPulse;

        /// <summary>确保 Unity 加载 Editor 程序集时构造单例。</summary>
        static NpcGenerationQueue() => _ = instance;

        /// <summary>恢复队列、迁移旧任务并注册 Editor 生命周期。</summary>
        private NpcGenerationQueue()
        {
            jobs.AddRange(NpcGenerationQueueStore.Load());
            foreach (NpcGenerationJob job in jobs)
            {
                if (!string.Equals(job.workflowVersion, "2.0", StringComparison.Ordinal))
                {
                    if (job.status is not (NpcGenerationStatus.Completed or NpcGenerationStatus.Cancelled or NpcGenerationStatus.Failed))
                    {
                        job.status = NpcGenerationStatus.NeedsReview;
                        job.statusMessage = "旧版部件任务无法继续，请使用当前设置重新创建";
                        job.error = "该任务来自 1.x 确定性部件流程；已完成资产仍保留。";
                    }
                    continue;
                }
                if (IsActiveStatus(job))
                {
                    job.status = NpcGenerationStatus.Interrupted;
                    job.statusMessage = "编辑器重载中断了任务，可安全继续";
                }
            }
            Persist();
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;
            EditorApplication.quitting += ShutdownForReload;
        }

        /// <summary>当前项目队列单例。</summary>
        public static NpcGenerationQueue Instance => instance;
        /// <summary>只读任务列表。</summary>
        public IReadOnlyList<NpcGenerationJob> Jobs => jobs;
        /// <summary>队列是否暂停领取新任务。</summary>
        public bool IsPaused => paused;
        /// <summary>优先展示会导致数据丢失风险的持久化错误。</summary>
        public string LastQueueError => string.IsNullOrWhiteSpace(lastPersistenceError) ? lastRequestError : lastPersistenceError;
        /// <summary>任务或进度变化事件。</summary>
        public event Action Changed;

        /// <summary>测试注入点；不引入额外工厂。</summary>
        public void SetProviderForTests(ISpriteSheetImageProvider value) => provider = value ?? throw new ArgumentNullException(nameof(value));

        /// <summary>创建一批冻结输入的图片任务；参考图整个批次只复制一次。</summary>
        public string Enqueue(
            string prompt,
            string instruction,
            string presetName,
            int count,
            string model,
            NpcRigProfile profile,
            string outputRoot,
            bool autoCompleteNpc,
            bool autoAddAsNpc,
            float greenScreenTolerance,
            IReadOnlyList<string> referenceImageGuids)
        {
            if (string.IsNullOrWhiteSpace(prompt)) throw new ArgumentException("角色描述不能为空。", nameof(prompt));
            string profileError = "未选择 Rig Profile";
            if (profile == null || !profile.IsValid(out profileError)) throw new ArgumentException("Rig Profile 无效：" + profileError, nameof(profile));
            count = Mathf.Clamp(count, 1, 200);
            string batchId = Guid.NewGuid().ToString("N");
            string profileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            if (string.IsNullOrWhiteSpace(profileGuid)) throw new InvalidOperationException("Rig Profile 必须是 Project 中已保存的资产。");
            string[] frozenReferences = NpcReferenceAssetStore.SnapshotBatchReferences(batchId, referenceImageGuids);
            int baseSeed = unchecked((int)DateTime.UtcNow.Ticks);
            for (int index = 0; index < count; index++)
            {
                int seed = unchecked(baseSeed + index * 7919);
                jobs.Add(new NpcGenerationJob
                {
                    workflowVersion = "2.0",
                    jobId = Guid.NewGuid().ToString("N"),
                    batchId = batchId,
                    createdUtc = DateTime.UtcNow.ToString("O"),
                    prompt = prompt.Trim(),
                    instruction = string.IsNullOrWhiteSpace(instruction) ? SpriteSheetGenerationDefaults.Instruction : instruction.Trim(),
                    presetName = presetName?.Trim() ?? string.Empty,
                    model = string.IsNullOrWhiteSpace(model) ? SpriteSheetGenerationDefaults.ImageModel : model.Trim(),
                    seed = seed,
                    status = NpcGenerationStatus.Pending,
                    statusMessage = "等待生成 Sprite Sheet",
                    profileGuid = profileGuid,
                    referenceImageGuids = (string[])frozenReferences.Clone(),
                    outputRoot = outputRoot,
                    autoCompleteNpc = autoCompleteNpc,
                    autoAddAsNpc = autoAddAsNpc,
                    greenScreenTolerance = Mathf.Clamp(greenScreenTolerance, 0.05f, 0.8f),
                    character = NpcLocalCharacterFactory.Create(prompt, presetName)
                });
            }
            paused = false;
            SetRequestError(string.Empty);
            NotifyAndPersist();
            return batchId;
        }

        /// <summary>暂停尚未开始的任务。</summary>
        public void Pause()
        {
            paused = true;
            foreach (NpcGenerationJob job in jobs.Where(job => job.status == NpcGenerationStatus.Pending))
            {
                job.status = NpcGenerationStatus.Paused;
                job.statusMessage = "已暂停，尚未请求图片";
            }
            NotifyAndPersist();
        }

        /// <summary>恢复暂停或重载中断的任务；已有图片时回到预览，避免重复计费。</summary>
        public void Resume()
        {
            paused = false;
            foreach (NpcGenerationJob job in jobs.Where(job => job.status is NpcGenerationStatus.Paused or NpcGenerationStatus.Interrupted))
            {
                bool hasPreview = !string.IsNullOrWhiteSpace(job.generatedSpriteSheetPath) && AssetDatabase.LoadAssetAtPath<Texture2D>(job.generatedSpriteSheetPath) != null;
                job.status = hasPreview ? NpcGenerationStatus.AwaitingImageReview : NpcGenerationStatus.Pending;
                job.statusMessage = hasPreview ? "图片已恢复，请确认生成 NPC" : "已恢复，等待生成图片";
                job.error = hasPreview ? job.imageValidationWarning : string.Empty;
            }
            NotifyAndPersist();
        }

        /// <summary>取消正在请求和尚未执行的任务，已保存预览与成品资产不删除。</summary>
        public void CancelOutstanding()
        {
            foreach (KeyValuePair<string, SpriteSheetImageRequestHandle> pair in activeRequests.ToArray())
            {
                pair.Value.Abort();
                pair.Value.Dispose();
                NpcGenerationJob job = jobs.FirstOrDefault(item => item.jobId == pair.Key);
                if (job != null) { job.status = NpcGenerationStatus.Cancelled; job.statusMessage = "用户已取消"; }
            }
            activeRequests.Clear();
            foreach (NpcGenerationJob job in jobs.Where(job => job.status is NpcGenerationStatus.Pending or NpcGenerationStatus.Paused or NpcGenerationStatus.RetryWaiting))
            {
                job.status = NpcGenerationStatus.Cancelled;
                job.statusMessage = "用户已取消";
            }
            NotifyAndPersist();
        }

        /// <summary>确认当前预览并进入本地 NPC 转换；人工确认允许覆盖自动检测警告。</summary>
        public void ConfirmImage(string jobId)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(item => item.jobId == jobId);
            if (job == null || string.IsNullOrWhiteSpace(job.generatedSpriteSheetPath)) return;
            job.error = string.Empty;
            job.status = NpcGenerationStatus.ConvertingSpriteSheet;
            job.statusMessage = "已确认图片，准备生成 NPC";
            NotifyAndPersist();
        }

        /// <summary>丢弃旧预览并重新请求图片；不会改变预设和批次参考图快照。</summary>
        public void RegenerateImage(string jobId)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(item => item.jobId == jobId);
            if (job == null || activeRequests.ContainsKey(jobId) || job.status == NpcGenerationStatus.Completed) return;
            if (!string.IsNullOrWhiteSpace(job.generatedSpriteSheetPath)) AssetDatabase.DeleteAsset(job.generatedSpriteSheetPath);
            job.generatedSpriteSheetPath = string.Empty;
            job.imageValidationWarning = string.Empty;
            job.error = string.Empty;
            job.retryCount = 0;
            job.seed = unchecked(job.seed + 104729);
            job.progress = 0f;
            job.status = NpcGenerationStatus.Pending;
            job.statusMessage = "等待重新生成 Sprite Sheet";
            NotifyAndPersist();
        }

        /// <summary>兼容旧 UI 调用名，失败任务重新生成图片。</summary>
        public void Retry(string jobId) => RegenerateImage(jobId);

        /// <summary>重试全部失败、待检查或中断且没有可用预览的任务。</summary>
        public void RetryAllFailed()
        {
            foreach (NpcGenerationJob job in jobs.Where(job => job.status is NpcGenerationStatus.Failed or NpcGenerationStatus.Interrupted))
                RegenerateImageWithoutNotify(job);
            NotifyAndPersist();
        }

        /// <summary>清理完成/取消记录；保留输入快照，避免已生成清单中的参考图 GUID 失效。</summary>
        public void ClearCompleted()
        {
            jobs.RemoveAll(job => job.status is NpcGenerationStatus.Completed or NpcGenerationStatus.Cancelled);
            NotifyAndPersist();
        }

        /// <summary>把已完成的 NPC 或 Player Prefab 添加到当前场景。</summary>
        public void AddToScene(string jobId, bool asPlayer)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(item => item.jobId == jobId);
            if (job != null && !string.IsNullOrWhiteSpace(job.outputAssetPath)) NpcGenerationPipeline.AddGeneratedPrefabToScene(job, asPlayer);
        }

        /// <summary>Editor update 驱动网络轮询和单个本地转换，避免使用无必要 Update MonoBehaviour。</summary>
        private void Tick()
        {
            if (shuttingDown) return;
            PollRequests();
            PromoteRetries();
            if (paused) return;
            ProcessOneLocalJob();
            StartRequests();
        }

        /// <summary>按并发上限领取 Pending 图片任务。</summary>
        private void StartRequests()
        {
            string apiKey = NpcGeneratorSessionSecrets.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SetRequestError(jobs.Any(job => job.status == NpcGenerationStatus.Pending)
                    ? "请输入 Gemini API Key 或设置 GEMINI_API_KEY 后再生成。"
                    : string.Empty);
                return;
            }
            SetRequestError(string.Empty);
            int concurrency = Mathf.Clamp(NpcGeneratorProjectSettings.instance.maxConcurrency, 1, 20);
            while (activeRequests.Count < concurrency)
            {
                NpcGenerationJob job = jobs.FirstOrDefault(item => item.status == NpcGenerationStatus.Pending);
                if (job == null) break;
                try
                {
                    NpcRigProfile profile = LoadProfile(job);
                    string prompt = SpriteSheetPromptBuilder.Build(job.instruction, job.prompt, profile, job.seed);
                    SpriteSheetImageRequestHandle handle = provider.BeginGenerate(new SpriteSheetImageRequest
                    {
                        prompt = prompt,
                        model = job.model,
                        apiKey = apiKey,
                        referenceImageGuids = job.referenceImageGuids ?? Array.Empty<string>()
                    });
                    activeRequests.Add(job.jobId, handle);
                    job.status = NpcGenerationStatus.GeneratingImage;
                    job.statusMessage = "Gemini 正在生成 Sprite Sheet";
                    job.progress = 0.05f;
                    NotifyAndPersist();
                }
                catch (Exception exception)
                {
                    job.status = NpcGenerationStatus.Failed;
                    job.error = exception.Message;
                    job.statusMessage = "无法启动图片请求";
                    NotifyAndPersist();
                }
            }
        }

        /// <summary>消费完成的图片请求，保存 PNG 并决定等待确认还是自动转换。</summary>
        private void PollRequests()
        {
            foreach (KeyValuePair<string, SpriteSheetImageRequestHandle> pair in activeRequests.ToArray())
            {
                NpcGenerationJob job = jobs.First(item => item.jobId == pair.Key);
                SpriteSheetImageRequestHandle handle = pair.Value;
                job.progress = Mathf.Lerp(0.05f, 0.48f, handle.Progress);
                if (!handle.IsDone) continue;
                handle.TryGetResult(out SpriteSheetImageResult result, out long statusCode, out string error);
                handle.Dispose();
                activeRequests.Remove(pair.Key);
                if (!string.IsNullOrWhiteSpace(error) || result?.bytes == null)
                {
                    HandleRequestFailure(job, statusCode, error);
                    continue;
                }
                try
                {
                    job.status = NpcGenerationStatus.SavingPreview;
                    job.statusMessage = "正在保存并检查 Sprite Sheet";
                    job.generatedSpriteSheetPath = SpriteSheetImageProcessor.SavePreview(job, result.bytes);
                    job.imageValidationWarning = SpriteSheetImageProcessor.Inspect(job.generatedSpriteSheetPath, LoadProfile(job), job.greenScreenTolerance);
                    bool canAutoComplete = job.autoCompleteNpc && string.IsNullOrWhiteSpace(job.imageValidationWarning);
                    job.status = canAutoComplete ? NpcGenerationStatus.ConvertingSpriteSheet : NpcGenerationStatus.AwaitingImageReview;
                    job.statusMessage = canAutoComplete ? "图片验证通过，准备自动生成 NPC" : "Sprite Sheet 已生成，请预览确认";
                    job.error = job.imageValidationWarning;
                    job.progress = canAutoComplete ? 0.52f : 0.5f;
                }
                catch (Exception exception)
                {
                    job.status = NpcGenerationStatus.Failed;
                    job.statusMessage = "保存或检查图片失败";
                    job.error = exception.Message;
                }
                NotifyAndPersist();
            }
            if (activeRequests.Count > 0 && EditorApplication.timeSinceStartup - lastUiPulse >= 0.1d)
            {
                lastUiPulse = EditorApplication.timeSinceStartup;
                Changed?.Invoke();
            }
        }

        /// <summary>每帧最多处理一个本地转换任务，避免多个 AssetDatabase 事务互相干扰。</summary>
        private void ProcessOneLocalJob()
        {
            NpcGenerationJob job = jobs.FirstOrDefault(item => item.status == NpcGenerationStatus.ConvertingSpriteSheet);
            if (job == null) return;
            try
            {
                NpcGeneratedAssets generated = NpcGenerationPipeline.GenerateFromSpriteSheet(job, LoadProfile(job), OnProgress);
                job.resolvedAppearance = generated.resolvedAppearance;
                job.outputAssetPath = generated.definitionPath;
                job.status = NpcGenerationStatus.Completed;
                job.statusMessage = "NPC、动画和预制体已生成并通过检查";
                job.progress = 1f;
                if (job.autoAddAsNpc) NpcGenerationPipeline.AddGeneratedPrefabToScene(job, false);
            }
            catch (NpcNeedsReviewException exception)
            {
                job.status = NpcGenerationStatus.NeedsReview;
                job.statusMessage = "转换结果需要人工确认后重试";
                job.error = exception.Message;
            }
            catch (Exception exception)
            {
                job.status = NpcGenerationStatus.Failed;
                job.statusMessage = "本地 NPC 生成失败";
                job.error = exception.Message;
                Debug.LogException(exception);
            }
            NotifyAndPersist();

            void OnProgress(NpcGenerationStatus status, float progress, string message)
            {
                job.status = status;
                job.progress = progress;
                job.statusMessage = message;
                Changed?.Invoke();
            }
        }

        /// <summary>到达退避时间后重新排队。</summary>
        private void PromoteRetries()
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            foreach (NpcGenerationJob job in jobs.Where(job => job.status == NpcGenerationStatus.RetryWaiting && job.retryAtEpochSeconds <= now))
            {
                job.status = NpcGenerationStatus.Pending;
                job.statusMessage = "正在重试图片请求";
            }
        }

        /// <summary>只对网络超时、限流和服务端错误执行最多三次退避重试。</summary>
        private void HandleRequestFailure(NpcGenerationJob job, long statusCode, string error)
        {
            bool retryable = statusCode == 0 || statusCode == 408 || statusCode == 429 || statusCode >= 500;
            if (retryable && job.retryCount < 3)
            {
                job.retryCount++;
                job.retryAtEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d + Math.Pow(2d, job.retryCount) + UnityEngine.Random.Range(0.1f, 0.8f);
                job.status = NpcGenerationStatus.RetryWaiting;
                job.statusMessage = $"图片请求将在稍后重试（{job.retryCount}/3）";
                job.error = error;
            }
            else
            {
                job.status = NpcGenerationStatus.Failed;
                job.statusMessage = statusCode > 0 ? $"Gemini 图片请求失败（{statusCode}）" : "Gemini 图片请求失败";
                job.error = string.IsNullOrWhiteSpace(error) ? "未知 Gemini 请求错误。" : error;
            }
            NotifyAndPersist();
        }

        /// <summary>载入任务冻结的 Rig Profile。</summary>
        private static NpcRigProfile LoadProfile(NpcGenerationJob job)
        {
            NpcRigProfile profile = AssetDatabase.LoadAssetAtPath<NpcRigProfile>(AssetDatabase.GUIDToAssetPath(job.profileGuid));
            if (profile == null) throw new InvalidOperationException("任务保存的 Rig Profile 已不存在。");
            return profile;
        }

        /// <summary>不触发多次写盘的内部重排。</summary>
        private static void RegenerateImageWithoutNotify(NpcGenerationJob job)
        {
            if (!string.IsNullOrWhiteSpace(job.generatedSpriteSheetPath)) AssetDatabase.DeleteAsset(job.generatedSpriteSheetPath);
            job.generatedSpriteSheetPath = string.Empty;
            job.imageValidationWarning = string.Empty;
            job.error = string.Empty;
            job.retryCount = 0;
            job.seed = unchecked(job.seed + 104729);
            job.progress = 0f;
            job.status = NpcGenerationStatus.Pending;
            job.statusMessage = "等待重新生成 Sprite Sheet";
        }

        /// <summary>域重载/退出前中止并释放全部请求，持久化为可恢复状态。</summary>
        private void ShutdownForReload()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            foreach (KeyValuePair<string, SpriteSheetImageRequestHandle> pair in activeRequests)
            {
                pair.Value.Abort();
                pair.Value.Dispose();
                NpcGenerationJob job = jobs.FirstOrDefault(item => item.jobId == pair.Key);
                if (job != null) { job.status = NpcGenerationStatus.Interrupted; job.statusMessage = "脚本重载前已安全中止请求"; }
            }
            activeRequests.Clear();
            Persist();
        }

        /// <summary>持久化后通知窗口。</summary>
        private void NotifyAndPersist() { Persist(); Changed?.Invoke(); }

        /// <summary>写盘失败不会被后续请求成功覆盖。</summary>
        private void Persist()
        {
            lastPersistenceError = NpcGenerationQueueStore.TrySave(jobs, out string error) ? string.Empty : error;
        }

        /// <summary>只有错误文本改变时刷新窗口，避免空 Key 每帧重建列表。</summary>
        private void SetRequestError(string value)
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(lastRequestError, normalized, StringComparison.Ordinal)) return;
            lastRequestError = normalized;
            Changed?.Invoke();
        }

        /// <summary>判断重载前需要标记中断的状态。</summary>
        private static bool IsActiveStatus(NpcGenerationJob job)
        {
            return job.status is NpcGenerationStatus.GeneratingImage or NpcGenerationStatus.SavingPreview or
                NpcGenerationStatus.ConvertingSpriteSheet or NpcGenerationStatus.Importing or NpcGenerationStatus.QualityChecking;
        }
    }
}
