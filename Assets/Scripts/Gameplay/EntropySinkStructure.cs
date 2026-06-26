using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural gold torus structure for Maxwell's Demon (the entropy sink). A flat donut built by
    /// <see cref="TorusMeshBuilder"/> that fills the 3x3 footprint, shaded by the
    /// <c>MobileIdleBuilder/EntropySinkTorus</c> shader. The gold patterns continuously swirl in the
    /// shader (_Time-driven); this component is the <b>random pattern swapper</b> — it holds a pattern
    /// for a random dwell, then crossfades (ramping <c>_Blend</c> 0->1) to a new random pattern, and
    /// repeats. The whole ring also turns slowly for extra life.
    ///
    /// BuildingVisualizer attaches this to any building carrying <see cref="EntropySinkTag"/>, replacing
    /// the placeholder cube. Mirrors the field-collector pattern (<see cref="CollectorStructure"/>).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class EntropySinkStructure : MonoBehaviour
    {
        private const int   TubularSegments = 48;   // around the main ring
        private const int   RadialSegments  = 16;   // around the tube cross-section
        private const int   PatternCount    = 5;    // must match PatternValue() branches in the shader

        private const float HoldMin      = 3f;      // min seconds to dwell on a pattern
        private const float HoldMax      = 6f;      // max seconds to dwell on a pattern
        private const float FadeDuration = 1.2f;    // crossfade length between patterns
        private const float SpinSpeed    = 8f;      // slow Y spin (deg/sec)

        private static readonly Color GoldBase   = new Color(1.0f, 0.78f, 0.30f, 1f);
        private static readonly Color GoldAccent = new Color(1.0f, 0.95f, 0.70f, 1f);

        private static readonly int BaseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
        private static readonly int PatternAId    = Shader.PropertyToID("_PatternA");
        private static readonly int PatternBId    = Shader.PropertyToID("_PatternB");
        private static readonly int BlendId       = Shader.PropertyToID("_Blend");

        private Material _material;
        private Mesh     _mesh;

        private int   _patternA;
        private int   _patternB;
        private bool  _fading;
        private float _holdElapsed;
        private float _fadeElapsed;
        private float _dwell;

        /// <summary>
        /// Builds the torus onto the host and starts the random pattern crossfade. Call once after the
        /// host GameObject has been positioned and scaled. The <paramref name="entity"/> is accepted for
        /// parity with the collector structure (and possible future deposit-driven effects); the torus
        /// visual itself is self-contained.
        /// </summary>
        public void Initialize(Entity entity)
        {
            BuildBody();
            SeedPatterns();
        }

        private void BuildBody()
        {
            _mesh = TorusMeshBuilder.Build(TubularSegments, RadialSegments);
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/EntropySinkTorus");
            if (shader == null)
            {
                GameLogger.Warning("[EntropySinkStructure] Shader 'MobileIdleBuilder/EntropySinkTorus' not found — entropy sink keeps placeholder mesh.");
                return;
            }

            _material = new Material(shader);
            _material.SetColor(BaseColorId, GoldBase);       // fallback; PresenceReceiver MPB overrides per-instance
            _material.SetColor(AccentColorId, GoldAccent);
            renderer.material = _material;                    // instance; PresenceReceiver's _BaseColor MPB still applies
        }

        private void SeedPatterns()
        {
            _patternA = Random.Range(0, PatternCount);
            _patternB = NextPatternDifferentFrom(_patternA);
            _holdElapsed = 0f;
            _fadeElapsed = 0f;
            _fading = false;
            _dwell  = Random.Range(HoldMin, HoldMax);

            if (_material != null)
            {
                _material.SetFloat(PatternAId, _patternA);
                _material.SetFloat(PatternBId, _patternB);
                _material.SetFloat(BlendId, 0f);
            }
        }

        private static int NextPatternDifferentFrom(int current)
        {
            if (PatternCount <= 1) return current;
            int next = Random.Range(0, PatternCount - 1);
            if (next >= current) next++; // skip the current index so we always change
            return next;
        }

        void Update()
        {
            // Slow lazy spin — the per-pixel swirl already animates in the shader.
            transform.Rotate(0f, SpinSpeed * Time.deltaTime, 0f, Space.Self);

            if (_material == null) return;

            if (_fading)
            {
                _fadeElapsed += Time.deltaTime;
                float b = Mathf.Clamp01(_fadeElapsed / FadeDuration);
                _material.SetFloat(BlendId, b);

                if (b >= 1f)
                {
                    // B is now fully shown — promote it to A, pick a new B, and reset the crossfade.
                    _patternA = _patternB;
                    _patternB = NextPatternDifferentFrom(_patternA);
                    _material.SetFloat(PatternAId, _patternA);
                    _material.SetFloat(PatternBId, _patternB);
                    _material.SetFloat(BlendId, 0f);

                    _fading      = false;
                    _holdElapsed = 0f;
                    _dwell       = Random.Range(HoldMin, HoldMax);
                }
            }
            else
            {
                _holdElapsed += Time.deltaTime;
                if (_holdElapsed >= _dwell)
                {
                    _fading      = true;
                    _fadeElapsed = 0f;
                }
            }
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_mesh != null)     Destroy(_mesh);
        }
    }
}
