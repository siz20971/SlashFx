using UnityEngine;

namespace Ympp.SlashEffectStudio.Samples
{
    /// <summary>
    /// Simple XY-plane side-scrolling character controller demonstrating horizontal movement,
    /// FlipX orientation, and triggering baked Slash Effect ParticleSystem prefabs.
    /// Controls: A/D or Left/Right to move, Space or Left Click to slash, 1-4 to switch slash prefabs.
    /// </summary>
    public class SideScrollerSlashController : MonoBehaviour
    {
        [Header("Movement (XY Plane)")]
        public float moveSpeed = 6f;
        public float minX = -7f;
        public float maxX = 7f;
        public bool facingRight = true;

        [Header("Visual & Attack Pivot")]
        public Transform visualRoot;
        public Transform slashPivot;

        [Header("Slash Particle Prefabs")]
        public SlashParticlePlayer[] slashPrefabs;
        public int activeSlashIndex = 0;

        private SlashParticlePlayer[] _pooledInstances;

        private void Start()
        {
            RebuildPool();
            ApplyFacingVisual();
        }

        public void RebuildPool()
        {
            if (slashPrefabs == null || slashPrefabs.Length == 0) return;
            _pooledInstances = new SlashParticlePlayer[slashPrefabs.Length];
            Transform anchor = slashPivot != null ? slashPivot : transform;

            for (int i = 0; i < slashPrefabs.Length; i++)
            {
                if (slashPrefabs[i] == null) continue;
                var inst = Instantiate(slashPrefabs[i], anchor);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                var ps = inst.ParticleSystem;
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _pooledInstances[i] = inst;
            }
        }

        private void Update()
        {
            // Horizontal XY movement (using legacy-safe Input/Keyboard fallback)
            float moveX = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) moveX -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) moveX += 1f;

            if (Mathf.Abs(moveX) > 0.01f)
            {
                bool nextFacing = moveX > 0f;
                if (nextFacing != facingRight)
                {
                    facingRight = nextFacing;
                    ApplyFacingVisual();
                }

                Vector3 pos = transform.position;
                pos.x = Mathf.Clamp(pos.x + moveX * moveSpeed * Time.deltaTime, minX, maxX);
                transform.position = pos;
            }

            // Switch active slash preset (keys 1~4)
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectSlash(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectSlash(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectSlash(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SelectSlash(3);

            // Trigger Slash on Space or Left Mouse Button
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))
            {
                TriggerActiveSlash();
            }
        }

        public void SelectSlash(int index)
        {
            if (slashPrefabs == null || slashPrefabs.Length == 0) return;
            activeSlashIndex = Mathf.Clamp(index, 0, slashPrefabs.Length - 1);
            TriggerActiveSlash();
        }

        public void TriggerActiveSlash()
        {
            if (_pooledInstances == null || _pooledInstances.Length == 0) return;
            int idx = Mathf.Clamp(activeSlashIndex, 0, _pooledInstances.Length - 1);
            var player = _pooledInstances[idx];
            if (player != null)
            {
                // Because visualRoot is separate from slashPivot (or slashPivot is unflipped),
                // SlashParticlePlayer handles horizontal FlipX cleanly via localScale.x!
                player.Play(facingRight);
            }
        }

        private void ApplyFacingVisual()
        {
            if (visualRoot != null)
            {
                Vector3 s = visualRoot.localScale;
                s.x = Mathf.Abs(s.x) * (facingRight ? 1f : -1f);
                visualRoot.localScale = s;
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 360, 120), GUI.skin.box);
            GUILayout.Label("Slash Effect Studio — XY Side-Scroller Demo");
            GUILayout.Label("Move: [A / D] or [Left / Right]  |  Attack: [Space] / [Click]");
            GUILayout.Label("Switch Slash: [1] ThinArc  [2] WideSlash  [3] CrossX  [4] Spin");
            string activeName = (slashPrefabs != null && activeSlashIndex < slashPrefabs.Length && slashPrefabs[activeSlashIndex] != null)
                ? slashPrefabs[activeSlashIndex].name
                : "(None)";
            GUILayout.Label($"Facing: {(facingRight ? "+X (Right)" : "-X (Left)")} | Active: {activeName}");
            GUILayout.EndArea();
        }
    }
}
