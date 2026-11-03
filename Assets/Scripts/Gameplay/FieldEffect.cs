using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Programmatically builds a ParticleSystem and point light effect
    /// for a resource field. Call Initialize(color) after adding this
    /// component to set the field's visual identity color.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class FieldEffect : MonoBehaviour
    {
        private ParticleSystem _particles;
        private Light          _light;
        private Color          _fieldColor;
        private bool           _locked;

        public void Initialize(Color fieldColor, Material particleMaterialTemplate)
        {
            _fieldColor = fieldColor;
            _particles  = GetComponent<ParticleSystem>();
            ConfigureParticles(fieldColor, particleMaterialTemplate);
            CreateLight(fieldColor);
        }

        /// <summary>
        /// Dims the field light when the field is locked by the tutorial. Particles are now spawned
        /// only as a tap burst (see <see cref="Burst"/>), so there is no continuous stream to dim.
        /// Call with locked=false to restore full brightness when the field becomes available.
        /// </summary>
        public void SetLocked(bool locked)
        {
            if (_locked == locked) return;
            _locked = locked;

            if (_light != null)
                _light.intensity = locked ? 0.2f : 1.5f;
        }

        /// <summary>
        /// Emits a one-shot particle burst from the tile — the particle "born" when the field is
        /// tapped. Safe to call before initialization (no-ops if the system is missing).
        /// </summary>
        public void Burst(int count)
        {
            if (_particles == null || count <= 0) return;
            if (_locked) return; // a locked field gives no tap feedback
            _particles.Emit(count);
        }

        private void ConfigureParticles(Color baseColor, Material particleMaterialTemplate)
        {
            // ---- Main module ----
            var main = _particles.main;
            main.loop          = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSpeed    = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSize     = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor    = new ParticleSystem.MinMaxGradient(
                new Color(baseColor.r, baseColor.g, baseColor.b, 0.9f),
                new Color(
                    Mathf.Clamp01(baseColor.r + 0.15f),
                    Mathf.Clamp01(baseColor.g + 0.15f),
                    Mathf.Clamp01(baseColor.b + 0.15f),
                    0.6f));
            main.gravityModifier     = -0.05f; // slight upward drift
            main.simulationSpace     = ParticleSystemSimulationSpace.World;
            main.maxParticles        = 80;

            // ---- Emission ----
            // No continuous stream: particles are born only as a burst on tap (see Burst()).
            var emission = _particles.emission;
            emission.enabled      = true;
            emission.rateOverTime = 0f;

            // ---- Shape: hemisphere rising from the tile ----
            var shape = _particles.shape;
            shape.enabled      = true;
            shape.shapeType    = ParticleSystemShapeType.Hemisphere;
            shape.radius       = 0.45f;
            shape.radiusThickness = 0.6f;

            // ---- Color over lifetime: fade out ----
            var col = _particles.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(baseColor, 0f), new GradientColorKey(baseColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.4f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);

            // ---- Size over lifetime: shrink ----
            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 1f);
            sizeCurve.AddKey(1f, 0f);
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // ---- Rotation over lifetime: slow spin ----
            var rot = _particles.rotationOverLifetime;
            rot.enabled = true;
            rot.z       = new ParticleSystem.MinMaxCurve(-45f * Mathf.Deg2Rad, 45f * Mathf.Deg2Rad);

            // ---- Renderer ----
            var rend = _particles.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = 1;

            // Instance from the serialized template so the shader/variant is guaranteed in builds.
            // Additive-transparent blend state is baked into FieldParticleMaterial.mat, so only the
            // per-field tint is set here. Do NOT enable GPU instancing: the template ships with
            // m_EnableInstancingVariants:0, so URP Lit's INSTANCING_ON variant is stripped from
            // device builds; requesting it on a billboard particle renders magenta on Android.
            var mat = new Material(particleMaterialTemplate);
            mat.SetColor("_BaseColor", baseColor);
            rend.material = mat;

            _particles.Play();
        }

        private void CreateLight(Color fieldColor)
        {
            var lightGO = new GameObject("FieldLight");
            lightGO.transform.SetParent(transform, worldPositionStays: false);
            lightGO.transform.localPosition = new Vector3(0f, 0.4f, 0f);

            _light              = lightGO.AddComponent<Light>();
            _light.type         = LightType.Point;
            _light.color        = fieldColor;
            _light.intensity    = 1.5f;
            _light.range        = 2.5f;
            _light.shadows      = LightShadows.None;
        }
    }
}
