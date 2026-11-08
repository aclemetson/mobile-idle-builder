using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "Tesla coil": the Basic Generator (a power source, not a producer). A column wound with copper rises to
    /// a toroidal crown and a sharp tip; on each heartbeat, lightning arcs from the crown to the ground and the
    /// tip flares, while flat rings spread across the ground, reading as power broadcasting to neighbours.
    /// Omnidirectional, so it has no input/output holes. The column is the host's mesh (it keeps the power and
    /// hover tints), shaded by the CollectorStructure shader. Selected by
    /// <see cref="BuildingStructureKind.BasicGenerator"/>. The pulse is time-driven: nothing is crafted here.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BasicGeneratorStructure : MonoBehaviour
    {
        private const float CrownY       = 1.0f;
        private const float CrownRadius  = 0.21f;

        private const float HeartbeatPeriod = 1.5f;  // seconds between power pulses
        private const float InitialPulse    = 0.7f;
        private const float DecayRate       = 3f;
        private const float SettleThreshold = 0.001f;

        private const int   ArcCount       = 2;
        private const int   ArcPoints      = 7;
        private const float ArcVisibleOver = 0.2f;   // arcs show while the pulse is above this
        private const float ArcRefresh     = 0.05f;  // seconds between new lightning shapes
        private const float ArcJitter      = 0.07f;
        private const float ArcReach       = 0.56f;  // where arcs land, from the centre
        private const float ArcWidth       = 0.035f; // world units

        private const int   RippleCount  = 3;
        private const float RipplePeriod = 2.4f;   // one ring's expand-and-fade cycle
        private const float RippleStart  = 0.15f;
        private const float RippleEnd    = 2.4f;   // ≈ link/influence reach

        private static readonly Color CopperColor = new Color(0.95f, 0.58f, 0.28f);
        private static readonly Color CrownColor  = new Color(0.72f, 0.80f, 0.90f);
        private static readonly Color TipColor    = new Color(0.78f, 0.96f, 1.0f);
        private static readonly Color ArcColor    = new Color(0.80f, 0.96f, 1.0f);
        private static readonly Color RippleColor = new Color(0.40f, 0.9f, 1.0f);

        private static readonly int PulseAmpId = Shader.PropertyToID("_PulseAmp");

        private readonly StructureParts _parts = new();
        private readonly LineRenderer[] _arcs = new LineRenderer[ArcCount];
        private GroundRipples _ripples;
        private Material _columnMat;

        private float _heartbeatTimer;
        private float _arcTimer;
        private float _pulseElapsed = Mathf.Infinity;

        public void Initialize(Color columnColor)
        {
            BuildColumn(columnColor);
            BuildWindings();
            BuildCrown();
            BuildArcs();
            _ripples = new GroundRipples(_parts, transform, RippleCount, 0.012f);
        }

        private void BuildColumn(Color color)
        {
            var mesh = _parts.Own(RevolveMeshBuilder.Build(new[]
            {
                new Vector2(0.30f, 0f),    new Vector2(0.30f, 0.05f), new Vector2(0.14f, 0.12f),
                new Vector2(0.085f, 0.24f), new Vector2(0.075f, 0.85f), new Vector2(0.05f, 1.02f),
                new Vector2(0.016f, 1.2f), new Vector2(0f, 1.3f),
            }, 22));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = GetComponent<MeshRenderer>();
            StructureParts.Plain(renderer);
            _columnMat = _parts.Shaded(color, TipColor, 3f);
            renderer.sharedMaterial = _columnMat;
        }

        private void BuildWindings()
        {
            var material = _parts.Flat(CopperColor);
            if (material == null) return;

            // Each turn is the same ring, scaled in so the winding tapers up the column.
            var mesh = _parts.Own(TorusMeshBuilder.Build(24, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = 0.13f, MinorRadius = 0.026f }));
            const int turns = 6;
            for (int i = 0; i < turns; i++)
            {
                float t = (float)i / (turns - 1);
                var turn = StructureParts.AddMesh($"Winding_{i}", transform, mesh, material,
                                                  new Vector3(0f, Mathf.Lerp(0.26f, 0.76f, t), 0f));
                float scale = Mathf.Lerp(1.05f, 0.82f, t);
                turn.localScale = new Vector3(scale, 1f, scale);
            }
        }

        private void BuildCrown()
        {
            var mesh = _parts.Own(TorusMeshBuilder.Build(32, 10,
                new TorusMeshBuilder.TorusProfile { MajorRadius = CrownRadius, MinorRadius = 0.06f }));
            StructureParts.AddMesh("Crown", transform, mesh, _parts.Shaded(CrownColor, Color.white, 0.5f), new Vector3(0f, CrownY, 0f));
        }

        private void BuildArcs()
        {
            var material = _parts.Flat(ArcColor);
            if (material == null) return;

            for (int i = 0; i < ArcCount; i++)
            {
                var go = new GameObject($"Arc_{i}");
                go.transform.SetParent(transform, worldPositionStays: false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = ArcPoints;
                line.startWidth    = ArcWidth;
                line.endWidth      = ArcWidth * 0.5f;
                line.sharedMaterial = material;
                StructureParts.Plain(line);
                line.enabled = false;
                _arcs[i] = line;
            }
        }

        void Update()
        {
            float t  = Time.time;
            float dt = Time.deltaTime;

            _heartbeatTimer += dt;
            if (_heartbeatTimer >= HeartbeatPeriod)
            {
                _heartbeatTimer -= HeartbeatPeriod;
                _pulseElapsed = 0f;
            }

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }

            if (_columnMat != null) _columnMat.SetFloat(PulseAmpId, pulse);
            AnimateArcs(pulse, dt);
            _ripples?.Animate(t, RipplePeriod, RippleStart, RippleEnd, 0.5f, 0f, RippleColor);
        }

        private void AnimateArcs(float pulse, float dt)
        {
            bool visible = pulse > ArcVisibleOver;
            _arcTimer -= dt;
            bool reshape = visible && _arcTimer <= 0f;
            if (reshape) _arcTimer = ArcRefresh;

            foreach (var arc in _arcs)
            {
                if (arc == null) continue;
                arc.enabled = visible;
                if (reshape) ReshapeArc(arc);
            }
        }

        // A jagged line from a random point on the crown down to the ground, a little further round.
        private static void ReshapeArc(LineRenderer arc)
        {
            float a = Random.value * 2f * Mathf.PI;
            float b = a + Random.Range(-0.5f, 0.5f);
            var from = new Vector3(CrownRadius * Mathf.Cos(a), CrownY, CrownRadius * Mathf.Sin(a));
            var to   = new Vector3(ArcReach * Mathf.Cos(b), 0.02f, ArcReach * Mathf.Sin(b));

            for (int i = 0; i < ArcPoints; i++)
            {
                float t = (float)i / (ArcPoints - 1);
                Vector3 point = Vector3.Lerp(from, to, t);
                if (i > 0 && i < ArcPoints - 1) point += Random.insideUnitSphere * ArcJitter;
                arc.SetPosition(i, point);
            }
        }

        void OnDestroy() => _parts.Dispose();
    }
}
