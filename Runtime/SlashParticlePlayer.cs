using UnityEngine;

namespace Ympp.SlashEffectStudio
{
    /// <summary>
    /// Lightweight XY-plane playback helper for Slash Effect Studio ParticleSystems.
    /// Handles horizontal FlipX orientation (for side-scrolling characters), one-shot restarts,
    /// and optional HDR tint overrides (ideal for Grayscale Mask baked sheets).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public class SlashParticlePlayer : MonoBehaviour
    {
        [Header("XY Plane Orientation")]
        [Tooltip("Default slash authoring faces +X (right). Disable to mirror horizontally (-X).")]
        public bool facingRight = true;

        [Tooltip("Optional local offset applied in facing direction.")]
        public Vector2 localOffset = Vector2.zero;

        [Header("Optional Tint Override (Grayscale Mask Mode)")]
        public bool overrideStartColor = false;
        [ColorUsage(true, true)]
        public Color tintColor = Color.white;

        private ParticleSystem _ps;
        private Vector3 _baseLocalScale = Vector3.one;
        private bool _initialized;

        public ParticleSystem ParticleSystem
        {
            get
            {
                if (_ps == null) _ps = GetComponent<ParticleSystem>();
                return _ps;
            }
        }

        private void Awake()
        {
            CacheDefaults();
        }

        private void CacheDefaults()
        {
            if (_initialized) return;
            _ps = GetComponent<ParticleSystem>();
            _baseLocalScale = transform.localScale;
            if (Mathf.Approximately(_baseLocalScale.x, 0f)) _baseLocalScale.x = 1f;
            if (Mathf.Approximately(_baseLocalScale.y, 0f)) _baseLocalScale.y = 1f;
            if (Mathf.Approximately(_baseLocalScale.z, 0f)) _baseLocalScale.z = 1f;
            _initialized = true;
        }

        /// <summary>
        /// Triggers the slash particle cleanly in the requested horizontal facing direction on the XY plane.
        /// </summary>
        public void Play(bool faceRight = true)
        {
            CacheDefaults();
            facingRight = faceRight;
            ApplyOrientation();

            var ps = ParticleSystem;
            if (ps == null) return;

            if (overrideStartColor)
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(tintColor);
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        /// <summary>
        /// Triggers the slash particle at a specific XY world position and horizontal facing direction.
        /// </summary>
        public void PlayAt(Vector3 worldPosition, bool faceRight = true)
        {
            CacheDefaults();
            facingRight = faceRight;
            float sign = facingRight ? 1f : -1f;
            transform.position = worldPosition + new Vector3(localOffset.x * sign, localOffset.y, 0f);
            Play(faceRight);
        }

        public void ApplyOrientation()
        {
            CacheDefaults();
            float sign = facingRight ? 1f : -1f;
            transform.localScale = new Vector3(
                Mathf.Abs(_baseLocalScale.x) * sign,
                _baseLocalScale.y,
                _baseLocalScale.z
            );
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                _baseLocalScale = new Vector3(
                    Mathf.Approximately(transform.localScale.x, 0f) ? 1f : Mathf.Abs(transform.localScale.x),
                    Mathf.Approximately(transform.localScale.y, 0f) ? 1f : transform.localScale.y,
                    Mathf.Approximately(transform.localScale.z, 0f) ? 1f : transform.localScale.z
                );
            }
        }
    }
}
