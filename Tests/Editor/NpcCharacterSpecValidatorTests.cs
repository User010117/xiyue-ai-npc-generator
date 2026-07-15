using NUnit.Framework;
using UnityEngine;
using Xiyue.AINpcGenerator.Editor;

namespace Xiyue.AINpcGenerator.Tests
{
    public sealed class NpcCharacterSpecValidatorTests
    {
        [Test]
        public void ValidateAndNormalize_ClampsAndNormalizesStructuredData()
        {
            var spec = new NpcCharacterSpec
            {
                displayName = "  A Very Long But Valid Name  ",
                age = 250,
                personalityTraits = new[] { "Calm", "Calm", "Curious" },
                dialogueLines = new[] { " Hello. " },
                defaultEmotion = "not-real",
                movementStyle = "FAST",
                behaviorTendency = "social",
                appearance = new NpcAppearanceSpec { hairStyle = "Long Hair", accessories = null }
            };

            NpcValidationResult result = NpcCharacterSpecValidator.ValidateAndNormalize(spec);

            Assert.That(result.IsValid, Is.True);
            Assert.That(spec.age, Is.EqualTo(90));
            Assert.That(spec.personalityTraits, Has.Length.EqualTo(2));
            Assert.That(spec.defaultEmotion, Is.EqualTo("neutral"));
            Assert.That(spec.movementStyle, Is.EqualTo("fast"));
            Assert.That(spec.appearance.hairStyle, Is.EqualTo("long_hair"));
            Assert.That(spec.appearance.accessories, Is.Empty);
        }

        [Test]
        public void ValidateAndNormalize_RejectsMissingPersonalityAndDialogue()
        {
            var spec = new NpcCharacterSpec
            {
                personalityTraits = System.Array.Empty<string>(),
                dialogueLines = System.Array.Empty<string>()
            };

            NpcValidationResult result = NpcCharacterSpecValidator.ValidateAndNormalize(spec);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.errors.Count, Is.EqualTo(2));
        }

        /// <summary>验证队列快照可记录参考图 GUID，但绝不序列化 API Key。</summary>
        [Test]
        public void QueueJobJson_DoesNotHaveApiKeyField()
        {
            var job = new NpcGenerationJob
            {
                prompt = "secret-free prompt",
                model = "gemini-test",
                referenceImageGuids = new[] { "guid-a", "guid-b" }
            };
            string json = JsonUtility.ToJson(job);
            Assert.That(json, Does.Not.Contain("apiKey").IgnoreCase);
            Assert.That(json, Does.Not.Contain("x-goog-api-key").IgnoreCase);
            Assert.That(json, Does.Contain("guid-a"));
        }

        /// <summary>
        /// 验证多模态请求先发送图片、最后发送文字，并保持 Gemini REST 所需字段名称。
        /// </summary>
        [Test]
        public void GeminiImageRequest_Create_PlacesInlineImagesBeforeText()
        {
            var image = new GeminiInlineData { mimeType = "image/png", data = "AQID" };

            string json = GeminiImageRequest.CreateJson("Create one Sprite Sheet", new[] { image });

            Assert.That(json, Does.Contain("{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}},{\"text\":\"Create one Sprite Sheet\"}"));
            Assert.That(json, Does.Not.Contain("\"text\":\"\",\"inlineData\""));
            Assert.That(json, Does.Not.Contain("\"text\":\"Create one Sprite Sheet\",\"inlineData\""));
            Assert.That(json, Does.Not.Contain("generationConfig"));
            Assert.That(json, Does.Not.Contain("responseModalities"));
            Assert.That(json, Does.Not.Contain("imageConfig"));
            Assert.That(json, Does.Not.Contain("responseFormat"));
            Assert.That(SpriteSheetGenerationDefaults.Instruction, Does.Contain("1:1"));
            Assert.That(json, Does.Not.Contain("apiKey").IgnoreCase);
        }

        /// <summary>验证友好名称不会被直接发送给接口，旧 Pro 预览 ID 也能恢复为稳定显示名。</summary>
        [Test]
        public void NanoBananaModelNames_MapToOfficialApiIds()
        {
            Assert.That(GeminiSpriteSheetImageProvider.Endpoint, Does.Contain("/v1beta/"));
            Assert.That(NpcGeneratorWindow.GetModelId("Nano Banana 2（推荐）", string.Empty), Is.EqualTo("gemini-3.1-flash-image"));
            Assert.That(NpcGeneratorWindow.GetModelId("Nano Banana Pro（高质量）", string.Empty), Is.EqualTo("gemini-3-pro-image"));
            Assert.That(NpcGeneratorWindow.GetModelDisplayName("gemini-3-pro-image-preview"), Is.EqualTo("Nano Banana Pro（高质量）"));
            Assert.That(NpcGeneratorWindow.GetModelDisplayName("gemini-3.1-pro-preview"), Is.EqualTo("Nano Banana Pro（高质量）"));
        }

        /// <summary>验证合法图片响应可被解析，且最后一个图片 Part 被作为最终成品。</summary>
        [Test]
        public void GeminiImageResponseParser_ParsesImageWithoutNetworkRequest()
        {
            const string json = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ignored\"},{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"AQID\"}}]}}]}";

            SpriteSheetImageResult result = GeminiImageResponseParser.Parse(json, out string error);

            Assert.That(error, Is.Empty);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.mimeType, Is.EqualTo("image/png"));
            Assert.That(result.bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>验证非图片、空图片与非法 Base64 都会被拒绝，而不是进入本地转换管线。</summary>
        [TestCase("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"only text\"}]}}]}")]
        [TestCase("{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"\"}}]}}]}")]
        [TestCase("{\"candidates\":[{\"content\":{\"parts\":[{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"not-base64\"}}]}}]}")]
        public void GeminiImageResponseParser_RejectsInvalidOutputs(string json)
        {
            SpriteSheetImageResult result = GeminiImageResponseParser.Parse(json, out string error);

            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        /// <summary>验证草稿、历史和命名预设能作为一个 JSON 文档往返，并按输入与名称去重。</summary>
        [Test]
        public void PromptRecordDocument_RoundTripsAndDeduplicatesInputs()
        {
            var document = new NpcPromptRecordDocument();
            NpcPromptRecordStore.SetDraft(document, "  蓝甲战士  ", new[] { "guid-a", "guid-a", "guid-b" }, "指令甲", 0.25f);
            NpcPromptRecordStore.RecordHistory(document, "蓝甲战士", new[] { "guid-a", "guid-b" }, "2026-07-15T08:00:00Z", "指令甲", 0.25f);
            NpcPromptRecordStore.RecordHistory(document, "蓝甲战士", new[] { "guid-a", "guid-b" }, "2026-07-15T09:00:00Z", "指令甲", 0.25f);
            NpcPromptRecordStore.UpsertPreset(document, "战士", "蓝甲战士", new[] { "guid-a" }, "指令甲", 0.25f);
            NpcPromptRecordStore.UpsertPreset(document, "战士", "红甲战士", new[] { "guid-b" }, "指令乙", 0.4f);

            string json = JsonUtility.ToJson(document, true);
            NpcPromptRecordDocument restored = JsonUtility.FromJson<NpcPromptRecordDocument>(json);

            Assert.That(restored.draft.prompt, Is.EqualTo("蓝甲战士"));
            Assert.That(restored.draft.referenceImageGuids, Is.EqualTo(new[] { "guid-a", "guid-b" }));
            Assert.That(restored.draft.instruction, Is.EqualTo("指令甲"));
            Assert.That(restored.history, Has.Length.EqualTo(1));
            Assert.That(restored.history[0].savedUtc, Does.StartWith("2026-07-15T09:00:00"));
            Assert.That(restored.presets, Has.Length.EqualTo(1));
            Assert.That(restored.presets[0].prompt, Is.EqualTo("红甲战士"));
            Assert.That(restored.presets[0].instruction, Is.EqualTo("指令乙"));
            Assert.That(restored.presets[0].greenScreenTolerance, Is.EqualTo(0.4f).Within(0.001f));
        }

        /// <summary>验证长期使用时历史记录保持固定上限，并优先保留最近输入。</summary>
        [Test]
        public void PromptRecordHistory_KeepsNewestThirtyEntries()
        {
            var document = new NpcPromptRecordDocument();
            for (int index = 0; index < 35; index++)
            {
                // 每条描述都不同，用于覆盖历史裁剪分支，而不是被去重逻辑提前合并。
                NpcPromptRecordStore.RecordHistory(document, "角色 " + index, System.Array.Empty<string>());
            }

            Assert.That(document.history, Has.Length.EqualTo(NpcPromptRecordStore.MaxHistoryCount));
            Assert.That(document.history[0].prompt, Is.EqualTo("角色 34"));
            Assert.That(document.history[^1].prompt, Is.EqualTo("角色 5"));
        }
    }
}
