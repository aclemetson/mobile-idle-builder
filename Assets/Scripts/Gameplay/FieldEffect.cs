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
        /// Dims particles and light when the field is locked by the tutorial.
        /// Call with locked=false to restore full visuals when the field becomes available.
        /// </summary>
        public void SetLocked(bool locked)
        {
            if (_locked == locked) return;
            _locked = locked;

            if (_particles != null)
            {
                var emission = _particles.emission;
                emission.rateOverTime = locked ? 2f : 15f;

                var main = _particles.main;
                if (locked)
                {
                    var grey = Color.grey * 0.35f;
                    main.startColor = new ParticleSystem.MinMaxGradient(grey, grey);
                }
                else
                {
                    var bright  = new Color(_fieldColor.r, _fieldColor.g, _fieldColor.b, 0.9f);
                    var lighter = new Color(
                        Mathf.Clamp01(_fieldColor.r + 0.15f),
                        Mathf.Clamp01(_fieldColor.g + 0.15f),
                        Mathf.Clamp01(_fieldColor.b + 0.15f), 0.6f);
                    main.startColor = new ParticleSystem.MinMaxGradient(bright, lighter);
                }
            }

            if (_light != null)
                _light.intensity = locked ? 0.2f : 1.5f;
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
            var emission = _particles.emission;
            emission.enabled      = true;
            emission.rateOverTime = 15f;

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

            // Instance from the serialized template so the shader is guaranteed included in builds.
            var mat = new Material(particleMaterialTemplate);
            mat.SetFloat("_Surface", 1f);           // transparent
            mat.SetFloat("_Blend", 2f);             // additive
            mat.SetFloat("_BlendOp", (float)UnityEngine.Rendering.BlendOp.Add);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetColor("_BaseColor", baseColor);
            mat.enableInstancing = true;
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
