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

        [Test]
        public void QueueJobJson_DoesNotHaveApiKeyField()
        {
            var job = new NpcGenerationJob { prompt = "secret-free prompt", model = "gemini-test" };
            string json = JsonUtility.ToJson(job);
            Assert.That(json, Does.Not.Contain("apiKey").IgnoreCase);
            Assert.That(json, Does.Not.Contain("x-goog-api-key").IgnoreCase);
        }
    }
}
