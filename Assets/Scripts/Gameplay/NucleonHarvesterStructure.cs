using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "Two wells, one nucleon": the 2x1 Nucleon Harvester. Each cell has its own cone drawing on its field, and
    /// arcs from both tips feed a cluster of quarks forming between them. On each harvest the finished nucleon
    /// flies to the output door. Selected by <see cref="BuildingStructureKind.NucleonHarvester"/>.
    ///
    /// Unlike the 1x1 <see cref="CollectorStructure"/>, which draws its own door at its centre, this has no
    /// door of its own: <see cref="BuildingVisualizer"/> draws one from the placed port, so door and flight path
    /// both come from where items really leave, on whichever cell and side that is after rotation.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NucleonHarvesterStructure : MonoBehaviour
    {
        private const float PlinthHeight    = 0.27f;
        private const float FootprintMargin = 0.06f;
        private const float WellTipY        = 0.84f;
        private const float NucleonY        = 1.1f;
        private const float QuarkSize       = 0.15f;
        private const float QuarkOrbit      = 0.07f;
        private const float MinCluster      = 0.45f; // cluster scale just after a harvest
        private const float ArcDiameter     = 0.036f;

        private const int   RingsPerWell = 2;
        private const float RingPeriod   = 2.2f;
        private const float RingOuter    = 0.9f;
        private const float RingInner    = 0.55f;

        private const float FlightSeconds   = 0.45f;
        private const float DoorInset       = 0.03f;  // stops just inside the footprint edge
        private const float DoorY           = 0.12f;
        private const float InitialPulse    = 0.6f;
        private const float DecayRate       = 4f;
        private const float SettleThreshold = 0.001f;
        private const float ClusterSpin     = 70f;    // deg/s

        private static readonly int PulseAmpId = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");

        private readonly StructureParts _parts = new();
        private readonly GroundRipples[] _rings = new GroundRipples[2];
        private Transform _cluster;
        private Transform _flyer;
        private Material  _plinthMat;
        private Material  _wellMat;
        private Material  _arcMat;
        private Material  _quarkMat;
        private Color     _fieldColor;
        private Vector3   _doorLocal;

        private World  _world;
        private Entity _entity;
        private bool   _hasEntity;
        private float  _lastTimer = -1f;
        private float  _pulseElapsed = Mathf.Infinity;
        private float  _flightElapsed = Mathf.Infinity;

        /// <summary>
        /// The middle of a port's outer edge, in the local space of a host centred on the footprint at one cell
        /// to the unit. Pure, so tests can pin the door to the port for every rotation.
        /// </summary>
        public static Vector2 PortEdgeLocal(int portCellX, int portCellY, int facing,
                                            int anchorX, int anchorY, int footprintW, int footprintH)
        {
            float x = portCellX - anchorX - (footprintW - 1) * 0.5f;
            float z = portCellY - anchorY - (footprintH - 1) * 0.5f;
            switch (facing)
            {
                case 0:  z += 0.5f; break; // North
                case 1:  x += 0.5f; break; // East
                case 2:  z -= 0.5f; break; // South
                default: x -= 0.5f; break; // West
            }
            return new Vector2(x, z);
        }

        /// <summary>
        /// Builds the structure. The host is at the footprint centre, uniform cell scale.
        /// <paramref name="portEdge"/> comes from <see cref="PortEdgeLocal"/>.
        /// </summary>
        public void Initialize(Entity entity, int fw, int fh, Vector2 portEdge, Color fieldColor)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;
            _fieldColor = fieldColor;

            // Stop just short of the edge, where the door fixture stands.
            var inward = new Vector2(portEdge.x, portEdge.y);
            bool alongX = Mathf.Abs(portEdge.x) > Mathf.Abs(fw * 0.5f) - 1e-3f;
            if (alongX) inward.x -= Mathf.Sign(portEdge.x) * DoorInset;
            else        inward.y -= Mathf.Sign(portEdge.y) * DoorInset;
            _doorLocal = new Vector3(inward.x, DoorY, inward.y);

            BuildPlinth(fw, fh, fieldColor);
            BuildWells(fw >= fh ? Vector3.right : Vector3.forward, fieldColor);
            ApplyFieldColor(fieldColor);
        }

        private void BuildPlinth(int fw, int fh, Color color)
        {
            var mesh = _parts.Own(RoundedBoxMeshBuilder.Build(32, 8, new RoundedBoxMeshBuilder.RoundedBoxProfile
            {
                HalfWidth = Mathf.Max(0.1f, fw * 0.5f - FootprintMargin),
                HalfDepth = Mathf.Max(0.1f, fh * 0.5f - FootprintMargin),
                Height    = PlinthHeight,
                Roundness = 0.5f,
            }));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = GetComponent<MeshRenderer>();
            StructureParts.Plain(renderer);
            _plinthMat = _parts.Shaded(color, color, 0f);
            renderer.sharedMaterial = _plinthMat;
        }

        private void BuildWells(Vector3 axis, Color color)
        {
            var mesh = _parts.Own(RevolveMeshBuilder.Build(new[]
            {
                new Vector2(0.33f, 0.2f), new Vector2(0.26f, 0.34f), new Vector2(0.17f, 0.45f),
                new Vector2(0.09f, 0.57f), new Vector2(0.035f, 0.7f), new Vector2(0f, WellTipY),
            }, 22));
            _wellMat = _parts.Shaded(color, color, 1.5f);
            _arcMat  = _parts.Flat(color);
            _quarkMat = _parts.Shaded(color, Color.white, 0.8f);

            var pivot = new GameObject("Cluster");
            pivot.transform.SetParent(transform, worldPositionStays: false);
            pivot.transform.localPosition = new Vector3(0f, NucleonY, 0f);
            _cluster = pivot.transform;

            for (int i = 0; i < 2; i++)
            {
                Vector3 centre = axis * (i == 0 ? -0.5f : 0.5f);
                StructureParts.AddMesh($"Well_{i}", transform, mesh, _wellMat, centre);

                var ringPivot = new GameObject($"WellRings_{i}");
                ringPivot.transform.SetParent(transform, worldPositionStays: false);
                ringPivot.transform.localPosition = centre + new Vector3(0f, PlinthHeight, 0f);
                _rings[i] = new GroundRipples(_parts, ringPivot.transform, RingsPerWell, 0.012f);

                if (_arcMat == null) continue;
                var arc = StructureParts.AddPrimitive(PrimitiveType.Cylinder, $"Arc_{i}", transform, _arcMat, Vector3.zero, Vector3.one);
                StructureParts.StretchBetween(arc, centre + new Vector3(0f, WellTipY, 0f), new Vector3(0f, NucleonY, 0f), ArcDiameter);
            }

            for (int i = 0; i < 3; i++)
            {
                float a = 2f * Mathf.PI * i / 3f;
                StructureParts.AddPrimitive(PrimitiveType.Sphere, $"Quark_{i}", _cluster, _quarkMat,
                    new Vector3(QuarkOrbit * Mathf.Cos(a), (i - 1) * 0.02f, QuarkOrbit * Mathf.Sin(a)), Vector3.one * QuarkSize);
            }

            _flyer = StructureParts.AddPrimitive(PrimitiveType.Sphere, "Flyer", transform, _quarkMat, _doorLocal, Vector3.zero);
        }

        /// <summary>
        /// Applies the field colour once it's known: a harvester can load before its field has spawned
        /// (see BuildingVisualizer's deferred field-colour resolution).
        /// </summary>
        public void SetFieldColor(Color fieldColor)
        {
            _fieldColor = fieldColor;
            ApplyFieldColor(fieldColor);
        }

        private void ApplyFieldColor(Color color)
        {
            Color pale = Color.Lerp(color, Color.white, 0.55f);
            if (_plinthMat != null) _plinthMat.SetColor(BaseColorId, color);
            if (_wellMat != null)
            {
                _wellMat.SetColor(BaseColorId, color);
                _wellMat.SetColor(TipColorId, pale);
            }
            if (_arcMat != null) _arcMat.SetColor(BaseColorId, pale);
            if (_quarkMat != null) _quarkMat.SetColor(BaseColorId, pale);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float fraction = ReadHarvestAndPollTick();

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }
            if (_wellMat != null) _wellMat.SetFloat(PulseAmpId, pulse);

            foreach (var rings in _rings)
                rings?.Animate(Time.time, RingPeriod, RingOuter, RingInner, 0f, 0.55f, _fieldColor);

            if (_cluster != null)
            {
                _cluster.Rotate(0f, ClusterSpin * dt, 0f, Space.Self);
                _cluster.localScale = Vector3.one * Mathf.Lerp(MinCluster, 1f, fraction);
            }
            AnimateFlyer(dt);
        }

        // The finished nucleon flies from the cluster to the door, where items really leave.
        private void AnimateFlyer(float dt)
        {
            if (_flyer == null || float.IsInfinity(_flightElapsed)) return;

            _flightElapsed += dt;
            float t = _flightElapsed / FlightSeconds;
            if (t >= 1f)
            {
                _flightElapsed = Mathf.Infinity;
                _flyer.localScale = Vector3.zero;
                return;
            }
            _flyer.localPosition = Vector3.Lerp(new Vector3(0f, NucleonY, 0f), _doorLocal, t * t);
            _flyer.localScale    = Vector3.one * (QuarkSize * 1.4f);
        }

        // CollectorSystem subtracts the interval on each deposit, so a drop in Timer means one was produced.
        private float ReadHarvestAndPollTick()
        {
            if (!_hasEntity || !_world.IsCreated) return 0f;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<CollectorData>(_entity)) return 0f;

            var data = em.GetComponentData<CollectorData>(_entity);
            if (_lastTimer >= 0f && data.Timer < _lastTimer - 0.0001f)
            {
                _pulseElapsed  = 0f;
                _flightElapsed = 0f;
            }
            _lastTimer = data.Timer;
            return Mathf.Clamp01(data.Timer * data.OutputRate);
        }

        void OnDestroy() => _parts.Dispose();
    }
}
