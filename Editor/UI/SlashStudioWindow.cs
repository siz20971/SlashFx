using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Ympp.SlashEffectStudio.Editor.Baking;
using Ympp.SlashEffectStudio.Editor.Generators;
using Ympp.SlashEffectStudio.Editor.Math;
using Ympp.SlashEffectStudio.Editor.Rendering;

namespace Ympp.SlashEffectStudio.Editor.UI
{
    /// <summary>
    /// Main 3-column interactive Unity EditorWindow for Slash Effect Studio.
    /// Left: Width / Appearance / Timing Properties
    /// Center: GPU Preview Viewport + Interactive Poly-Bezier & Pivot Handles + XY Dummy Character + Bottom Timeline
    /// Right: Canvas/PPU, Path Transforms, Spin Generator, Multi-Slash Combo Layers, SpriteSheet Bake & ParticleSystem Output
    /// </summary>
    public class SlashStudioWindow : EditorWindow
    {
        private enum BgMode { Checker, Dark, Light }

        private SlashEffectAsset _asset;
        private SlashEffectAsset _tempSessionAsset;
        private SlashGpuRenderer _renderer;
        private RenderTexture _previewRT;
        private RenderTexture _onionPrevRT;
        private RenderTexture _onionNextRT;
        private Texture2D _checkerTex;
        private bool _previewDirty = true;
        private bool _hasOnionPrev;
        private bool _hasOnionNext;

        // Playback state
        private int _currentFrame;
        private bool _isPlaying = true;
        private float _playbackSpeed = 1f;
        private double _lastUpdateTime;
        private double _frameAccumulator;

        // Viewport state
        private float _zoom = 1f;
        private Vector2 _panOffset = Vector2.zero;
        private BgMode _bgMode = BgMode.Checker;
        private bool _showGuides = true;
        private float _guideOpacity = 0.85f;
        private bool _showOnionSkin = false;
        private bool _showDummyCharacter = true;
        private bool _dummyFlipX = false;
        private float _dummyScale = 1f;
        private bool _pivotLocked = false;
        private bool _previewBakeOutputMode = false;

        // Layer & drag state
        private int _selectedLayerIndex = -1; // -1 = Main, >=0 = extraSlashes[idx]
        private string _draggingHandleId;
        private bool _isPanning;
        private Vector2 _lastMousePos;

        // Spin generator UI state
        private bool _foldSpinGen = false;
        private float _spinRadiusStart = 150f;
        private float _spinRadiusEnd = 150f;
        private float _spinSweepDeg = 360f;
        private float _spinFlattenY = 1f;
        private bool _spinClockwise = true;

        // Foldouts & scrolls
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private bool _foldWidth = true;
        private bool _foldAppearance = true;
        private bool _foldEffects = false;
        private bool _foldTiming = true;
        private bool _foldPath = true;
        private bool _foldCombo = true;
        private bool _foldBake = true;

        // Resizable side panels
        private const string PrefLeftPanelWidth = "Ympp.SlashEffectStudio.LeftPanelWidth";
        private const string PrefRightPanelWidth = "Ympp.SlashEffectStudio.RightPanelWidth";
        private const float DefaultLeftPanelWidth = 300f;
        private const float DefaultRightPanelWidth = 320f;
        private const float MinSidePanelWidth = 220f;
        private const float MinCenterViewportWidth = 280f;
        private const float SplitterWidth = 5f;

        private float _leftPanelWidth = DefaultLeftPanelWidth;
        private float _rightPanelWidth = DefaultRightPanelWidth;
        private bool _resizingLeft;
        private bool _resizingRight;

        private int _selectedPresetIndex;
        private string _statusMessage = "Ready. XY-Plane Side-Scroller Slash Studio.";

        [MenuItem("Tools/Slash Effect Studio", false, 200)]
        public static void OpenWindow()
        {
            var win = GetWindow<SlashStudioWindow>("Slash Effect Studio");
            win.minSize = new Vector2(1060f, 640f);
            win.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            _leftPanelWidth = EditorPrefs.GetFloat(PrefLeftPanelWidth, DefaultLeftPanelWidth);
            _rightPanelWidth = EditorPrefs.GetFloat(PrefRightPanelWidth, DefaultRightPanelWidth);
            _renderer = new SlashGpuRenderer();
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedo;
            EnsureActiveAsset();
            BuildCheckerTexture();
            _previewDirty = true;
            UpdatePreviewTexturesIfNeeded();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SlashSceneOverlay.Enabled = false;
            ReleaseRenderResources();
            if (_tempSessionAsset != null && _asset == _tempSessionAsset)
            {
                DestroyImmediate(_tempSessionAsset);
                _tempSessionAsset = null;
                _asset = null;
            }
        }

        private void OnUndoRedo()
        {
            _previewDirty = true;
            if (SlashSceneOverlay.Enabled) SlashSceneOverlay.MarkDirty();
            Repaint();
        }

        private void EnsureActiveAsset()
        {
            if (_asset != null)
            {
                _asset.EnsureValid();
                return;
            }

            if (Selection.activeObject is SlashEffectAsset selectedAsset)
            {
                _asset = selectedAsset;
                _asset.EnsureValid();
                return;
            }

            if (_tempSessionAsset == null)
            {
                _tempSessionAsset = CreateInstance<SlashEffectAsset>();
                _tempSessionAsset.name = "UnsavedSlashSession";
                _tempSessionAsset.hideFlags = HideFlags.HideAndDontSave;
                SlashEffectPresets.ApplyPreset(_tempSessionAsset, "ThinArc", pathOnly: false);
            }
            _asset = _tempSessionAsset;
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is SlashEffectAsset selected && selected != _asset)
            {
                _asset = selected;
                _asset.EnsureValid();
                _selectedLayerIndex = -1;
                _currentFrame = Mathf.Clamp(_currentFrame, 0, Mathf.Max(0, _asset.playback.frameCount - 1));
                _previewDirty = true;
                Repaint();
            }
        }

        private void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            double dt = System.Math.Min(0.1, now - _lastUpdateTime);
            _lastUpdateTime = now;

            if (_asset == null) return;

            bool needsRepaint = false;

            if (_isPlaying)
            {
                float fps = Mathf.Max(1f, _asset.playback.fps) * Mathf.Max(0.05f, _playbackSpeed);
                double frameDur = 1.0 / fps;
                _frameAccumulator += dt;
                if (_frameAccumulator >= frameDur)
                {
                    int steps = Mathf.Max(1, (int)(_frameAccumulator / frameDur));
                    _frameAccumulator %= frameDur;
                    int count = Mathf.Max(1, _asset.playback.frameCount);
                    _currentFrame = (_currentFrame + steps) % count;
                    _previewDirty = true;
                    needsRepaint = true;

                    if (SlashSceneOverlay.Enabled)
                    {
                        SlashSceneOverlay.ActiveAsset = _asset;
                        SlashSceneOverlay.PreviewFrame = _currentFrame;
                        SlashSceneOverlay.MarkDirty();
                    }
                }
            }

            if (_previewDirty || _previewRT == null || !_previewRT.IsCreated())
            {
                UpdatePreviewTexturesIfNeeded();
                needsRepaint = true;
            }

            if (needsRepaint)
            {
                Repaint();
            }
        }

        /// <summary>
        /// Renders offscreen preview RenderTextures strictly OUTSIDE OnGUI()
        /// so the EditorWindow IMGUI render target is never disturbed.
        /// </summary>
        private void UpdatePreviewTexturesIfNeeded()
        {
            if (_asset == null || _renderer == null) return;
            _asset.EnsureValid();
            EnsurePreviewRenderTextures(_asset.canvasWidth, _asset.canvasHeight);

            AlphaOutputMode viewMode = _previewBakeOutputMode
                ? _asset.bakeSettings.alphaOutputMode
                : AlphaOutputMode.StandardAlpha;

            int clampedFrame = Mathf.Clamp(_currentFrame, 0, Mathf.Max(0, _asset.playback.frameCount - 1));
            _renderer.RenderFrame(_asset, clampedFrame, _previewRT, viewMode);

            _hasOnionPrev = false;
            _hasOnionNext = false;
            if (_showOnionSkin)
            {
                int prevF = Mathf.Max(0, clampedFrame - 1);
                int nextF = Mathf.Min(_asset.playback.frameCount - 1, clampedFrame + 1);
                if (prevF != clampedFrame)
                {
                    _renderer.RenderFrame(_asset, prevF, _onionPrevRT, AlphaOutputMode.StandardAlpha);
                    _hasOnionPrev = true;
                }
                if (nextF != clampedFrame)
                {
                    _renderer.RenderFrame(_asset, nextF, _onionNextRT, AlphaOutputMode.StandardAlpha);
                    _hasOnionNext = true;
                }
            }

            _previewDirty = false;
        }

        private void OnGUI()
        {
            EnsureActiveAsset();
            if (_asset == null) return;

            ClampPanelWidthsToWindow();
            DrawTopToolbar();

            Rect leftSplitterRect;
            Rect rightSplitterRect;

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            {
                // 1. Left Panel: Properties (Width, Appearance, Timing)
                DrawLeftPropertyPanel(_leftPanelWidth);

                // Left Resizable Splitter
                leftSplitterRect = DrawVerticalSplitter(_resizingLeft);

                // 2. Center: Interactive GPU Viewport + Bottom Timeline
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                {
                    DrawViewportToolbar();
                    Rect viewportRect = GUILayoutUtility.GetRect(MinCenterViewportWidth, 260f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                    DrawViewport(viewportRect);
                    DrawBottomTimeline();
                }
                EditorGUILayout.EndVertical();

                // Right Resizable Splitter
                rightSplitterRect = DrawVerticalSplitter(_resizingRight);

                // 3. Right Panel: Path, Combo Layers, SpriteSheet Bake & ParticleSystem Output
                DrawRightPathAndExportPanel(_rightPanelWidth);
            }
            EditorGUILayout.EndHorizontal();

            HandlePanelSplitterEvents(leftSplitterRect, rightSplitterRect);

            DrawStatusBar();
        }

        private void ClampPanelWidthsToWindow()
        {
            float available = Mathf.Max(MinSidePanelWidth * 2f + MinCenterViewportWidth, position.width - SplitterWidth * 2f);
            float maxLeft = Mathf.Max(MinSidePanelWidth, available - MinCenterViewportWidth - _rightPanelWidth);
            _leftPanelWidth = Mathf.Clamp(_leftPanelWidth, MinSidePanelWidth, maxLeft);

            float maxRight = Mathf.Max(MinSidePanelWidth, available - MinCenterViewportWidth - _leftPanelWidth);
            _rightPanelWidth = Mathf.Clamp(_rightPanelWidth, MinSidePanelWidth, maxRight);
        }

        private Rect DrawVerticalSplitter(bool isDragging)
        {
            Rect rect = GUILayoutUtility.GetRect(SplitterWidth, 10f, GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true));

            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = rect.Contains(Event.current.mousePosition);
                EditorGUI.DrawRect(rect, new Color(0.14f, 0.15f, 0.18f, 1f));
                Rect lineRect = new Rect(rect.x + Mathf.Floor(rect.width * 0.5f), rect.y, isDragging ? 2f : 1f, rect.height);
                Color lineCol = (isDragging || hovered)
                    ? new Color(0.22f, 0.74f, 0.97f, 0.95f)
                    : new Color(0.08f, 0.09f, 0.11f, 1f);
                EditorGUI.DrawRect(lineRect, lineCol);
            }

            Rect cursorRect = new Rect(rect.x - 2f, rect.y, rect.width + 4f, rect.height);
            EditorGUIUtility.AddCursorRect(cursorRect, MouseCursor.ResizeHorizontal);
            return rect;
        }

        private void HandlePanelSplitterEvents(Rect leftSplitterRect, Rect rightSplitterRect)
        {
            Event e = Event.current;
            Rect leftHit = new Rect(leftSplitterRect.x - 2f, leftSplitterRect.y, leftSplitterRect.width + 4f, leftSplitterRect.height);
            Rect rightHit = new Rect(rightSplitterRect.x - 2f, rightSplitterRect.y, rightSplitterRect.width + 4f, rightSplitterRect.height);

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (leftHit.Contains(e.mousePosition))
                {
                    if (e.clickCount == 2)
                    {
                        _leftPanelWidth = DefaultLeftPanelWidth;
                        EditorPrefs.SetFloat(PrefLeftPanelWidth, _leftPanelWidth);
                        ClampPanelWidthsToWindow();
                    }
                    else
                    {
                        _resizingLeft = true;
                    }
                    e.Use();
                    Repaint();
                }
                else if (rightHit.Contains(e.mousePosition))
                {
                    if (e.clickCount == 2)
                    {
                        _rightPanelWidth = DefaultRightPanelWidth;
                        EditorPrefs.SetFloat(PrefRightPanelWidth, _rightPanelWidth);
                        ClampPanelWidthsToWindow();
                    }
                    else
                    {
                        _resizingRight = true;
                    }
                    e.Use();
                    Repaint();
                }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_resizingLeft)
                {
                    float maxLeft = Mathf.Max(MinSidePanelWidth, position.width - SplitterWidth * 2f - MinCenterViewportWidth - _rightPanelWidth);
                    _leftPanelWidth = Mathf.Clamp(e.mousePosition.x, MinSidePanelWidth, maxLeft);
                    e.Use();
                    Repaint();
                }
                else if (_resizingRight)
                {
                    float maxRight = Mathf.Max(MinSidePanelWidth, position.width - SplitterWidth * 2f - MinCenterViewportWidth - _leftPanelWidth);
                    _rightPanelWidth = Mathf.Clamp(position.width - e.mousePosition.x, MinSidePanelWidth, maxRight);
                    e.Use();
                    Repaint();
                }
            }
            else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                if (_resizingLeft)
                {
                    _resizingLeft = false;
                    EditorPrefs.SetFloat(PrefLeftPanelWidth, _leftPanelWidth);
                    e.Use();
                    Repaint();
                }
                if (_resizingRight)
                {
                    _resizingRight = false;
                    EditorPrefs.SetFloat(PrefRightPanelWidth, _rightPanelWidth);
                    e.Use();
                    Repaint();
                }
            }
        }

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Asset:", EditorStyles.miniBoldLabel, GUILayout.Width(42f));
            var picked = (SlashEffectAsset)EditorGUILayout.ObjectField(
                _asset == _tempSessionAsset ? null : _asset,
                typeof(SlashEffectAsset),
                false,
                GUILayout.Width(190f));
            if (picked != null && picked != _asset)
            {
                _asset = picked;
                _asset.EnsureValid();
                _selectedLayerIndex = -1;
                _previewDirty = true;
                _statusMessage = $"Loaded asset: {_asset.name}";
            }

            if (GUILayout.Button("New Asset", EditorStyles.toolbarButton, GUILayout.Width(74f)))
            {
                CreateNewPersistentAsset();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Preset:", EditorStyles.miniLabel, GUILayout.Width(42f));
            _selectedPresetIndex = EditorGUILayout.Popup(_selectedPresetIndex, SlashEffectPresets.PresetNames, EditorStyles.toolbarPopup, GUILayout.Width(120f));
            if (GUILayout.Button("Load Full", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                Undo.RecordObject(_asset, "Load Slash Preset (Full)");
                SlashEffectPresets.ApplyPreset(_asset, SlashEffectPresets.PresetNames[_selectedPresetIndex], pathOnly: false);
                _selectedLayerIndex = -1;
                MarkAssetDirty();
                _statusMessage = $"Loaded preset (Full): {SlashEffectPresets.PresetNames[_selectedPresetIndex]}";
            }
            if (GUILayout.Button("Path Only", EditorStyles.toolbarButton, GUILayout.Width(66f)))
            {
                Undo.RecordObject(_asset, "Load Slash Preset (Path Only)");
                SlashEffectPresets.ApplyPreset(_asset, SlashEffectPresets.PresetNames[_selectedPresetIndex], pathOnly: true);
                MarkAssetDirty();
                _statusMessage = $"Loaded preset path: {SlashEffectPresets.PresetNames[_selectedPresetIndex]}";
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Import .slash.json", EditorStyles.toolbarButton, GUILayout.Width(112f)))
            {
                string file = EditorUtility.OpenFilePanel("Import Slash Effect JSON", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(file))
                {
                    Undo.RecordObject(_asset, "Import .slash.json");
                    if (SlashEffectJsonInterop.ImportFromJsonFile(_asset, file))
                    {
                        MarkAssetDirty();
                        _statusMessage = $"Imported: {Path.GetFileName(file)}";
                    }
                }
            }

            if (GUILayout.Button("Export .slash.json", EditorStyles.toolbarButton, GUILayout.Width(112f)))
            {
                string file = EditorUtility.SaveFilePanel("Export Slash Effect JSON", Application.dataPath, $"{_asset.effectName}.slash", "json");
                if (!string.IsNullOrEmpty(file))
                {
                    SlashEffectJsonInterop.ExportToJsonFile(_asset, file);
                    _statusMessage = $"Exported JSON: {Path.GetFileName(file)}";
                }
            }

            GUILayout.Space(6f);
            bool newSceneOverlay = GUILayout.Toggle(SlashSceneOverlay.Enabled, "Scene Overlay", EditorStyles.toolbarButton, GUILayout.Width(92f));
            if (newSceneOverlay != SlashSceneOverlay.Enabled)
            {
                SlashSceneOverlay.Enabled = newSceneOverlay;
                SlashSceneOverlay.ActiveAsset = _asset;
                SlashSceneOverlay.PreviewFrame = _currentFrame;
                SlashSceneOverlay.FlipX = _dummyFlipX;
                SlashSceneOverlay.MarkDirty();
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPropertyPanel(float width)
        {
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(width * 0.44f, 105f, 185f);

            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, false, false);

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(_asset, "Edit Slash Properties");

            // --- SECTION 1: WIDTH PROFILE ---
            _foldWidth = EditorGUILayout.BeginFoldoutHeaderGroup(_foldWidth, "1. Ribbon Width (Dual-Curve)");
            if (_foldWidth)
            {
                EditorGUI.indentLevel++;
                _asset.width.minWidth = EditorGUILayout.Slider("Min Width (px)", _asset.width.minWidth, 0f, 150f);
                _asset.width.maxWidth = EditorGUILayout.Slider("Max Width (px)", _asset.width.maxWidth, 2f, 300f);
                _asset.width.symmetric = EditorGUILayout.Toggle("Symmetric Blade", _asset.width.symmetric);
                if (!_asset.width.symmetric)
                {
                    _asset.width.innerWidthRatio = EditorGUILayout.Slider("Inner Edge Ratio", _asset.width.innerWidthRatio, 0.05f, 1f);
                }

                EditorGUILayout.Space(2);
                _asset.width.progressWidthCurve = EditorGUILayout.CurveField("Progress Head Width", _asset.width.progressWidthCurve, Color.cyan, new Rect(0, 0, 1, 1));
                _asset.width.widthCurve = EditorGUILayout.CurveField("Tail -> Head Shape", _asset.width.widthCurve, new Color(0.4f, 0.85f, 1f), new Rect(0, 0, 1, 1));

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                if (GUILayout.Button("Comet", EditorStyles.miniButtonLeft)) _asset.width.widthCurve = SlashEffectPresets.CurveCometSlash();
                if (GUILayout.Button("Katana", EditorStyles.miniButtonMid)) _asset.width.widthCurve = SlashEffectPresets.CurveKatanaRazor();
                if (GUILayout.Button("Heavy", EditorStyles.miniButtonMid)) _asset.width.widthCurve = SlashEffectPresets.CurveHeavyGreatsword();
                if (GUILayout.Button("Crescent", EditorStyles.miniButtonRight)) _asset.width.widthCurve = SlashEffectPresets.CurveBellCrescent();
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // --- SECTION 2: APPEARANCE & GLOW ---
            _foldAppearance = EditorGUILayout.BeginFoldoutHeaderGroup(_foldAppearance, "2. Appearance, Core & Glow");
            if (_foldAppearance)
            {
                var app = _asset.appearance;
                EditorGUI.indentLevel++;
                app.colorGradient = EditorGUILayout.GradientField("Color Ramp (Tail->Head)", app.colorGradient);
                app.baseAlpha = EditorGUILayout.Slider("Base Alpha", app.baseAlpha, 0.05f, 1f);
                app.enableTailAlphaFade = EditorGUILayout.Toggle("Tail Alpha Fade", app.enableTailAlphaFade);
                if (app.enableTailAlphaFade)
                {
                    app.tailFadeRatio = EditorGUILayout.Slider("Tail Fade Ratio", app.tailFadeRatio, 0.05f, 0.95f);
                }
                app.edgeSoftness = EditorGUILayout.Slider("Edge Softness", app.edgeSoftness, 0f, 0.45f);
                app.blendMode = (SlashBlendMode)EditorGUILayout.EnumPopup("Blend Mode", app.blendMode);

                EditorGUILayout.Space(3);
                app.enableCore = EditorGUILayout.Toggle("Enable Inner Core", app.enableCore);
                if (app.enableCore)
                {
                    app.coreColor = EditorGUILayout.ColorField("Core Color", app.coreColor);
                    app.coreWidthRatio = EditorGUILayout.Slider("Core Width Ratio", app.coreWidthRatio, 0.05f, 0.8f);
                    app.coreAlpha = EditorGUILayout.Slider("Core Alpha", app.coreAlpha, 0.05f, 1f);
                }

                EditorGUILayout.Space(3);
                app.glowBlur = EditorGUILayout.Slider("Glow Radius (px)", app.glowBlur, 0f, 48f);
                if (app.glowBlur > 0.1f)
                {
                    app.glowColor = EditorGUILayout.ColorField("Glow Color", app.glowColor);
                    app.glowIntensity = EditorGUILayout.Slider("Glow Intensity", app.glowIntensity, 0f, 2.5f);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // --- SECTION 3: PATTERNS, TOON, PIXEL ART, PHANTOM ---
            _foldEffects = EditorGUILayout.BeginFoldoutHeaderGroup(_foldEffects, "3. Style (Pattern / Toon / Pixel / Echo)");
            if (_foldEffects)
            {
                var app = _asset.appearance;
                EditorGUI.indentLevel++;

                app.patternType = (SlashPatternType)EditorGUILayout.EnumPopup("Trail Pattern", app.patternType);
                if (app.patternType != SlashPatternType.None)
                {
                    app.patternIntensity = EditorGUILayout.Slider("Pattern Intensity", app.patternIntensity, 0.05f, 1f);
                    app.patternScale = EditorGUILayout.Slider("Pattern Scale", app.patternScale, 0.25f, 4f);
                }

                EditorGUILayout.Space(2);
                app.enableToonOutline = EditorGUILayout.Toggle("Toon Outline", app.enableToonOutline);
                if (app.enableToonOutline)
                {
                    app.toonOutlineWidth = EditorGUILayout.Slider("Outline Width (px)", app.toonOutlineWidth, 1f, 12f);
                    app.toonOutlineColor = EditorGUILayout.ColorField("Outline Color", app.toonOutlineColor);
                }

                EditorGUILayout.Space(2);
                app.enablePixelArt = EditorGUILayout.Toggle("Retro Pixel Art", app.enablePixelArt);
                if (app.enablePixelArt)
                {
                    app.pixelArtSize = EditorGUILayout.IntSlider("Pixel Block Size", app.pixelArtSize, 2, 16);
                    app.pixelArtAlphaCutoff = EditorGUILayout.Slider("Alpha Cutoff", app.pixelArtAlphaCutoff, 0.05f, 0.85f);
                }

                EditorGUILayout.Space(2);
                app.enablePhantom = EditorGUILayout.Toggle("Phantom Echo", app.enablePhantom);
                if (app.enablePhantom)
                {
                    app.phantomCount = EditorGUILayout.IntSlider("Echo Count", app.phantomCount, 1, 3);
                    app.phantomDelay = EditorGUILayout.Slider("Frame Delay", app.phantomDelay, 0.2f, 3f);
                    app.phantomAlpha = EditorGUILayout.Slider("Echo Alpha", app.phantomAlpha, 0.05f, 0.9f);
                    app.phantomScale = EditorGUILayout.Slider("Echo Width Scale", app.phantomScale, 0.4f, 1.6f);
                    app.phantomColor = EditorGUILayout.ColorField("Echo Color", app.phantomColor);
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // --- SECTION 4: TIMING & CURVES ---
            _foldTiming = EditorGUILayout.BeginFoldoutHeaderGroup(_foldTiming, "4. Timing & Playback");
            if (_foldTiming)
            {
                var pb = _asset.playback;
                EditorGUI.indentLevel++;
                pb.frameCount = EditorGUILayout.IntSlider("Frame Count", pb.frameCount, 2, 64);
                pb.fps = EditorGUILayout.Slider("FPS", pb.fps, 6f, 60f);
                pb.mode = (RibbonMode)EditorGUILayout.EnumPopup("Ribbon Mode", pb.mode);
                if (pb.mode == RibbonMode.Progressive)
                {
                    pb.tailLength = EditorGUILayout.Slider("Tail Length", pb.tailLength, 0.05f, 1f);
                    pb.finishMode = (FinishMode)EditorGUILayout.EnumPopup("Finish Mode", pb.finishMode);
                }
                pb.timeCurve = EditorGUILayout.CurveField("Time Curve", pb.timeCurve, Color.yellow, new Rect(0, 0, 1, 1));
                pb.fadeCurve = EditorGUILayout.CurveField("Fade Curve", pb.fadeCurve, new Color(1f, 0.55f, 0.2f), new Rect(0, 0, 1, 1));

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                if (GUILayout.Button("EaseInOut", EditorStyles.miniButtonLeft)) pb.timeCurve = SlashEffectPresets.CurveEaseInOut();
                if (GUILayout.Button("Snap Expo", EditorStyles.miniButtonMid)) pb.timeCurve = SlashEffectPresets.CurveEaseOutExpo();
                if (GUILayout.Button("FlashHold", EditorStyles.miniButtonRight)) pb.fadeCurve = SlashEffectPresets.CurveFlashHoldDrop();
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (EditorGUI.EndChangeCheck())
            {
                _currentFrame = Mathf.Clamp(_currentFrame, 0, Mathf.Max(0, _asset.playback.frameCount - 1));
                MarkAssetDirty();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUIUtility.labelWidth = prevLabelWidth;
        }

        private void DrawViewportToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            _bgMode = (BgMode)EditorGUILayout.EnumPopup(_bgMode, EditorStyles.toolbarPopup, GUILayout.Width(72f));
            _showGuides = GUILayout.Toggle(_showGuides, "Guides", EditorStyles.toolbarButton, GUILayout.Width(54f));
            if (_showGuides)
            {
                _guideOpacity = GUILayout.HorizontalSlider(_guideOpacity, 0.1f, 1f, GUILayout.Width(52f));
            }
            bool nextOnion = GUILayout.Toggle(_showOnionSkin, "Onion", EditorStyles.toolbarButton, GUILayout.Width(48f));
            if (nextOnion != _showOnionSkin)
            {
                _showOnionSkin = nextOnion;
                _previewDirty = true;
            }
            _showDummyCharacter = GUILayout.Toggle(_showDummyCharacter, "XY Hero", EditorStyles.toolbarButton, GUILayout.Width(60f));
            if (_showDummyCharacter)
            {
                bool nextFlip = GUILayout.Toggle(_dummyFlipX, _dummyFlipX ? "Face -X" : "Face +X", EditorStyles.toolbarButton, GUILayout.Width(58f));
                if (nextFlip != _dummyFlipX)
                {
                    _dummyFlipX = nextFlip;
                    SlashSceneOverlay.FlipX = _dummyFlipX;
                    SceneView.RepaintAll();
                }
            }
            bool nextPreviewBake = GUILayout.Toggle(_previewBakeOutputMode, "Bake Mode", EditorStyles.toolbarButton, GUILayout.Width(72f));
            if (nextPreviewBake != _previewBakeOutputMode)
            {
                _previewBakeOutputMode = nextPreviewBake;
                _previewDirty = true;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"{Mathf.RoundToInt(_zoom * 100f)}%", EditorStyles.miniLabel, GUILayout.Width(38f));
            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                _zoom = 1f;
                _panOffset = Vector2.zero;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawViewport(Rect vpRect)
        {
            EditorGUI.DrawRect(vpRect, new Color(0.11f, 0.12f, 0.15f, 1f));
            GUI.BeginClip(vpRect);
            Rect localVp = new Rect(0, 0, vpRect.width, vpRect.height);

            // Calculate canvas display rectangle inside viewport
            float baseScale = Mathf.Min((localVp.width - 60f) / _asset.canvasWidth, (localVp.height - 60f) / _asset.canvasHeight);
            baseScale = Mathf.Max(0.1f, baseScale);
            float scale = baseScale * _zoom;

            float dispW = _asset.canvasWidth * scale;
            float dispH = _asset.canvasHeight * scale;
            Rect canvasRect = new Rect(
                (localVp.width - dispW) * 0.5f + _panOffset.x,
                (localVp.height - dispH) * 0.5f + _panOffset.y,
                dispW,
                dispH
            );

            // Draw canvas background
            if (_bgMode == BgMode.Checker && _checkerTex != null)
            {
                GUI.DrawTextureWithTexCoords(canvasRect, _checkerTex, new Rect(0, 0, dispW / 24f, dispH / 24f));
            }
            else if (_bgMode == BgMode.Dark)
            {
                EditorGUI.DrawRect(canvasRect, new Color(0.06f, 0.07f, 0.09f, 1f));
            }
            else
            {
                EditorGUI.DrawRect(canvasRect, new Color(0.92f, 0.93f, 0.95f, 1f));
            }

            // Optional XY Side-Scroller Dummy Character Silhouette
            if (_showDummyCharacter && Event.current.type == EventType.Repaint)
            {
                DrawDummySideScrollerCharacter(canvasRect, scale);
            }

            // Draw pre-rendered GPU Slash Frame (rendered outside OnGUI in UpdatePreviewTexturesIfNeeded)
            if (Event.current.type == EventType.Repaint)
            {
                if (_showOnionSkin)
                {
                    if (_hasOnionPrev && _onionPrevRT != null)
                    {
                        var prevCol = GUI.color;
                        GUI.color = new Color(0.25f, 0.85f, 1f, 0.28f);
                        GUI.DrawTexture(canvasRect, _onionPrevRT, ScaleMode.StretchToFill, true);
                        GUI.color = prevCol;
                    }
                    if (_hasOnionNext && _onionNextRT != null)
                    {
                        var prevCol = GUI.color;
                        GUI.color = new Color(1f, 0.35f, 0.45f, 0.28f);
                        GUI.DrawTexture(canvasRect, _onionNextRT, ScaleMode.StretchToFill, true);
                        GUI.color = prevCol;
                    }
                }

                if (_previewRT != null)
                {
                    GUI.DrawTexture(canvasRect, _previewRT, ScaleMode.StretchToFill, true);
                }

                // Canvas boundary border & Bezier guides (NO Handles.BeginGUI/EndGUI in EditorWindow!)
                var prevHandleCol = Handles.color;
                Handles.color = new Color(0.45f, 0.55f, 0.7f, 0.6f);
                Handles.DrawSolidRectangleWithOutline(canvasRect, Color.clear, new Color(0.45f, 0.55f, 0.75f, 0.7f));

                if (_showGuides)
                {
                    DrawBezierGuides(canvasRect, scale);
                }
                Handles.color = prevHandleCol;
            }

            // Handle Viewport Mouse Input (drag handles / pivot / zoom / pan)
            HandleViewportEvents(localVp, canvasRect, scale);

            GUI.EndClip();
        }

        private Vector2 CanvasToGui(Vector2 canvasPxYUp, Rect canvasRect, float scale)
        {
            return new Vector2(
                canvasRect.x + canvasPxYUp.x * scale,
                canvasRect.y + (_asset.canvasHeight - canvasPxYUp.y) * scale
            );
        }

        private Vector2 GuiToCanvas(Vector2 guiPos, Rect canvasRect, float scale)
        {
            float cx = (guiPos.x - canvasRect.x) / scale;
            float cy = _asset.canvasHeight - (guiPos.y - canvasRect.y) / scale;
            return new Vector2(cx, cy);
        }

        private SlashPathData GetActiveEditingPath()
        {
            if (_selectedLayerIndex >= 0 && _asset.extraSlashes != null && _selectedLayerIndex < _asset.extraSlashes.Count)
            {
                return _asset.extraSlashes[_selectedLayerIndex].path;
            }
            return _asset.path;
        }

        private void DrawDummySideScrollerCharacter(Rect canvasRect, float scale)
        {
            Vector2 pivotCanvas = new Vector2(_asset.canvasWidth * _asset.pivot.x, _asset.canvasHeight * _asset.pivot.y);
            Vector2 p = CanvasToGui(pivotCanvas, canvasRect, scale);
            float dir = _dummyFlipX ? -1f : 1f;
            float s = scale * _dummyScale;

            var prevHandleCol = Handles.color;
            Color bodyCol = new Color(0.45f, 0.55f, 0.68f, 0.38f);
            Color swordCol = new Color(0.95f, 0.85f, 0.45f, 0.45f);

            // Ground line
            Handles.color = new Color(0.4f, 0.75f, 0.5f, 0.35f);
            Handles.DrawLine(new Vector3(p.x - 120f * s, p.y + 42f * s, 0), new Vector3(p.x + 120f * s, p.y + 42f * s, 0));

            // Head
            Handles.color = bodyCol;
            Handles.DrawSolidDisc(new Vector3(p.x - 28f * dir * s, p.y - 54f * s, 0), Vector3.forward, 13f * s);

            // Torso
            Handles.DrawAAConvexPolygon(
                new Vector3(p.x - 40f * dir * s, p.y - 40f * s, 0),
                new Vector3(p.x - 16f * dir * s, p.y - 38f * s, 0),
                new Vector3(p.x - 20f * dir * s, p.y - 2f * s, 0),
                new Vector3(p.x - 36f * dir * s, p.y - 2f * s, 0)
            );

            // Legs
            Handles.DrawAAPolyLine(4f,
                new Vector3(p.x - 24f * dir * s, p.y - 2f * s, 0),
                new Vector3(p.x - 10f * dir * s, p.y + 20f * s, 0),
                new Vector3(p.x - 14f * dir * s, p.y + 42f * s, 0));
            Handles.DrawAAPolyLine(4f,
                new Vector3(p.x - 32f * dir * s, p.y - 2f * s, 0),
                new Vector3(p.x - 46f * dir * s, p.y + 20f * s, 0),
                new Vector3(p.x - 52f * dir * s, p.y + 42f * s, 0));

            // Weapon Arm extending toward pivot
            Handles.DrawAAPolyLine(3.5f,
                new Vector3(p.x - 20f * dir * s, p.y - 30f * s, 0),
                new Vector3(p.x - 4f * dir * s, p.y - 16f * s, 0),
                new Vector3(p.x + 6f * dir * s, p.y - 8f * s, 0));

            // Sword blade pointing in facing direction (+X / -X)
            Handles.color = swordCol;
            Handles.DrawAAPolyLine(3f,
                new Vector3(p.x + 6f * dir * s, p.y - 8f * s, 0),
                new Vector3(p.x + 62f * dir * s, p.y + 2f * s, 0));

            Handles.color = prevHandleCol;
        }

        private void DrawBezierGuides(Rect canvasRect, float scale)
        {
            float op = Mathf.Clamp(_guideOpacity, 0.1f, 1f);

            // Draw ghost paths for unselected layers
            if (_asset.extraSlashes != null && _asset.extraSlashes.Count > 0)
            {
                if (_selectedLayerIndex >= 0)
                {
                    DrawPathSegments(_asset.path, canvasRect, scale, new Color(0.22f, 0.74f, 0.97f, 0.32f * op), 1.5f);
                }
                for (int l = 0; l < _asset.extraSlashes.Count; l++)
                {
                    if (l == _selectedLayerIndex || !_asset.extraSlashes[l].enabled) continue;
                    DrawPathSegments(_asset.extraSlashes[l].path, canvasRect, scale, new Color(0.96f, 0.25f, 0.37f, 0.32f * op), 1.5f);
                }
            }

            var path = GetActiveEditingPath();
            if (path == null || path.anchors == null) return;

            // Active Bezier curve
            DrawPathSegments(path, canvasRect, scale, new Color(1f, 1f, 1f, 0.85f * op), 2f);

            // Control arms & handles
            for (int i = 0; i < path.anchors.Count; i++)
            {
                var a = path.anchors[i];
                Vector2 ptGui = CanvasToGui(a.point, canvasRect, scale);

                if (a.hasHandleIn)
                {
                    Vector2 inGui = CanvasToGui(a.handleIn, canvasRect, scale);
                    Handles.color = new Color(0.96f, 0.45f, 0.71f, 0.7f * op);
                    Handles.DrawDottedLine(ptGui, inGui, 3f);
                    DrawHandleBox(inGui, 5f, new Color(0.96f, 0.45f, 0.71f, op));
                }

                if (a.hasHandleOut)
                {
                    Vector2 outGui = CanvasToGui(a.handleOut, canvasRect, scale);
                    Handles.color = new Color(0.38f, 0.65f, 0.98f, 0.7f * op);
                    Handles.DrawDottedLine(ptGui, outGui, 3f);
                    DrawHandleBox(outGui, 5f, new Color(0.38f, 0.65f, 0.98f, op));
                }

                bool isStart = i == 0;
                bool isEnd = i == path.anchors.Count - 1;
                Color anchorCol = isStart ? new Color(0.23f, 0.51f, 0.96f, op)
                    : isEnd ? new Color(0.93f, 0.28f, 0.60f, op)
                    : new Color(0.06f, 0.73f, 0.51f, op);

                Handles.color = anchorCol;
                Handles.DrawSolidDisc(ptGui, Vector3.forward, 6.5f);
                Handles.color = new Color(1f, 1f, 1f, op);
                Handles.DrawWireDisc(ptGui, Vector3.forward, 6.5f);

                string label = isStart ? "Start" : isEnd ? "End" : $"P{i + 1}";
                GUI.Label(new Rect(ptGui.x + 8f, ptGui.y - 16f, 52f, 16f), label, EditorStyles.whiteMiniLabel);
            }

            // Pivot Gizmo
            Vector2 pivGui = CanvasToGui(new Vector2(_asset.canvasWidth * _asset.pivot.x, _asset.canvasHeight * _asset.pivot.y), canvasRect, scale);
            Handles.color = new Color(0.98f, 0.80f, 0.08f, op);
            Handles.DrawWireDisc(pivGui, Vector3.forward, 8f);
            Handles.DrawLine(pivGui + new Vector2(-12f, 0f), pivGui + new Vector2(12f, 0f));
            Handles.DrawLine(pivGui + new Vector2(0f, -12f), pivGui + new Vector2(0f, 12f));
            GUI.Label(new Rect(pivGui.x + 10f, pivGui.y + 4f, 110f, 16f), $"Pivot ({_asset.pivot.x:F2},{_asset.pivot.y:F2})", EditorStyles.whiteMiniLabel);
        }

        private void DrawPathSegments(SlashPathData path, Rect canvasRect, float scale, Color col, float thickness)
        {
            if (path == null || path.anchors == null || path.anchors.Count < 2) return;
            for (int i = 0; i < path.anchors.Count - 1; i++)
            {
                var a0 = path.anchors[i];
                var a1 = path.anchors[i + 1];
                Vector2 p0 = CanvasToGui(a0.point, canvasRect, scale);
                Vector2 p1 = CanvasToGui(a0.hasHandleOut ? a0.handleOut : a0.point, canvasRect, scale);
                Vector2 p2 = CanvasToGui(a1.hasHandleIn ? a1.handleIn : a1.point, canvasRect, scale);
                Vector2 p3 = CanvasToGui(a1.point, canvasRect, scale);
                Handles.DrawBezier(p0, p3, p1, p2, col, null, thickness);
            }
        }

        private static void DrawHandleBox(Vector2 center, float halfSize, Color fill)
        {
            Rect r = new Rect(center.x - halfSize, center.y - halfSize, halfSize * 2f, halfSize * 2f);
            Handles.DrawSolidRectangleWithOutline(r, fill, Color.white);
        }

        private void HandleViewportEvents(Rect localVp, Rect canvasRect, float scale)
        {
            if (_resizingLeft || _resizingRight) return;

            Event e = Event.current;
            if (!localVp.Contains(e.mousePosition) && string.IsNullOrEmpty(_draggingHandleId) && !_isPanning) return;

            if (e.type == EventType.ScrollWheel && localVp.Contains(e.mousePosition))
            {
                float zoomFactor = e.delta.y > 0 ? 0.9f : 1.11f;
                _zoom = Mathf.Clamp(_zoom * zoomFactor, 0.15f, 4f);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && (e.button == 2 || (e.button == 0 && e.alt)))
            {
                _isPanning = true;
                _lastMousePos = e.mousePosition;
                e.Use();
                return;
            }

            if (_isPanning && e.type == EventType.MouseDrag)
            {
                _panOffset += e.mousePosition - _lastMousePos;
                _lastMousePos = e.mousePosition;
                e.Use();
                Repaint();
                return;
            }

            if (_isPanning && e.type == EventType.MouseUp)
            {
                _isPanning = false;
                e.Use();
                return;
            }

            if (!_showGuides) return;

            var path = GetActiveEditingPath();
            if (path == null || path.anchors == null) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                const float hitRadius = 11f;

                // 1. Check Pivot
                if (!_pivotLocked)
                {
                    Vector2 pivGui = CanvasToGui(new Vector2(_asset.canvasWidth * _asset.pivot.x, _asset.canvasHeight * _asset.pivot.y), canvasRect, scale);
                    if (Vector2.Distance(e.mousePosition, pivGui) <= hitRadius)
                    {
                        _draggingHandleId = "pivot";
                        Undo.RecordObject(_asset, "Move Slash Pivot");
                        e.Use();
                        return;
                    }
                }

                // 2. Check Control Handles & Anchors
                for (int i = 0; i < path.anchors.Count; i++)
                {
                    var a = path.anchors[i];
                    if (a.hasHandleIn && Vector2.Distance(e.mousePosition, CanvasToGui(a.handleIn, canvasRect, scale)) <= hitRadius)
                    {
                        _draggingHandleId = $"in_{i}";
                        Undo.RecordObject(_asset, "Move Bezier Handle");
                        e.Use();
                        return;
                    }
                    if (a.hasHandleOut && Vector2.Distance(e.mousePosition, CanvasToGui(a.handleOut, canvasRect, scale)) <= hitRadius)
                    {
                        _draggingHandleId = $"out_{i}";
                        Undo.RecordObject(_asset, "Move Bezier Handle");
                        e.Use();
                        return;
                    }
                    if (Vector2.Distance(e.mousePosition, CanvasToGui(a.point, canvasRect, scale)) <= hitRadius)
                    {
                        _draggingHandleId = $"anchor_{i}";
                        Undo.RecordObject(_asset, "Move Bezier Anchor");
                        e.Use();
                        return;
                    }
                }
            }
            else if (e.type == EventType.MouseDrag && !string.IsNullOrEmpty(_draggingHandleId))
            {
                Vector2 canvasPt = GuiToCanvas(e.mousePosition, canvasRect, scale);

                if (_draggingHandleId == "pivot")
                {
                    _asset.pivot = new Vector2(
                        Mathf.Clamp01(canvasPt.x / _asset.canvasWidth),
                        Mathf.Clamp01(canvasPt.y / _asset.canvasHeight)
                    );
                }
                else
                {
                    string[] parts = _draggingHandleId.Split('_');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int idx) && idx >= 0 && idx < path.anchors.Count)
                    {
                        var a = path.anchors[idx];
                        if (parts[0] == "anchor")
                        {
                            Vector2 delta = canvasPt - a.point;
                            a.point = canvasPt;
                            if (a.hasHandleIn) a.handleIn += delta;
                            if (a.hasHandleOut) a.handleOut += delta;
                        }
                        else if (parts[0] == "in")
                        {
                            a.handleIn = canvasPt;
                        }
                        else if (parts[0] == "out")
                        {
                            a.handleOut = canvasPt;
                        }
                        path.anchors[idx] = a;
                    }
                }

                MarkAssetDirty();
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && !string.IsNullOrEmpty(_draggingHandleId))
            {
                _draggingHandleId = null;
                e.Use();
            }
        }

        private void DrawBottomTimeline()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(28f));

            if (GUILayout.Button(_isPlaying ? "❚❚ Pause" : "▶ Play", EditorStyles.toolbarButton, GUILayout.Width(68f)))
            {
                _isPlaying = !_isPlaying;
            }
            if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(26f)))
            {
                _isPlaying = false;
                _currentFrame = (_currentFrame - 1 + _asset.playback.frameCount) % Mathf.Max(1, _asset.playback.frameCount);
                _previewDirty = true;
                if (SlashSceneOverlay.Enabled)
                {
                    SlashSceneOverlay.PreviewFrame = _currentFrame;
                    SlashSceneOverlay.MarkDirty();
                }
            }
            if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(26f)))
            {
                _isPlaying = false;
                _currentFrame = (_currentFrame + 1) % Mathf.Max(1, _asset.playback.frameCount);
                _previewDirty = true;
                if (SlashSceneOverlay.Enabled)
                {
                    SlashSceneOverlay.PreviewFrame = _currentFrame;
                    SlashSceneOverlay.MarkDirty();
                }
            }

            GUILayout.Label($"Frame {_currentFrame + 1}/{_asset.playback.frameCount}", EditorStyles.miniBoldLabel, GUILayout.Width(80f));

            int nextFrame = Mathf.RoundToInt(GUILayout.HorizontalSlider(_currentFrame, 0, Mathf.Max(0, _asset.playback.frameCount - 1), GUILayout.ExpandWidth(true)));
            if (nextFrame != _currentFrame)
            {
                _currentFrame = nextFrame;
                _isPlaying = false;
                _previewDirty = true;
                if (SlashSceneOverlay.Enabled)
                {
                    SlashSceneOverlay.PreviewFrame = _currentFrame;
                    SlashSceneOverlay.MarkDirty();
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Speed:", EditorStyles.miniLabel, GUILayout.Width(40f));
            if (GUILayout.Toggle(Mathf.Approximately(_playbackSpeed, 0.25f), "0.25x", EditorStyles.toolbarButton, GUILayout.Width(40f))) _playbackSpeed = 0.25f;
            if (GUILayout.Toggle(Mathf.Approximately(_playbackSpeed, 0.5f), "0.5x", EditorStyles.toolbarButton, GUILayout.Width(36f))) _playbackSpeed = 0.5f;
            if (GUILayout.Toggle(Mathf.Approximately(_playbackSpeed, 1f), "1x", EditorStyles.toolbarButton, GUILayout.Width(28f))) _playbackSpeed = 1f;
            if (GUILayout.Toggle(Mathf.Approximately(_playbackSpeed, 2f), "2x", EditorStyles.toolbarButton, GUILayout.Width(28f))) _playbackSpeed = 2f;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRightPathAndExportPanel(float width)
        {
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(width * 0.44f, 105f, 185f);

            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, false, false);

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(_asset, "Edit Path & Bake Settings");

            // --- CANVAS & PATH TRANSFORMS ---
            _foldPath = EditorGUILayout.BeginFoldoutHeaderGroup(_foldPath, "Path, Canvas & Pivot (XY Plane)");
            if (_foldPath)
            {
                EditorGUI.indentLevel++;
                _asset.effectName = EditorGUILayout.TextField("Effect Name", _asset.effectName);

                EditorGUILayout.BeginHorizontal();
                int newW = EditorGUILayout.IntField("Canvas W", _asset.canvasWidth);
                int newH = EditorGUILayout.IntField("H", _asset.canvasHeight);
                EditorGUILayout.EndHorizontal();

                newW = Mathf.Clamp(newW, 32, 2048);
                newH = Mathf.Clamp(newH, 32, 2048);
                if (newW != _asset.canvasWidth || newH != _asset.canvasHeight)
                {
                    Vector2 ratio = new Vector2(newW / (float)_asset.canvasWidth, newH / (float)_asset.canvasHeight);
                    PolyBezierUtility.ScalePath(_asset.path, ratio);
                    if (_asset.extraSlashes != null)
                    {
                        foreach (var sub in _asset.extraSlashes) PolyBezierUtility.ScalePath(sub.path, ratio);
                    }
                    _asset.canvasWidth = newW;
                    _asset.canvasHeight = newH;
                }

                // Quick resolution presets
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                foreach (int res in new[] { 128, 256, 512, 1024 })
                {
                    if (GUILayout.Button($"{res}", EditorStyles.miniButton))
                    {
                        Vector2 ratio = new Vector2(res / (float)_asset.canvasWidth, res / (float)_asset.canvasHeight);
                        PolyBezierUtility.ScalePath(_asset.path, ratio);
                        if (_asset.extraSlashes != null)
                        {
                            foreach (var sub in _asset.extraSlashes) PolyBezierUtility.ScalePath(sub.path, ratio);
                        }
                        _asset.canvasWidth = res;
                        _asset.canvasHeight = res;
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                _asset.pivot = EditorGUILayout.Vector2Field("Pivot (Y-Up)", _asset.pivot);
                _pivotLocked = GUILayout.Toggle(_pivotLocked, _pivotLocked ? "Locked" : "Drag", EditorStyles.miniButton, GUILayout.Width(50f));
                EditorGUILayout.EndHorizontal();

                // Geometric transform buttons for active layer
                var activePath = GetActiveEditingPath();
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                if (GUILayout.Button("Flip X", EditorStyles.miniButtonLeft)) PolyBezierUtility.FlipHorizontal(activePath, _asset.canvasWidth);
                if (GUILayout.Button("Flip Y", EditorStyles.miniButtonMid)) PolyBezierUtility.FlipVertical(activePath, _asset.canvasHeight);
                if (GUILayout.Button("Rot -90°", EditorStyles.miniButtonMid))
                    PolyBezierUtility.RotateAroundPivot(activePath, new Vector2(_asset.canvasWidth * _asset.pivot.x, _asset.canvasHeight * _asset.pivot.y), -90f);
                if (GUILayout.Button("Rot +90°", EditorStyles.miniButtonRight))
                    PolyBezierUtility.RotateAroundPivot(activePath, new Vector2(_asset.canvasWidth * _asset.pivot.x, _asset.canvasHeight * _asset.pivot.y), 90f);
                EditorGUILayout.EndHorizontal();

                // Anchor Add/Remove
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(14);
                if (GUILayout.Button("+ Insert Mid Anchor", EditorStyles.miniButtonLeft))
                {
                    InsertMidAnchor(activePath);
                }
                using (new EditorGUI.DisabledScope(activePath.anchors.Count <= 2))
                {
                    if (GUILayout.Button("- Remove Last Mid", EditorStyles.miniButtonRight) && activePath.anchors.Count > 2)
                    {
                        activePath.anchors.RemoveAt(activePath.anchors.Count - 2);
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Spin Slash Generator
                _foldSpinGen = EditorGUILayout.Foldout(_foldSpinGen, "Spin / Whirlwind Generator (360°~720°)");
                if (_foldSpinGen)
                {
                    _spinSweepDeg = EditorGUILayout.Slider("Sweep Angle", _spinSweepDeg, 180f, 720f);
                    _spinRadiusStart = EditorGUILayout.Slider("Start Radius", _spinRadiusStart, 20f, _asset.canvasWidth * 0.48f);
                    _spinRadiusEnd = EditorGUILayout.Slider("End Radius", _spinRadiusEnd, 20f, _asset.canvasWidth * 0.48f);
                    _spinFlattenY = EditorGUILayout.Slider("Flatten Y", _spinFlattenY, 0.25f, 1f);
                    _spinClockwise = EditorGUILayout.Toggle("Clockwise", _spinClockwise);
                    if (GUILayout.Button("Generate Spin Path"))
                    {
                        var spin = PolyBezierUtility.GenerateSpinSlashPath(
                            new Vector2(_asset.canvasWidth * _asset.pivot.x, _asset.canvasHeight * _asset.pivot.y),
                            _spinRadiusStart, _spinRadiusEnd, 90f, _spinSweepDeg, _spinFlattenY, _spinClockwise);
                        if (_selectedLayerIndex >= 0 && _selectedLayerIndex < _asset.extraSlashes.Count)
                            _asset.extraSlashes[_selectedLayerIndex].path = spin;
                        else
                            _asset.path = spin;
                    }
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // --- MULTI-SLASH COMBO LAYERS ---
            _foldCombo = EditorGUILayout.BeginFoldoutHeaderGroup(_foldCombo, $"Multi-Slash Layers ({1 + (_asset.extraSlashes?.Count ?? 0)})");
            if (_foldCombo)
            {
                EditorGUI.indentLevel++;
                if (_asset.extraSlashes != null && _asset.extraSlashes.Count > 0)
                {
                    _asset.playback.comboSyncMode = (ComboSyncMode)EditorGUILayout.EnumPopup("Combo Sync", _asset.playback.comboSyncMode);
                    if (_asset.playback.comboSyncMode == ComboSyncMode.Sequential)
                    {
                        _asset.playback.mainDurationFrames = EditorGUILayout.IntSlider("Main Duration (F)", _asset.playback.mainDurationFrames, 2, _asset.playback.frameCount);
                    }
                }

                // Main Layer selector
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Toggle(_selectedLayerIndex == -1, "Main Slash Layer", EditorStyles.miniButton))
                {
                    _selectedLayerIndex = -1;
                }
                if (GUILayout.Button("+ Add Sub-Slash", EditorStyles.miniButton, GUILayout.Width(105f)))
                {
                    var clonedPath = _asset.path.Clone();
                    PolyBezierUtility.FlipHorizontal(clonedPath, _asset.canvasWidth);
                    _asset.extraSlashes.Add(new SlashSubLayerData
                    {
                        name = $"Combo Hit {_asset.extraSlashes.Count + 2}",
                        path = clonedPath,
                        delayFrames = Mathf.Min(_asset.playback.frameCount - 2, (_asset.extraSlashes.Count + 1) * 4),
                        durationFrames = 8,
                        overrideColors = true
                    });
                    _selectedLayerIndex = _asset.extraSlashes.Count - 1;
                }
                EditorGUILayout.EndHorizontal();

                for (int i = 0; i < _asset.extraSlashes.Count; i++)
                {
                    var sub = _asset.extraSlashes[i];
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    sub.enabled = EditorGUILayout.Toggle(sub.enabled, GUILayout.Width(18f));
                    if (GUILayout.Toggle(_selectedLayerIndex == i, sub.name, EditorStyles.miniButton))
                    {
                        _selectedLayerIndex = i;
                    }
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        _asset.extraSlashes.RemoveAt(i);
                        if (_selectedLayerIndex >= _asset.extraSlashes.Count) _selectedLayerIndex = -1;
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();

                    if (_selectedLayerIndex == i)
                    {
                        sub.name = EditorGUILayout.TextField("Name", sub.name);
                        sub.delayFrames = EditorGUILayout.IntSlider("Delay (F)", sub.delayFrames, 0, Mathf.Max(0, _asset.playback.frameCount - 2));
                        if (_asset.playback.comboSyncMode == ComboSyncMode.Sequential)
                        {
                            sub.durationFrames = EditorGUILayout.IntSlider("Duration (F)", sub.durationFrames, 2, _asset.playback.frameCount);
                        }
                        sub.widthScale = EditorGUILayout.Slider("Width Scale", sub.widthScale, 0.3f, 2f);
                        sub.overrideColors = EditorGUILayout.Toggle("Override Color", sub.overrideColors);
                        if (sub.overrideColors)
                        {
                            sub.colorStart = EditorGUILayout.ColorField("Start", sub.colorStart);
                            sub.colorEnd = EditorGUILayout.ColorField("End", sub.colorEnd);
                        }
                    }
                    EditorGUILayout.EndVertical();
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // --- UNITY SPRITESHEET BAKE & PARTICLE OUTPUT ---
            _foldBake = EditorGUILayout.BeginFoldoutHeaderGroup(_foldBake, "Unity Output: SpriteSheet & Particle");
            if (_foldBake)
            {
                var bs = _asset.bakeSettings;
                EditorGUI.indentLevel++;

                bs.outputFolder = EditorGUILayout.TextField("Output Folder", bs.outputFolder);
                bs.columns = EditorGUILayout.IntSlider("Columns", bs.columns, 1, 16);
                bs.padding = EditorGUILayout.IntSlider("Cell Padding (px)", bs.padding, 0, 16);
                bs.packMode = (SheetPackMode)EditorGUILayout.EnumPopup("Pack Mode", bs.packMode);
                bs.potMode = (SheetPotMode)EditorGUILayout.EnumPopup("POT Mode", bs.potMode);
                bs.maxSize = EditorGUILayout.IntPopup("Max Sheet Size", bs.maxSize, new[] { "512", "1024", "2048", "4096" }, new[] { 512, 1024, 2048, 4096 });
                bs.skipEmptyBoundaryFrames = EditorGUILayout.Toggle("Skip Empty Start/End", bs.skipEmptyBoundaryFrames);
                bs.alphaBleedDilation = EditorGUILayout.Toggle("Alpha Bleed (1px)", bs.alphaBleedDilation);
                bs.alphaOutputMode = (AlphaOutputMode)EditorGUILayout.EnumPopup("Alpha Output", bs.alphaOutputMode);
                bs.pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit (XY)", bs.pixelsPerUnit);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("ParticleSystem (XY Plane)", EditorStyles.boldLabel);
                bs.particleSheetMode = (ParticleSheetMode)EditorGUILayout.EnumPopup("Sheet Anim Mode", bs.particleSheetMode);
                bs.particleRenderMode = (ParticlePlaneRenderMode)EditorGUILayout.EnumPopup("Plane Render Mode", bs.particleRenderMode);
                bs.doubleSidedMaterial = EditorGUILayout.Toggle("Double-Sided (FlipX)", bs.doubleSidedMaterial);
                bs.loopParticle = EditorGUILayout.Toggle("Loop Playback", bs.loopParticle);
                bs.stopAction = (ParticleSystemStopAction)EditorGUILayout.EnumPopup("Stop Action", bs.stopAction);
                bs.sortingOrder = EditorGUILayout.IntField("Sorting Order", bs.sortingOrder);

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(6);

                // Action Buttons
                GUI.backgroundColor = new Color(0.28f, 0.72f, 0.98f);
                if (GUILayout.Button("1. Bake SpriteSheet PNG (Auto-Slice)", GUILayout.Height(28f)))
                {
                    ExecuteBakeSpriteSheetOnly();
                }

                GUI.backgroundColor = new Color(0.32f, 0.85f, 0.56f);
                if (GUILayout.Button("2. Bake & Create XY Particle Object", GUILayout.Height(30f)))
                {
                    ExecuteBakeAndCreateSceneParticle();
                }

                GUI.backgroundColor = new Color(0.95f, 0.68f, 0.25f);
                if (GUILayout.Button("3. Bake & Save Particle Prefab Bundle", GUILayout.Height(28f)))
                {
                    ExecuteBakeAndSavePrefab();
                }

                GUI.backgroundColor = Color.white;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Create 2D Sprite Anim", EditorStyles.miniButtonLeft, GUILayout.Height(22f)))
                {
                    ExecuteBakeAndCreateSpriteAnim();
                }
                bool hasLinks = !string.IsNullOrEmpty(bs.lastBakedTextureGuid) || !string.IsNullOrEmpty(bs.lastBakedPrefabGuid);
                using (new EditorGUI.DisabledScope(!hasLinks))
                {
                    if (GUILayout.Button("Hot-Rebake Linked", EditorStyles.miniButtonRight, GUILayout.Height(22f)))
                    {
                        ExecuteHotRebake();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(bs.lastBakedTextureGuid))
                {
                    string texPath = AssetDatabase.GUIDToAssetPath(bs.lastBakedTextureGuid);
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        EditorGUILayout.HelpBox($"Linked Sheet: {texPath}", MessageType.None);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (EditorGUI.EndChangeCheck())
            {
                MarkAssetDirty();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUIUtility.labelWidth = prevLabelWidth;
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(_statusMessage, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Unity 6 XY-Plane | {_asset.canvasWidth}x{_asset.canvasHeight} | {_asset.playback.frameCount}F @ {_asset.playback.fps}fps", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void InsertMidAnchor(SlashPathData path)
        {
            if (path == null || path.anchors == null || path.anchors.Count < 2) return;
            var lut = new PolyBezierLut(path.anchors);
            lut.Evaluate(0.5f, out Vector2 midPt, out Vector2 deriv);
            Vector2 dir = deriv.sqrMagnitude > 1e-4f ? deriv.normalized : Vector2.right;
            float handleLen = Mathf.Max(20f, lut.TotalLength * 0.14f);

            int insertIdx = path.anchors.Count / 2;
            path.anchors.Insert(insertIdx, new BezierAnchorData
            {
                point = midPt,
                hasHandleIn = true,
                handleIn = midPt - dir * handleLen,
                hasHandleOut = true,
                handleOut = midPt + dir * handleLen
            });
        }

        private void CreateNewPersistentAsset()
        {
            string defaultFolder = "Assets/SlashEffects";
            SpriteSheetBaker.EnsureAssetFolderExists(defaultFolder);
            string path = EditorUtility.SaveFilePanelInProject("Create Slash Effect Asset", "NewSlashEffect", "asset", "Save SlashEffectAsset", defaultFolder);
            if (string.IsNullOrEmpty(path)) return;

            var newAsset = CreateInstance<SlashEffectAsset>();
            SlashEffectPresets.ApplyPreset(newAsset, SlashEffectPresets.PresetNames[_selectedPresetIndex], pathOnly: false);
            newAsset.effectName = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(newAsset, path);
            AssetDatabase.SaveAssets();

            _asset = newAsset;
            Selection.activeObject = newAsset;
            _previewDirty = true;
            _statusMessage = $"Created asset: {path}";
        }

        private void ExecuteBakeSpriteSheetOnly()
        {
            var targetAsset = _asset;
            EditorApplication.delayCall += () =>
            {
                if (targetAsset == null) return;
                var result = SpriteSheetBaker.Bake(targetAsset, reuseLinkedTexturePath: true);
                EditorGUIUtility.PingObject(result.texture);
                _statusMessage = $"Baked SpriteSheet ({result.sheetWidth}x{result.sheetHeight}, {result.effectiveFrameCount} frames) -> {result.textureAssetPath}";
                _previewDirty = true;
                Repaint();
            };
        }

        private void ExecuteBakeAndCreateSceneParticle()
        {
            var targetAsset = _asset;
            EditorApplication.delayCall += () =>
            {
                if (targetAsset == null) return;
                var result = SpriteSheetBaker.Bake(targetAsset, reuseLinkedTexturePath: true);
                var go = SlashParticleGenerator.CreateSceneParticleObject(targetAsset, result);
                _statusMessage = $"Created XY ParticleSystem '{go.name}' ({result.effectiveFrameCount}F @ {result.fps}fps)";
                _previewDirty = true;
                Repaint();
            };
        }

        private void ExecuteBakeAndSavePrefab()
        {
            var targetAsset = _asset;
            EditorApplication.delayCall += () =>
            {
                if (targetAsset == null) return;
                var result = SpriteSheetBaker.Bake(targetAsset, reuseLinkedTexturePath: true);
                var prefab = SlashParticleGenerator.SaveOrUpdatePrefabBundle(targetAsset, result, reuseLinkedPrefab: true);
                _statusMessage = $"Saved Prefab Bundle: {AssetDatabase.GetAssetPath(prefab)}";
                _previewDirty = true;
                Repaint();
            };
        }

        private void ExecuteBakeAndCreateSpriteAnim()
        {
            var targetAsset = _asset;
            EditorApplication.delayCall += () =>
            {
                if (targetAsset == null) return;
                var result = SpriteSheetBaker.Bake(targetAsset, reuseLinkedTexturePath: true);
                var go = SlashParticleGenerator.CreateSpriteAnimationObject(targetAsset, result);
                if (go != null)
                {
                    _statusMessage = $"Created 2D Sprite Animation Object '{go.name}'";
                }
                _previewDirty = true;
                Repaint();
            };
        }

        private void ExecuteHotRebake()
        {
            var targetAsset = _asset;
            EditorApplication.delayCall += () =>
            {
                if (targetAsset == null) return;
                var result = SpriteSheetBaker.Bake(targetAsset, reuseLinkedTexturePath: true);
                if (!string.IsNullOrEmpty(targetAsset.bakeSettings.lastBakedMaterialGuid))
                {
                    ParticleMaterialFactory.CreateOrUpdateParticleMaterial(targetAsset, result, reuseLinkedMaterial: true);
                }
                if (!string.IsNullOrEmpty(targetAsset.bakeSettings.lastBakedPrefabGuid))
                {
                    SlashParticleGenerator.SaveOrUpdatePrefabBundle(targetAsset, result, reuseLinkedPrefab: true);
                }
                _statusMessage = $"Hot-Rebaked linked assets (GUID preserved): {result.textureAssetPath}";
                _previewDirty = true;
                Repaint();
            };
        }

        private void MarkAssetDirty()
        {
            _previewDirty = true;
            if (_asset != null && _asset != _tempSessionAsset)
            {
                EditorUtility.SetDirty(_asset);
            }
            if (SlashSceneOverlay.Enabled)
            {
                SlashSceneOverlay.ActiveAsset = _asset;
                SlashSceneOverlay.MarkDirty();
            }
        }

        private void EnsurePreviewRenderTextures(int w, int h)
        {
            w = Mathf.Clamp(w, 32, 2048);
            h = Mathf.Clamp(h, 32, 2048);

            void EnsureRT(ref RenderTexture rt)
            {
                if (rt != null && rt.width == w && rt.height == h) return;
                if (rt != null)
                {
                    rt.Release();
                    DestroyImmediate(rt);
                }
                rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                rt.Create();
            }

            EnsureRT(ref _previewRT);
            EnsureRT(ref _onionPrevRT);
            EnsureRT(ref _onionNextRT);
        }

        private void BuildCheckerTexture()
        {
            if (_checkerTex != null) return;
            _checkerTex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };
            Color a = new Color(0.14f, 0.16f, 0.21f, 1f);
            Color b = new Color(0.19f, 0.22f, 0.28f, 1f);
            _checkerTex.SetPixels(new[] { a, b, b, a });
            _checkerTex.Apply();
        }

        private void ReleaseRenderResources()
        {
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }
            if (_previewRT != null) { _previewRT.Release(); DestroyImmediate(_previewRT); _previewRT = null; }
            if (_onionPrevRT != null) { _onionPrevRT.Release(); DestroyImmediate(_onionPrevRT); _onionPrevRT = null; }
            if (_onionNextRT != null) { _onionNextRT.Release(); DestroyImmediate(_onionNextRT); _onionNextRT = null; }
            if (_checkerTex != null) { DestroyImmediate(_checkerTex); _checkerTex = null; }
        }
    }
}
