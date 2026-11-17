using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Maxwell's thought experiment made literal, for the entropy sink (3x3). A gold ring holds two chambers
    /// split by a wall: fast hot particles on one side, slow cold ones on the other. A small demon rides a gate
    /// in the wall, which lifts to let a particle through and warm it up: sorting by speed, which is the whole
    /// point of the building.
    ///
    /// The ring is the host's mesh (so it keeps the hover tint), shaded by
    /// <c>MobileIdleBuilder/EntropySinkTorus</c>, whose gold patterns swirl in the shader; this component
    /// crossfades between them at random. BuildingVisualizer attaches this to any building carrying
    /// <see cref="EntropySinkTag"/> and scales the host to the footprint, so one local unit spans it.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class EntropySinkStructure : MonoBehaviour
    {
        private const int   TubularSegments = 64;
        private const int   RadialSegments  = 12;
        private const int   PatternCount    = 5;    // must match PatternValue() branches in the shader
        private const float HoldMin      = 3f;      // min seconds to dwell on a pattern
        private const float HoldMax      = 6f;      // max seconds to dwell on a pattern
        private const float FadeDuration = 1.2f;    // crossfade length between patterns

        private const float RingRadius    = 0.455f;
        private const float RingTube      = 0.04f;
        private const float FloorY        = -0.03f; // relative to the ring's centre, just above the tiles
        private const float ChamberRadius = 0.42f;  // particles bounce inside this
        private const float WallHalfGap   = 0.075f; // half the opening in the wall
        private const float WallHeight    = 0.085f;
        private const float WallThickness = 0.022f;
        private const float GateLift      = 0.1f;

        private const int   ParticlesPerSide = 8;
        private const float ParticleSize     = 0.042f;
        private const float HotSpeed         = 0.26f; // local units/s
        private const float ColdSpeed        = 0.06f;

        // The courier's trip through the gate, in seconds from the start of a cycle.
        private const float TripPeriod   = 4.5f;
        private const float ReachGate    = 1.3f;
        private const float ClearGate    = 1.8f;
        private const float ReachChamber = 2.9f;
        private const float GateOpensAt  = 0.9f;
        private const float GateShutsAt  = 2.2f;
        private const float GateSpeed    = 8f;    // how quickly the gate follows its target

        private static readonly Color GoldBase    = new Color(1.0f, 0.78f, 0.30f, 1f);
        private static readonly Color GoldAccent  = new Color(1.0f, 0.95f, 0.70f, 1f);
        private static readonly Color HotFloor    = new Color(0.26f, 0.10f, 0.07f);
        private static readonly Color ColdFloor   = new Color(0.06f, 0.13f, 0.27f);
        private static readonly Color HotParticle = new Color(1.0f, 0.46f, 0.14f);
        private static readonly Color ColdParticle = new Color(0.36f, 0.68f, 1.0f);
        private static readonly Color WallColor   = new Color(0.62f, 0.56f, 0.40f);
        private static readonly Color GateColor   = new Color(0.86f, 0.72f, 0.36f);
        private static readonly Color DemonColor  = new Color(0.90f, 0.14f, 0.16f);

        private static readonly int BaseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
        private static readonly int PatternAId    = Shader.PropertyToID("_PatternA");
        private static readonly int PatternBId    = Shader.PropertyToID("_PatternB");
        private static readonly int BlendId       = Shader.PropertyToID("_Blend");

        private readonly StructureParts _parts = new();
        private readonly Transform[] _particles = new Transform[ParticlesPerSide * 2];
        private readonly Vector2[]   _positions = new Vector2[ParticlesPerSide * 2];
        private readonly Vector2[]   _velocities = new Vector2[ParticlesPerSide * 2];
        private Transform _gate;
        private Transform _courier;
        private Material  _courierMat;
        private Material  _material;
        private float     _gateHeight;
        private float     _floorY;
        private Vector2   _tripStart, _tripEnd;
        private float     _tripTime;

        private int   _patternA;
        private int   _patternB;
        private bool  _fading;
        private float _holdElapsed;
        private float _fadeElapsed;
        private float _dwell;

        /// <summary>
        /// Builds the ring, chambers and demon. Call once after the host has been positioned and scaled.
        /// The <paramref name="entity"/> is accepted for parity with the other structures.
        /// </summary>
        public void Initialize(Entity entity)
        {
            // The ring's centre sits at the host origin, so raise the host until the ring rests on the ground.
            var position = transform.localPosition;
            transform.localPosition = new Vector3(position.x, RingTube * transform.localScale.y, position.z);

            BuildRing();
            BuildChambers();
            BuildParticles();
            SeedPatterns();
            StartTrip();
        }

        private void BuildRing()
        {
            var mesh = _parts.Own(TorusMeshBuilder.Build(TubularSegments, RadialSegments,
                new TorusMeshBuilder.TorusProfile { MajorRadius = RingRadius, MinorRadius = RingTube }));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = GetComponent<MeshRenderer>();
            StructureParts.Plain(renderer);

            var shader = Shader.Find("MobileIdleBuilder/EntropySinkTorus");
            if (shader == null)
            {
                GameLogger.Warning("[EntropySinkStructure] Shader 'MobileIdleBuilder/EntropySinkTorus' not found; the ring keeps the placeholder material.");
                return;
            }

            _material = _parts.Own(new Material(shader));
            _material.SetColor(BaseColorId, GoldBase);       // fallback; PresenceReceiver MPB overrides per-instance
            _material.SetColor(AccentColorId, GoldAccent);
            renderer.material = _material;
        }

        private void BuildChambers()
        {
            _floorY = FloorY;
            var hot  = _parts.Flat(HotFloor);
            var cold = _parts.Flat(ColdFloor);
            if (hot == null || cold == null) return;

            // Angles run from +X towards +Z: the hot chamber is the +X half.
            var hotMesh  = _parts.Own(RevolveMeshBuilder.BuildSector(RingRadius, _floorY, -90f, 90f, 32));
            var coldMesh = _parts.Own(RevolveMeshBuilder.BuildSector(RingRadius, _floorY, 90f, 270f, 32));
            StructureParts.AddMesh("HotChamber", transform, hotMesh, hot, Vector3.zero);
            StructureParts.AddMesh("ColdChamber", transform, coldMesh, cold, Vector3.zero);

            // A wall along Z, either side of the opening.
            var wallMat = _parts.Flat(WallColor);
            float wallLength = RingRadius - RingTube - WallHalfGap;
            float wallCentreZ = WallHalfGap + wallLength * 0.5f;
            float wallY = _floorY + WallHeight * 0.5f;
            foreach (float side in new[] { -1f, 1f })
                StructureParts.AddPrimitive(PrimitiveType.Cube, "Wall", transform, wallMat,
                    new Vector3(0f, wallY, side * wallCentreZ), new Vector3(WallThickness, WallHeight, wallLength));

            // The gate, with the demon riding on top so it visibly works it.
            var gate = new GameObject("Gate");
            gate.transform.SetParent(transform, worldPositionStays: false);
            _gate = gate.transform;
            StructureParts.AddPrimitive(PrimitiveType.Cube, "GatePanel", _gate, _parts.Flat(GateColor),
                new Vector3(0f, wallY, 0f), new Vector3(WallThickness * 1.3f, WallHeight, WallHalfGap * 2f));

            var demon = _parts.Shaded(DemonColor, new Color(1f, 0.7f, 0.5f), 0.8f);
            float demonY = wallY + WallHeight * 0.5f + 0.045f;
            StructureParts.AddPrimitive(PrimitiveType.Sphere, "Demon", _gate, demon,
                new Vector3(0f, demonY, 0f), Vector3.one * 0.1f);
            foreach (float side in new[] { -1f, 1f })
                StructureParts.AddPrimitive(PrimitiveType.Sphere, "Horn", _gate, demon,
                    new Vector3(0f, demonY + 0.056f, side * 0.03f), new Vector3(0.02f, 0.065f, 0.02f));
        }

        private void BuildParticles()
        {
            var hot  = _parts.Shaded(HotParticle, Color.white, 0.5f);
            var cold = _parts.Shaded(ColdParticle, Color.white, 0.5f);
            for (int i = 0; i < _particles.Length; i++)
            {
                bool isHot = i < ParticlesPerSide;
                float side  = isHot ? 1f : -1f;
                float speed = isHot ? HotSpeed : ColdSpeed;

                _positions[i] = RandomInChamber(side);
                float a = Random.value * 2f * Mathf.PI;
                _velocities[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed;
                _particles[i] = StructureParts.AddPrimitive(PrimitiveType.Sphere, isHot ? "Hot" : "Cold", transform,
                    isHot ? hot : cold, ToLocal(_positions[i]), Vector3.one * ParticleSize);
            }

            _courierMat = _parts.Flat(ColdParticle);
            _courier = StructureParts.AddPrimitive(PrimitiveType.Sphere, "Courier", transform, _courierMat,
                Vector3.zero, Vector3.zero);
        }

        // x = local X, y = local Z.
        private static Vector2 RandomInChamber(float side)
        {
            float a = Random.Range(-1.2f, 1.2f);
            float r = Random.Range(0.1f, ChamberRadius - 0.05f);
            return new Vector2(side * r * Mathf.Cos(a), r * Mathf.Sin(a));
        }

        private Vector3 ToLocal(Vector2 p) => new Vector3(p.x, _floorY + ParticleSize * 0.6f, p.y);

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
            int next = Random.Range(0, PatternCount - 1);
            if (next >= current) next++; // skip the current index so we always change
            return next;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            MoveParticles(dt);
            MoveCourierAndGate(dt);
            CrossfadePatterns(dt);
        }

        private void MoveParticles(float dt)
        {
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null) continue;
                float side = i < ParticlesPerSide ? 1f : -1f;
                Vector2 p = _positions[i] + _velocities[i] * dt;
                Vector2 v = _velocities[i];

                // Bounce off the wall, then off the curved side of the chamber.
                if (p.x * side < WallThickness * 2f)
                {
                    p.x = side * WallThickness * 2f;
                    v.x = Mathf.Abs(v.x) * side;
                }
                if (p.sqrMagnitude > ChamberRadius * ChamberRadius)
                {
                    Vector2 normal = p.normalized;
                    p = normal * ChamberRadius;
                    v -= 2f * Vector2.Dot(v, normal) * normal;
                }

                _positions[i]  = p;
                _velocities[i] = v;
                _particles[i].localPosition = ToLocal(p);
            }
        }

        private void StartTrip()
        {
            _tripTime  = 0f;
            _tripStart = RandomInChamber(-1f);
            _tripEnd   = RandomInChamber(1f);
        }

        // One cold particle crosses through the gate, heating up on the far side, then fades into the crowd.
        private void MoveCourierAndGate(float dt)
        {
            if (_gate == null || _courier == null) return;

            _tripTime += dt;
            float t = _tripTime;
            if (t >= TripPeriod) { StartTrip(); t = 0f; }

            var gapNear = new Vector2(-0.05f, 0f);
            var gapFar  = new Vector2(0.05f, 0f);
            Vector2 position;
            float heat = 0f;
            if (t < ReachGate)
            {
                position = Vector2.Lerp(_tripStart, gapNear, t / ReachGate);
            }
            else if (t < ClearGate)
            {
                position = Vector2.Lerp(gapNear, gapFar, Mathf.InverseLerp(ReachGate, ClearGate, t));
            }
            else
            {
                float along = Mathf.Clamp01(Mathf.InverseLerp(ClearGate, ReachChamber, t));
                position = Vector2.Lerp(gapFar, _tripEnd, along);
                heat = along;
            }

            _courier.localPosition = ToLocal(position);
            float fade = 1f - Mathf.Clamp01(Mathf.InverseLerp(ReachChamber, ReachChamber + 0.5f, t));
            _courier.localScale = Vector3.one * (ParticleSize * 1.5f * fade);
            if (_courierMat != null) _courierMat.SetColor(BaseColorId, Color.Lerp(ColdParticle, HotParticle, heat));

            float target = t >= GateOpensAt && t < GateShutsAt ? GateLift : 0f;
            _gateHeight = Mathf.MoveTowards(_gateHeight, target, GateSpeed * GateLift * dt);
            _gate.localPosition = new Vector3(0f, _gateHeight, 0f);
        }

        private void CrossfadePatterns(float dt)
        {
            if (_material == null) return;

            if (_fading)
            {
                _fadeElapsed += dt;
                float b = Mathf.Clamp01(_fadeElapsed / FadeDuration);
                _material.SetFloat(BlendId, b);

                if (b >= 1f)
                {
                    // B is now fully shown: promote it to A, pick a new B, and reset the crossfade.
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
                _holdElapsed += dt;
                if (_holdElapsed >= _dwell)
                {
                    _fading      = true;
                    _fadeElapsed = 0f;
                }
            }
        }

        void OnDestroy() => _parts.Dispose();
    }
}
