using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Xiyue.AINpcGenerator.Editor
{
    /// <summary>集中保存 Sprite Sheet 生图默认值，避免窗口、预设和队列各自维护魔法字符串。</summary>
    internal static class SpriteSheetGenerationDefaults
    {
        /// <summary>默认使用 Gemini 当前通用图片模型；高级设置仍允许填写兼容模型 ID。</summary>
        public const string ImageModel = "gemini-3.1-flash-image";
        /// <summary>绿幕色差容差；保留为用户可调值，因为生成图片的绿色并不总是完全一致。</summary>
        public const float GreenScreenTolerance = 0.32f;
        /// <summary>类似 Gem 的可编辑默认指令；A0/A1/A2 等业务规则由具体预设自行追加。</summary>
        public const string Instruction =
            "你是一名资深像素动画师，专门设计像素角色动画。\n" +
            "请根据用户描述和按顺序提供的参考图，生成一张角色 Sprite Sheet。\n" +
            "保持清晰的像素边缘和统一像素密度，不使用抗锯齿、模糊、半透明边缘或非整数缩放。\n" +
            "背景使用纯绿色，整张图片为 1:1 方形。\n" +
            "图片中不得出现文字、数字、网格线、水印或界面元素。\n" +
            "角色外观、动作、朝向、帧顺序、每行帧数和留白严格遵循用户描述与参考图。\n" +
            "每格角色尺寸、脚底基准线、光源、比例和配色保持一致；各帧不得裁切或重叠。\n" +
            "只输出 Sprite Sheet 图片，不输出说明文字。";
    }

    /// <summary>一次冻结后的 Sprite Sheet 图片请求；API Key 只存在内存，不进入队列 JSON。</summary>
    public sealed class SpriteSheetImageRequest
    {
        /// <summary>最终发送给模型的完整指令和角色描述。</summary>
        public string prompt;
        /// <summary>Gemini 图片模型 ID。</summary>
        public string model;
        /// <summary>当前编辑器会话中的 Gemini Key。</summary>
        public string apiKey;
        /// <summary>按用户顺序冻结的 Unity 参考图 GUID。</summary>
        public IReadOnlyList<string> referenceImageGuids;
    }

    /// <summary>图片模型的最小结果，只暴露图片字节和 MIME，不把网络类型泄漏到队列。</summary>
    public sealed class SpriteSheetImageResult
    {
        /// <summary>模型返回的原始图片字节。</summary>
        public byte[] bytes;
        /// <summary>模型返回的图片 MIME。</summary>
        public string mimeType;
    }

    /// <summary>隔离图片模型实现，队列只依赖开始请求和轮询结果这两个稳定能力。</summary>
    public interface ISpriteSheetImageProvider
    {
        /// <summary>启动一次 Sprite Sheet 图片生成。</summary>
        SpriteSheetImageRequestHandle BeginGenerate(SpriteSheetImageRequest request);
    }

    /// <summary>可取消的图片请求句柄；域重载时必须 Abort 并 Dispose。</summary>
    public abstract class SpriteSheetImageRequestHandle : IDisposable
    {
        /// <summary>请求是否已经结束。</summary>
        public abstract bool IsDone { get; }
        /// <summary>传输阶段的近似进度。</summary>
        public abstract float Progress { get; }
        /// <summary>停止尚未完成的网络请求。</summary>
        public abstract void Abort();
        /// <summary>读取一次最终结果；未完成或已经消费时返回 false。</summary>
        public abstract bool TryGetResult(out SpriteSheetImageResult result, out long statusCode, out string error);
        /// <summary>释放 UnityWebRequest 原生资源。</summary>
        public abstract void Dispose();
    }

    /// <summary>
    /// 未来文本模型（例如 DeepSeek）的扩展边界。本轮没有实现和调用，避免 Gemini 再产生第二次请求。
    /// </summary>
    public interface INpcMetadataProvider
    {
        /// <summary>根据角色描述异步生成可选元数据；接入实现时由显式配置启用。</summary>
        NpcMetadataRequestHandle BeginGenerate(string prompt, string apiKey);
    }

    /// <summary>未来元数据 Provider 的可取消结果句柄。</summary>
    public abstract class NpcMetadataRequestHandle : IDisposable
    {
        /// <summary>请求是否结束。</summary>
        public abstract bool IsDone { get; }
        /// <summary>读取一次角色元数据结果。</summary>
        public abstract bool TryGetResult(out NpcCharacterSpec spec, out string error);
        /// <summary>停止请求。</summary>
        public abstract void Abort();
        /// <summary>释放请求资源。</summary>
        public abstract void Dispose();
    }

    /// <summary>使用 Gemini generateContent 图片输出生成 Sprite Sheet。</summary>
    public sealed class GeminiSpriteSheetImageProvider : ISpriteSheetImageProvider
    {
        /// <summary>Google 图片模型通过 v1beta 暴露 generateContent；模型名由任务快照提供。</summary>
        internal const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        /// <summary>校验输入、构造合法 oneof Parts，并发送一次图片请求。</summary>
        public SpriteSheetImageRequestHandle BeginGenerate(SpriteSheetImageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.apiKey)) throw new ArgumentException("Gemini API Key is empty.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.prompt)) throw new ArgumentException("Sprite Sheet prompt is empty.", nameof(request));

            string model = string.IsNullOrWhiteSpace(request.model) ? SpriteSheetGenerationDefaults.ImageModel : request.model.Trim();
            string url = string.Format(Endpoint, UnityWebRequest.EscapeURL(model));
            GeminiInlineData[] images = GeminiReferenceImageLoader.Load(request.referenceImageGuids);
            byte[] json = Encoding.UTF8.GetBytes(GeminiImageRequest.CreateJson(request.prompt, images));
            var webRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(json),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 180
            };
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("x-goog-api-key", request.apiKey);
            return new GeminiSpriteSheetRequestHandle(webRequest, webRequest.SendWebRequest());
        }
    }

    /// <summary>构造图片请求文本；Rig Profile 约束由代码追加，用户指令无法意外删掉关键网格要求。</summary>
    internal static class SpriteSheetPromptBuilder
    {
        /// <summary>合并可编辑指令、机器网格契约、角色描述和批量变化种子。</summary>
        public static string Build(string instruction, string userPrompt, NpcRigProfile profile, int variationSeed)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            string directions = string.Join("、", profile.directionNames ?? Array.Empty<string>());
            return
                (string.IsNullOrWhiteSpace(instruction) ? SpriteSheetGenerationDefaults.Instruction : instruction.Trim()) +
                $"\n\n必须遵守的 Rig Profile：精确 {profile.directions} 行 × {profile.framesPerDirection} 列；" +
                $"从上到下依次为 {directions}；每格角色区域一致；每行第 1 帧是待机，其余帧与第 1 帧共同组成行走循环。" +
                "不要增加标题、边距说明或额外面板。" +
                $"\n\n角色描述：\n{(userPrompt ?? string.Empty).Trim()}" +
                $"\n\n变化种子：{variationSeed}。保持网格不变，只改变符合描述的角色视觉细节。";
        }
    }

    /// <summary>负责把 Unity 图片资产转换为 Gemini inlineData，并统一数量、格式和体积边界。</summary>
    internal static class GeminiReferenceImageLoader
    {
        /// <summary>单个任务允许的最大参考图数量。</summary>
        public const int MaxImageCount = 6;
        /// <summary>单张参考图最大原始文件体积。</summary>
        public const long MaxImageBytes = 5L * 1024L * 1024L;
        /// <summary>全部参考图原始体积上限。</summary>
        public const long MaxTotalBytes = 14L * 1024L * 1024L;

        /// <summary>只验证资产引用和文件边界，不分配 Base64。</summary>
        public static void Validate(IReadOnlyList<string> guids) => _ = CollectFiles(guids);

        /// <summary>读取并编码校验后的参考图。</summary>
        public static GeminiInlineData[] Load(IReadOnlyList<string> guids)
        {
            return CollectFiles(guids).Select(file => new GeminiInlineData
            {
                mimeType = file.mimeType,
                data = Convert.ToBase64String(File.ReadAllBytes(file.absolutePath))
            }).ToArray();
        }

        /// <summary>从 GUID 解析真实文件，拒绝非项目路径和不支持格式。</summary>
        private static List<ReferenceFile> CollectFiles(IReadOnlyList<string> guids)
        {
            var files = new List<ReferenceFile>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            long total = 0L;
            foreach (string guid in guids ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(guid) || !unique.Add(guid)) continue;
                if (unique.Count > MaxImageCount) throw new InvalidOperationException($"Default reference images cannot exceed {MaxImageCount} files.");
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(assetPath) || AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) == null)
                    throw new InvalidOperationException($"Reference image asset no longer exists for GUID '{guid}'.");
                string extension = Path.GetExtension(assetPath).ToLowerInvariant();
                string mimeType = extension switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _ => throw new InvalidOperationException($"Reference image '{assetPath}' must be a PNG or JPEG asset.")
                };
                string absolutePath = DefaultNpcAssetWriter.AssetPathToAbsolute(assetPath);
                var info = new FileInfo(absolutePath);
                if (!info.Exists || info.Length <= 0L) throw new InvalidOperationException($"Reference image file is missing or empty: {assetPath}.");
                if (info.Length > MaxImageBytes) throw new InvalidOperationException($"Reference image '{assetPath}' exceeds the 5MB per-image limit.");
                total += info.Length;
                if (total > MaxTotalBytes) throw new InvalidOperationException("Default reference images exceed the 14MB total limit.");
                files.Add(new ReferenceFile(absolutePath, mimeType));
            }
            return files;
        }

        /// <summary>一次校验后的文件描述。</summary>
        private sealed class ReferenceFile
        {
            /// <summary>初始化不可变路径和 MIME。</summary>
            public ReferenceFile(string absolutePath, string mimeType) { this.absolutePath = absolutePath; this.mimeType = mimeType; }
            /// <summary>资产对应的绝对路径。</summary>
            public readonly string absolutePath;
            /// <summary>Gemini inlineData MIME。</summary>
            public readonly string mimeType;
        }
    }

    /// <summary>构造满足 Gemini Part oneof 的图片生成 JSON。</summary>
    internal static class GeminiImageRequest
    {
        /// <summary>图片 Part 和文字 Part 分别序列化，避免空字段也被服务端判定为设置了 oneof。</summary>
        public static string CreateJson(string prompt, IReadOnlyList<GeminiInlineData> referenceImages)
        {
            var json = new StringBuilder("{\"contents\":[{\"role\":\"user\",\"parts\":[");
            bool hasPart = false;
            foreach (GeminiInlineData image in referenceImages ?? Array.Empty<GeminiInlineData>())
            {
                if (image == null) continue;
                if (hasPart) json.Append(',');
                json.Append(JsonUtility.ToJson(new GeminiImagePart { inlineData = image }));
                hasPart = true;
            }
            if (hasPart) json.Append(',');
            json.Append(JsonUtility.ToJson(new GeminiTextPart { text = prompt ?? string.Empty }));
            // v1 图片模型会直接返回图片；线上端点拒绝图片 generationConfig，比例由 Rig 提示和本地验证保证。
            json.Append("]}]}");
            return json.ToString();
        }
    }

    /// <summary>UnityWebRequest 图片结果适配器。</summary>
    internal sealed class GeminiSpriteSheetRequestHandle : SpriteSheetImageRequestHandle
    {
        /// <summary>实际网络请求。</summary>
        private UnityWebRequest request;
        /// <summary>Unity 异步操作。</summary>
        private UnityWebRequestAsyncOperation operation;
        /// <summary>防止同一结果被消费两次。</summary>
        private bool consumed;

        /// <summary>保存网络请求及异步操作。</summary>
        public GeminiSpriteSheetRequestHandle(UnityWebRequest request, UnityWebRequestAsyncOperation operation)
        {
            this.request = request;
            this.operation = operation;
        }

        /// <summary>请求完成或已经释放。</summary>
        public override bool IsDone => operation == null || operation.isDone;
        /// <summary>上传/下载近似进度。</summary>
        public override float Progress => operation?.progress ?? 1f;

        /// <summary>中止未完成请求。</summary>
        public override void Abort() { if (request != null && !IsDone) request.Abort(); }

        /// <summary>读取候选中最后一个有效 inlineData 图片。</summary>
        public override bool TryGetResult(out SpriteSheetImageResult result, out long statusCode, out string error)
        {
            result = null;
            statusCode = request?.responseCode ?? 0L;
            error = string.Empty;
            if (!IsDone || consumed) return false;
            consumed = true;
            if (request == null) { error = "Gemini request was disposed before its result was read."; return true; }
            if (request.result != UnityWebRequest.Result.Success)
            {
                error = ExtractError(request.downloadHandler?.text, request.error);
                return true;
            }
            result = GeminiImageResponseParser.Parse(request.downloadHandler.text, out error);
            return true;
        }

        /// <summary>释放请求资源。</summary>
        public override void Dispose() { operation = null; request?.Dispose(); request = null; }

        /// <summary>优先读取 Gemini JSON 错误消息。</summary>
        private static string ExtractError(string json, string fallback)
        {
            try
            {
                GeminiErrorEnvelope envelope = JsonUtility.FromJson<GeminiErrorEnvelope>(json ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(envelope?.error?.message)) return envelope.error.message;
            }
            catch { /* 传输错误仍由下方 fallback 返回。 */ }
            return string.IsNullOrWhiteSpace(fallback) ? "Unknown Gemini request error." : fallback;
        }
    }

    /// <summary>纯函数式响应解析器，便于在不发送真实请求的情况下覆盖异常响应。</summary>
    internal static class GeminiImageResponseParser
    {
        /// <summary>读取最后一个有效图片 Part；任何无效输出都返回可展示的错误。</summary>
        public static SpriteSheetImageResult Parse(string json, out string error)
        {
            error = string.Empty;
            try
            {
                GeminiResponse response = JsonUtility.FromJson<GeminiResponse>(json ?? string.Empty);
                GeminiInlineData image = response?.candidates?.SelectMany(
                        candidate => candidate?.content?.parts ?? Array.Empty<GeminiResponsePart>())
                    .Select(part => part?.inlineData)
                    .LastOrDefault(data => data != null && !string.IsNullOrWhiteSpace(data.data)
                        && data.mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);
                if (image == null)
                {
                    error = "Gemini response did not contain an image.";
                    return null;
                }

                byte[] bytes = Convert.FromBase64String(image.data);
                if (bytes.Length == 0)
                {
                    error = "Gemini returned an empty image.";
                    return null;
                }
                return new SpriteSheetImageResult { bytes = bytes, mimeType = image.mimeType };
            }
            catch (Exception exception)
            {
                error = "Failed to parse Gemini image output: " + exception.Message;
                return null;
            }
        }
    }

    /// <summary>请求图片 Part。</summary>
    [Serializable] internal sealed class GeminiImagePart { public GeminiInlineData inlineData; }
    /// <summary>请求文字 Part。</summary>
    [Serializable] internal sealed class GeminiTextPart { public string text; }
    /// <summary>Gemini 内联图片。</summary>
    [Serializable] internal sealed class GeminiInlineData { public string mimeType; public string data; }
    /// <summary>Gemini 图片响应。</summary>
    [Serializable] internal sealed class GeminiResponse { public GeminiCandidate[] candidates; }
    /// <summary>单个候选。</summary>
    [Serializable] internal sealed class GeminiCandidate { public GeminiResponseContent content; }
    /// <summary>候选消息内容。</summary>
    [Serializable] internal sealed class GeminiResponseContent { public GeminiResponsePart[] parts; }
    /// <summary>图片响应 Part；响应反序列化允许同时声明可选字段。</summary>
    [Serializable] internal sealed class GeminiResponsePart { public string text; public GeminiInlineData inlineData; public bool thought; }
    /// <summary>Gemini 错误信封。</summary>
    [Serializable] internal sealed class GeminiErrorEnvelope { public GeminiError error; }
    /// <summary>Gemini 错误详情。</summary>
    [Serializable] internal sealed class GeminiError { public int code; public string message; public string status; }
}
