using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xiyue.AINpcGenerator.Editor
{
    [InitializeOnLoad]
    public sealed class NpcGenerationQueue
    {
        private static readonly NpcGenerationQueue instance = new();
        private readonly List<NpcGenerationJob> jobs = new();
        private readonly Dictionary<string, NpcAiRequestHandle> activeRequests = new();
        private INpcAiProvider provider = new GeminiNpcProvider();
        private bool paused;
        private bool shuttingDown;
        private string lastQueueError = string.Empty;
        private double lastUiPulse;

        static NpcGenerationQueue()
        {
            _ = instance;
        }

        private NpcGenerationQueue()
        {
            jobs.AddRange(NpcGenerationQueueStore.Load());
            foreach (NpcGenerationJob job in jobs.Where(IsActiveStatus))
            {
                job.status = NpcGenerationStatus.Interrupted;
                job.statusMessage = "Interrupted by editor or script reload. Resume when ready.";
            }

            Persist();
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += ShutdownForReload;
            EditorApplication.quitting += ShutdownForReload;
        }

        public static NpcGenerationQueue Instance => instance;
        public IReadOnlyList<NpcGenerationJob> Jobs => jobs;
        public bool IsPaused => paused;
        public string LastQueueError => lastQueueError;
        public event Action Changed;

        public void SetProviderForTests(INpcAiProvider value)
        {
            provider = value ?? throw new ArgumentNullException(nameof(value));
        }

        public string Enqueue(
            string prompt,
            int count,
            string model,
            NpcRigProfile profile,
            NpcPartCatalog catalog,
            string outputRoot,
            bool autoAddAsNpc)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Character description cannot be empty.", nameof(prompt));
            }

            if (profile == null || catalog == null)
            {
                throw new ArgumentException("A rig profile and part catalog are required.");
            }

            count = Mathf.Clamp(count, 1, 200);
            string batchId = Guid.NewGuid().ToString("N");
            string profileGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile));
            string catalogGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(catalog));
            int baseSeed = unchecked((int)DateTime.UtcNow.Ticks);

            for (int index = 0; index < count; index++)
            {
                jobs.Add(new NpcGenerationJob
                {
                    jobId = Guid.NewGuid().ToString("N"),
                    batchId = batchId,
                    createdUtc = DateTime.UtcNow.ToString("O"),
                    prompt = prompt.Trim(),
                    model = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim(),
                    seed = unchecked(baseSeed + (index * 7919)),
                    status = NpcGenerationStatus.Pending,
                    statusMessage = "Queued",
                    profileGuid = profileGuid,
                    catalogGuid = catalogGuid,
                    outputRoot = outputRoot,
                    autoAddAsNpc = autoAddAsNpc
                });
            }

            paused = false;
            lastQueueError = string.Empty;
            NotifyAndPersist();
            return batchId;
        }

        public void Pause()
        {
            paused = true;
            foreach (NpcGenerationJob job in jobs.Where(job => job.status == NpcGenerationStatus.Pending))
            {
                job.status = NpcGenerationStatus.Paused;
                job.statusMessage = "Paused before request started";
            }
            NotifyAndPersist();
        }

        public void Resume()
        {
            paused = false;
            foreach (NpcGenerationJob job in jobs.Where(job =>
                         job.status == NpcGenerationStatus.Paused || job.status == NpcGenerationStatus.Interrupted))
            {
                job.status = job.HasCharacterData ? NpcGenerationStatus.SelectingParts : NpcGenerationStatus.Pending;
                job.statusMessage = "Resumed";
                job.error = string.Empty;
            }
            NotifyAndPersist();
        }

        public void CancelOutstanding()
        {
            foreach (KeyValuePair<string, NpcAiRequestHandle> pair in activeRequests.ToArray())
            {
                pair.Value.Abort();
                pair.Value.Dispose();
                NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.jobId == pair.Key);
                if (job != null)
                {
                    job.status = NpcGenerationStatus.Cancelled;
                    job.statusMessage = "Cancelled by user";
                }
            }
            activeRequests.Clear();

            foreach (NpcGenerationJob job in jobs.Where(job =>
                         job.status is NpcGenerationStatus.Pending or NpcGenerationStatus.Paused or NpcGenerationStatus.RetryWaiting))
            {
                job.status = NpcGenerationStatus.Cancelled;
                job.statusMessage = "Cancelled by user";
            }
            NotifyAndPersist();
        }

        public void Retry(string jobId)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.jobId == jobId);
            if (job == null || activeRequests.ContainsKey(jobId))
            {
                return;
            }

            job.error = string.Empty;
            job.retryCount = 0;
            job.status = job.HasCharacterData ? NpcGenerationStatus.SelectingParts : NpcGenerationStatus.Pending;
            job.statusMessage = "Queued for retry";
            NotifyAndPersist();
        }

        public void RegenerateCharacterData(string jobId)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.jobId == jobId);
            if (job == null || activeRequests.ContainsKey(jobId))
            {
                return;
            }

            job.character = null;
            job.resolvedAppearance = null;
            job.outputAssetPath = string.Empty;
            job.seed = unchecked(job.seed + 104729);
            job.retryCount = 0;
            job.error = string.Empty;
            job.progress = 0f;
            job.status = NpcGenerationStatus.Pending;
            job.statusMessage = "Character data will be regenerated";
            NotifyAndPersist();
        }

        public void RerollAppearance(string jobId, bool keepSeed)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.jobId == jobId);
            if (job == null || !job.HasCharacterData || activeRequests.ContainsKey(jobId))
            {
                return;
            }

            if (!keepSeed)
            {
                job.seed = unchecked(job.seed + 104729);
            }
            job.resolvedAppearance = null;
            job.outputAssetPath = string.Empty;
            job.error = string.Empty;
            job.progress = 0.4f;
            job.status = NpcGenerationStatus.SelectingParts;
            job.statusMessage = keepSeed ? "Rebuilding with the saved seed" : "Rerolling modular appearance";
            NotifyAndPersist();
        }

        public void RetryAllFailed()
        {
            foreach (NpcGenerationJob job in jobs.Where(job =>
                         job.status is NpcGenerationStatus.Failed or NpcGenerationStatus.NeedsReview or NpcGenerationStatus.Interrupted))
            {
                job.error = string.Empty;
                job.retryCount = 0;
                job.status = job.HasCharacterData ? NpcGenerationStatus.SelectingParts : NpcGenerationStatus.Pending;
                job.statusMessage = "Queued for retry";
            }
            NotifyAndPersist();
        }

        public void ClearCompleted()
        {
            jobs.RemoveAll(job => job.status is NpcGenerationStatus.Completed or NpcGenerationStatus.Cancelled);
            NotifyAndPersist();
        }

        public void AddToScene(string jobId, bool asPlayer)
        {
            NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.jobId == jobId);
            if (job == null || string.IsNullOrWhiteSpace(job.outputAssetPath))
            {
                return;
            }

            NpcGenerationPipeline.AddGeneratedPrefabToScene(job, asPlayer);
        }

        private void Tick()
        {
            if (shuttingDown)
            {
                return;
            }

            PollRequests();
            PromoteRetries();
            if (paused)
            {
                return;
            }

            ProcessOneLocalJob();
            StartRequests();
        }

        private void StartRequests()
        {
            string apiKey = NpcGeneratorSessionSecrets.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (jobs.Any(job => job.status == NpcGenerationStatus.Pending))
                {
                    lastQueueError = "Enter a Gemini API Key or set GEMINI_API_KEY before starting queued requests.";
                    Changed?.Invoke();
                }
                return;
            }

            lastQueueError = string.Empty;
            int concurrency = Mathf.Clamp(NpcGeneratorProjectSettings.instance.maxConcurrency, 1, 20);
            while (activeRequests.Count < concurrency)
            {
                NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.status == NpcGenerationStatus.Pending);
                if (job == null)
                {
                    break;
                }

                try
                {
                    string variationPrompt = job.prompt + $"\n\nVariation token: {job.seed}. Create a distinct interpretation of the request.";
                    NpcAiRequestHandle handle = provider.BeginGenerate(variationPrompt, job.model, apiKey);
                    activeRequests.Add(job.jobId, handle);
                    job.status = NpcGenerationStatus.GeneratingData;
                    job.statusMessage = "Requesting structured character data";
                    job.progress = 0.05f;
                    NotifyAndPersist();
                }
                catch (Exception exception)
                {
                    job.status = NpcGenerationStatus.Failed;
                    job.error = exception.Message;
                    job.statusMessage = "Could not start Gemini request";
                    NotifyAndPersist();
                }
            }
        }

        private void PollRequests()
        {
            foreach (KeyValuePair<string, NpcAiRequestHandle> pair in activeRequests.ToArray())
            {
                NpcGenerationJob job = jobs.First(candidate => candidate.jobId == pair.Key);
                NpcAiRequestHandle handle = pair.Value;
                job.progress = Mathf.Lerp(0.05f, 0.35f, handle.Progress);
                if (!handle.IsDone)
                {
                    continue;
                }

                handle.TryGetResult(out NpcCharacterSpec spec, out long statusCode, out string error);
                handle.Dispose();
                activeRequests.Remove(pair.Key);

                if (!string.IsNullOrWhiteSpace(error) || spec == null)
                {
                    HandleRequestFailure(job, statusCode, error);
                    continue;
                }

                job.status = NpcGenerationStatus.ValidatingData;
                job.statusMessage = "Validating structured data";
                NpcValidationResult validation = NpcCharacterSpecValidator.ValidateAndNormalize(spec);
                if (!validation.IsValid)
                {
                    job.status = NpcGenerationStatus.Failed;
                    job.error = string.Join("\n", validation.errors);
                    job.statusMessage = "Structured data failed validation";
                }
                else
                {
                    job.character = spec;
                    job.status = NpcGenerationStatus.SelectingParts;
                    job.statusMessage = validation.warnings.Count == 0
                        ? "Character data ready"
                        : "Character data normalized: " + string.Join("; ", validation.warnings);
                    job.progress = 0.4f;
                }

                NotifyAndPersist();
            }

            if (activeRequests.Count > 0 && EditorApplication.timeSinceStartup - lastUiPulse >= 0.1d)
            {
                lastUiPulse = EditorApplication.timeSinceStartup;
                Changed?.Invoke();
            }
        }

        private void ProcessOneLocalJob()
        {
            NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.status == NpcGenerationStatus.SelectingParts);
            if (job == null)
            {
                return;
            }

            try
            {
                string profilePath = AssetDatabase.GUIDToAssetPath(job.profileGuid);
                string catalogPath = AssetDatabase.GUIDToAssetPath(job.catalogGuid);
                NpcRigProfile profile = AssetDatabase.LoadAssetAtPath<NpcRigProfile>(profilePath);
                NpcPartCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcPartCatalog>(catalogPath);
                if (profile == null || catalog == null)
                {
                    throw new InvalidOperationException("The saved rig profile or part catalog no longer exists.");
                }

                var usedFingerprints = new HashSet<string>(jobs
                    .Where(candidate => candidate.batchId == job.batchId && candidate.jobId != job.jobId &&
                                        candidate.status == NpcGenerationStatus.Completed && candidate.resolvedAppearance != null &&
                                        !string.IsNullOrWhiteSpace(candidate.resolvedAppearance.fingerprint))
                    .Select(candidate => candidate.resolvedAppearance.fingerprint), StringComparer.Ordinal);
                NpcGeneratedAssets generated = NpcGenerationPipeline.Generate(job, profile, catalog, OnLocalProgress, usedFingerprints);
                job.resolvedAppearance = generated.resolvedAppearance;
                job.outputAssetPath = generated.definitionPath;
                job.status = NpcGenerationStatus.Completed;
                job.statusMessage = "Generated and quality checked";
                job.progress = 1f;
                if (job.autoAddAsNpc)
                {
                    NpcGenerationPipeline.AddGeneratedPrefabToScene(job, false);
                }
            }
            catch (NpcNeedsReviewException exception)
            {
                job.status = NpcGenerationStatus.NeedsReview;
                job.error = exception.Message;
                job.statusMessage = "Generated output needs review";
            }
            catch (Exception exception)
            {
                job.status = NpcGenerationStatus.Failed;
                job.error = exception.Message;
                job.statusMessage = "Local generation failed";
                Debug.LogException(exception);
            }

            NotifyAndPersist();

            void OnLocalProgress(NpcGenerationStatus status, float progress, string message)
            {
                job.status = status;
                job.progress = progress;
                job.statusMessage = message;
                Changed?.Invoke();
            }
        }

        private void PromoteRetries()
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            foreach (NpcGenerationJob job in jobs.Where(job =>
                         job.status == NpcGenerationStatus.RetryWaiting && job.retryAtEpochSeconds <= now))
            {
                job.status = NpcGenerationStatus.Pending;
                job.statusMessage = "Retrying request";
            }
        }

        private void HandleRequestFailure(NpcGenerationJob job, long statusCode, string error)
        {
            bool retryable = statusCode == 0 || statusCode == 408 || statusCode == 429 || statusCode >= 500;
            if (retryable && job.retryCount < 3)
            {
                job.retryCount++;
                double jitter = UnityEngine.Random.Range(0.1f, 0.8f);
                job.retryAtEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d +
                                          Math.Pow(2d, job.retryCount) + jitter;
                job.status = NpcGenerationStatus.RetryWaiting;
                job.statusMessage = $"Retry {job.retryCount}/3 scheduled";
                job.error = error;
            }
            else
            {
                job.status = NpcGenerationStatus.Failed;
                job.statusMessage = statusCode > 0 ? $"Gemini request failed ({statusCode})" : "Gemini request failed";
                job.error = string.IsNullOrWhiteSpace(error) ? "Unknown Gemini request error." : error;
            }
            NotifyAndPersist();
        }

        private void ShutdownForReload()
        {
            if (shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            foreach (KeyValuePair<string, NpcAiRequestHandle> pair in activeRequests)
            {
                pair.Value.Abort();
                pair.Value.Dispose();
                NpcGenerationJob job = jobs.FirstOrDefault(candidate => candidate.jobId == pair.Key);
                if (job != null)
                {
                    job.status = NpcGenerationStatus.Interrupted;
                    job.statusMessage = "Interrupted safely before script reload or editor exit";
                }
            }
            activeRequests.Clear();
            Persist();
        }

        private void NotifyAndPersist()
        {
            Persist();
            Changed?.Invoke();
        }

        private void Persist() => NpcGenerationQueueStore.Save(jobs);

        private static bool IsActiveStatus(NpcGenerationJob job)
        {
            return job.status is NpcGenerationStatus.GeneratingData or NpcGenerationStatus.ValidatingData or
                NpcGenerationStatus.SelectingParts or NpcGenerationStatus.Assembling or
                NpcGenerationStatus.Importing or NpcGenerationStatus.QualityChecking;
        }
    }
}
