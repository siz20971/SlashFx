using UnityEditor;
using UnityEngine;
using Sizzle.SlashFx.Editor.Rendering;

namespace Sizzle.SlashFx.Editor.UI
{
    /// <summary>
    /// Renders a live XY-plane preview of the active SlashEffectAsset directly in the Unity Scene View
    /// attached to a chosen character/weapon Transform.
    /// Offscreen RenderTexture updates happen strictly outside OnSceneGui (in EditorApplication.update)
    /// so SceneView render targets are never disturbed.
    /// </summary>
    [InitializeOnLoad]
    public static class SlashSceneOverlay
    {
        public static bool Enabled { get; set; }
        public static SlashEffectAsset ActiveAsset { get; set; }
        public static Transform TargetTransform { get; set; }
        public static int PreviewFrame { get; set; }
        public static bool FlipX { get; set; }
        public static float Opacity { get; set; } = 0.95f;

        private static SlashGpuRenderer _renderer;
        private static RenderTexture _previewRT;
        private static Material _blitMat;
        private static Mesh _quadMesh;
        private static bool _dirty = true;

        static SlashSceneOverlay()
        {
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        public static void MarkDirty()
        {
            _dirty = true;
        }

        private static void OnEditorUpdate()
        {
            if (!Enabled || ActiveAsset == null || !_dirty) return;
            _dirty = false;

            int w = Mathf.Clamp(ActiveAsset.canvasWidth, 64, 1024);
            int h = Mathf.Clamp(ActiveAsset.canvasHeight, 64, 1024);

            EnsurePreviewResources(w, h);
            if (_renderer != null && _previewRT != null)
            {
                _renderer.RenderFrame(ActiveAsset, PreviewFrame, _previewRT, AlphaOutputMode.StandardAlpha);
                SceneView.RepaintAll();
            }
        }

        private static void OnSceneGui(SceneView sceneView)
        {
            if (!Enabled || ActiveAsset == null) return;
            if (_previewRT == null || _blitMat == null || _quadMesh == null) return;

            Transform target = TargetTransform != null ? TargetTransform : Selection.activeTransform;
            Vector3 anchorPos = target != null ? target.position : Vector3.zero;
            Quaternion rot = target != null ? target.rotation : Quaternion.identity;

            float ppu = Mathf.Max(1f, ActiveAsset.bakeSettings.pixelsPerUnit);
            float worldW = ActiveAsset.canvasWidth / ppu;
            float worldH = ActiveAsset.canvasHeight / ppu;

            // Align canvas pivot onto target anchor position on XY plane
            Vector3 pivotOffset = new Vector3(
                (0.5f - ActiveAsset.pivot.x) * worldW * (FlipX ? -1f : 1f),
                (0.5f - ActiveAsset.pivot.y) * worldH,
                -0.02f
            );

            Vector3 scale = new Vector3(FlipX ? -worldW : worldW, worldH, 1f);
            Matrix4x4 matrix = Matrix4x4.TRS(anchorPos + rot * pivotOffset, rot, scale);

            if (Event.current.type == EventType.Repaint)
            {
                _blitMat.mainTexture = _previewRT;
                _blitMat.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Opacity));
                _blitMat.SetPass(0);
                Graphics.DrawMeshNow(_quadMesh, matrix);
            }

            // Draw subtle XY pivot crosshair
            var prevCol = Handles.color;
            Handles.color = new Color(1f, 0.85f, 0.2f, 0.8f);
            float cross = 0.2f;
            Handles.DrawLine(anchorPos + rot * new Vector3(-cross, 0f, 0f), anchorPos + rot * new Vector3(cross, 0f, 0f));
            Handles.DrawLine(anchorPos + rot * new Vector3(0f, -cross, 0f), anchorPos + rot * new Vector3(0f, cross, 0f));
            Handles.color = prevCol;
        }

        private static void EnsurePreviewResources(int w, int h)
        {
            if (_renderer == null) _renderer = new SlashGpuRenderer();

            if (_previewRT == null || _previewRT.width != w || _previewRT.height != h)
            {
                if (_previewRT != null)
                {
                    _previewRT.Release();
                    Object.DestroyImmediate(_previewRT);
                }
                _previewRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _previewRT.Create();
            }

            if (_blitMat == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _blitMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
            }

            if (_quadMesh == null)
            {
                _quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            }
        }

        public static void Cleanup()
        {
            if (_renderer != null)
            {
                _renderer.Dispose();
                _renderer = null;
            }
            if (_previewRT != null)
            {
                _previewRT.Release();
                Object.DestroyImmediate(_previewRT);
                _previewRT = null;
            }
            if (_blitMat != null)
            {
                Object.DestroyImmediate(_blitMat);
                _blitMat = null;
            }
            _dirty = true;
        }
    }
}
