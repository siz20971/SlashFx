# Changelog

All notable changes to this package will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-11-19
### Added
- Initial Unity 6 (`6000.3.14f1`+) UPM package for Slash Effect Studio.
- `SlashEffectAsset` ScriptableObject with bidirectional `.slash.json` import/export.
- Arc-length LUT Poly-Bezier math engine, Fritsch-Carlson Monotone spline curves, and Dual-Curve UV ribbon mesh builder.
- Render-pipeline-agnostic GPU offscreen renderer (`CommandBuffer` + `RenderTexture`) with UV-space gradient, core, glow blur, wind/fire/lightning patterns, toon outline, pixel art quantization, phantom echo, and multi-slash combos.
- One-click SpriteSheet baker with `ISpriteEditorDataProvider` automatic Multiple Sprite slicing, POT/Grid/Tight Trim, empty frame trimming, and Standard/PMA/Additive/Grayscale output modes.
- XY-plane `ParticleSystem` GameObject & Prefab factory with double-sided (`Cull Off`) material auto-detection (Built-in / URP / HDRP) and GUID-preserving Hot-Rebake.
- `SlashStudioWindow` interactive editor with Bezier/Pivot viewport, timeline playback, onion skinning, 11 presets, spin generator, and Scene View target overlay.
- Lightweight `SlashParticlePlayer` XY-plane runtime helper.
