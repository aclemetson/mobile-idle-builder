# Fix: Field particles render magenta on Android (shader variant stripped)

**Status:** fix applied (pending on-device verification) — off release/0.3.
**Type:** quick fix / bug
**Required reading:** `docs/agents/architecture.md`. Plus current code:
`Assets/Scripts/Gameplay/FieldEffect.cs`, `Assets/Scripts/Gameplay/FieldGenerator.cs`,
`Assets/Materials/FieldParticleMaterial.mat`, `ProjectSettings/GraphicsSettings.asset`.
**Scope estimate:** S. One material asset reconfigured + a few lines in `FieldEffect.cs`; no schema/test changes expected.
**Branch:** off the current release branch (confirm target with user) -> PR.

## Symptom

Resource-field particle effects (reported as the "electron"/Lepton field) render **magenta on
Android** while looking correct in the Editor. Magenta = the shader/variant the GPU was asked for
was not present in the build.

## Root cause (confirmed in asset)

This is NOT the usual Built-in/Standard-shader-on-URP magenta. A full material audit (below) shows
every material already uses URP shaders and is wired in the scene.

The trigger is **GPU instancing variant stripping**. `FieldParticleMaterial.mat` serializes
`m_EnableInstancingVariants: 0`, so URP Lit's `INSTANCING_ON` variant is not referenced by any
serialized material and gets stripped from the Android build. But at runtime
`FieldEffect.ConfigureParticles` did:

```
var mat = new Material(particleMaterialTemplate);   // FieldEffect.cs
...
mat.enableInstancing = true;     // <-- forces the INSTANCING_ON variant
rend.material = mat;
```

The billboard particle renderer then requests the instancing variant, which isn't in the build ->
magenta. The Editor keeps all variants, so it looks correct there. Buildings/tiles use the same URP
Lit shader but never enable instancing at runtime (they use `MaterialPropertyBlock`), which is exactly
why only the field effect was magenta.

(Instancing on a Billboard ParticleSystemRenderer is also pointless — particle billboards don't use
the per-instance instancing path — so removing it costs nothing for the ~80 particles per field.)

A secondary, non-magenta discrepancy: the asset was **alpha** transparent (`_SrcBlend: 5`) while the
runtime forced **additive** (`_SrcBlend: 1`) via `SetFloat`. Blend factors are dynamic render state,
not shader variants, so this did not cause magenta, but it meant the asset did not represent the
effect honestly.

## Fix (applied)

1. `FieldEffect.cs` — removed `mat.enableInstancing = true;` (the magenta cause) and removed the
   runtime blend `SetFloat` calls; only the per-field `mat.SetColor("_BaseColor", baseColor)` remains.
   Added a comment documenting the stripping trap.
2. `FieldParticleMaterial.mat` — baked additive blend into the asset (`_SrcBlend: 5` -> `1`; the rest
   was already additive-shaped: `_DstBlend: 1`, `_BlendOp: 0`, `_ZWrite: 0`, `_Surface: 1`,
   `_Blend: 2`, keyword `_SURFACE_TYPE_TRANSPARENT`). Serialized state now matches what renders.

Follow-up (optional, not done): switching the template from URP **Lit** to URP **Unlit** would be
cheaper on mobile and shed more variants (particles get no benefit from lighting; matches the
`RenderingMaterials` "runtime primitives should be Unlit" convention). Deferred to keep this fix small.

## Verification (must be on-device, not just Editor)

- Build to Android and confirm field particles render with their field color, not magenta.
- Editor regression: fields still look correct; `scripts/test-local.ps1` stays green.
- Spot-check every field type (`Assets/Data/fields/*.asset`), since they share one template.

## Material audit (done 2026-06-16) — reference

Project has **no image/texture assets** at all (no PNG/JPG/TGA outside TextMesh Pro). All visuals are
procedural URP materials + particle systems + TMP. Three materials, all URP Lit, all wired:

| Material | Shader | Consumers | Wired |
|---|---|---|---|
| `BuildingMaterial` | URP Lit opaque | `RenderingMaterials.Opaque`, `BuildingVisualizer._buildingMaterial` | yes |
| `TileMaterial` | URP Lit transparent | `RenderingMaterials.Transparent`, `GridRenderer.tileMaterial`/`_highlightMaterial` | yes |
| `FieldParticleMaterial` | URP Lit transparent | `FieldGenerator._fieldParticleMaterial` | yes |

No null material refs -> the Built-in default-material magenta path is not in play. The only exposure
is the runtime additive-blend variant on the field particles described above.
