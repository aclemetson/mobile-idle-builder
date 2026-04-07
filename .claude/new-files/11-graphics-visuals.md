# 11 — Graphics & Visuals

## Art Style Direction
- **Low-poly 3D** with a sci-fi aesthetic — premium feel without excessive geometry cost
- **Isometric or slight perspective camera** — works naturally with the square grid on mobile touch
- Color-coded tiers: blue = subatomic, green = atomic, teal = molecular, orange = alloys, purple = components, gold = megastructures
- Clean, readable silhouettes — buildings and items must be instantly distinguishable at mobile screen sizes
- Consistent with the sci-fi HUD design token system (dark backgrounds, glowing accents)

## Camera
- Fixed isometric angle (approx 45° horizontal, 30° vertical) — no free rotation on mobile
- Pinch to zoom in/out on the grid
- Pan by dragging
- Camera smoothly follows placed buildings during tutorial

## Visual Layers

### Items & Particles
- Subatomic particles: small glowing orbs (color-coded by type)
- Atoms: stylized low-poly spheres with electron orbit rings
- Molecules: bonded atom clusters, simplified geometry
- Alloys/components: geometric abstract shapes reflecting their function
- Items animated while in transit on conveyors

### Buildings
- Low-poly 3D models, flat-shaded with emissive accent details
- **Active animation:** subtle pulse, rotation, or glow when processing
- **Idle animation:** slow breathing pulse when waiting for inputs
- **Unpowered state:** desaturated, no emissive glow
- Core buildings (vault, lab, containment) visually larger and more imposing than transient buildings
- Radioactive containment building has visible warning indicators and decay particle VFX

### Grid & Environment
- Square grid tiles with subtle edge highlighting
- Dark space/lab environment background with parallax layers
- Power radius shown as a soft glowing circle overlay (toggleable)
- Grid expansion tiles shown as locked/purchasable zones at the edge of the current map

### Megastructures
- Dyson swarm nodes: orbiting geometric panels visible above the grid
- Dyson sphere: partial sphere visible in the background sky, grows as segments are added
- Star system view: zoomed-out overview showing multiple connected grids

## Unity Rendering Setup
- **URP (Universal Render Pipeline)** — mobile optimized
- **Shader Graph** for emissive glow, power radius overlay, and tier color effects
- **VFX Graph** for particle effects (decay particles, crafting bursts, unlock celebrations)
- **GPU instancing** enabled on all building and item meshes — critical for DOTS performance
- **LOD:** Single LOD level for buildings (mobile — no LOD switching needed at this scale)

## Geometry Budgets (Mobile)
| Object | Max Triangles |
|--------|--------------|
| Core building | 800 tris |
| Transient building | 400 tris |
| Item / particle | 50 tris |
| Grid tile | 2 tris |
| Dyson sphere segment | 1200 tris |

## Performance Targets
- All meshes use texture atlasing — minimize draw calls
- Max simultaneous VFX emitters: TBD based on profiling
- Target: stable 60fps on mid-range device with full grid populated

## Notes / Open Questions
- [ ] Commission 3D artist vs asset store base + custom shaders?
- [ ] Define exact camera FOV and isometric angle after prototype
- [ ] Colorblind-friendly palette pass before beta
