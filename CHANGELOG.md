# Changelog

## 0.3.0 - 2026-07-15

- Replaced raw image model IDs in the UI with Nano Banana 2 and Nano Banana Pro names while retaining official API IDs internally.
- Removed unsupported v1 image `generationConfig` fields; square output remains enforced by the Rig prompt and local validation.
- Switched image generation to the supported Gemini API `v1beta` endpoint so current Nano Banana model IDs resolve correctly.
- Replaced the structured-character plus modular-parts workflow with Gemini Sprite Sheet image generation and local Unity conversion.
- Added editable generation instructions, 1:1 image requests, preview confirmation, regeneration, and optional automatic NPC completion.
- Bound prompt, instruction, green-screen tolerance, and ordered reference images into versioned presets.
- Copied preset references into managed Unity asset folders and snapshotted references for queued batches.
- Added point-sampled Rig Profile normalization, green-screen removal, four-direction idle/walk clips, Animator, definition, and NPC/player prefabs.
- Added queue v2 preview recovery while preserving old completed records and flagging incomplete legacy jobs for recreation.
- Fixed preset selection so it immediately restores all fields and removed the redundant load button.
- Added response parsing, image pipeline, managed-reference, and full Sprite Sheet-to-prefab editor tests.

## 0.2.0 - 2026-07-15

- Added up to six persistent Unity asset reference images for Gemini NPC generation.
- Added PNG/JPEG reference validation with 5 MB per-image and 14 MB total limits.
- Made queue persistence recoverable through atomic replacement, backup, and temporary files.
- Rolled back generated asset folders when quality checks or manifest writing fail.
- Fixed incompatible fallback selection and repeated missing-API-key UI refreshes.
- Added project-local JSON drafts, recent prompt history, and named prompt/reference presets.
- Kept the task list usable down to a 720×440 editor window by reserving queue space.

## 0.1.0 - 2026-07-14

- Initial UPM package implementation.
- Gemini structured character generation and persistent editor queue.
- Deterministic modular pixel character assembly and Unity asset generation.
- Lightweight four-direction NPC and player runtime.
