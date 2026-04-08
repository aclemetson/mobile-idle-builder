# 07 — Reusable Components

## Philosophy
Build once, reuse everywhere. Components should be data-driven and configurable so adding new items, buildings, or UI panels requires minimal new code.

## UI Style Direction
- **Sci-fi HUD aesthetic** — clean, dark backgrounds, glowing accents, technical typography
- Built on a **design token system** (USS variables in UI Toolkit) so the entire visual style can be updated from a single file without touching individual components
- All colors, font sizes, spacing, and border radii defined as tokens — never hardcoded in components

### Design Tokens (USS Variables)
```css
/* Colors */
--color-primary: #00e5ff;        /* Cyan accent */
--color-secondary: #7c4dff;      /* Purple accent */
--color-background: #0a0e1a;     /* Deep space dark */
--color-surface: #111827;        /* Panel background */
--color-text-primary: #e0f7fa;   /* Light cyan text */
--color-text-secondary: #546e7a; /* Muted text */
--color-warning: #ffab00;        /* Alerts, energy warnings */
--color-danger: #ff1744;         /* Errors, decay events */
--color-success: #00e676;        /* Unlock, completion */

/* Spacing */
--spacing-xs: 4px;
--spacing-sm: 8px;
--spacing-md: 16px;
--spacing-lg: 24px;

/* Typography */
--font-size-sm: 11px;
--font-size-md: 14px;
--font-size-lg: 18px;
--font-size-xl: 24px;

/* Borders */
--border-radius: 4px;
--border-color: #1e3a5f;
--border-glow: 0 0 8px var(--color-primary);
```

## Gameplay Components (ECS)

| Component | Data |
|-----------|------|
| `ItemData` | Item ID, quantity, tier |
| `RecipeData` | Input list, output, craft time |
| `BuildingData` | Type, level, speed multiplier |
| `StorageData` | Capacity, current contents |
| `ConveyorData` | Source, destination, speed |

## UI Layout — Build View

### Always Visible (Floating HUD)
Minimal overlay sitting on top of the full-screen grid. Contains only what the player needs at a glance:

| Element | Position | Content |
|---------|----------|---------|
| Resource bar | Top | Current base currency, prestige currency |
| Power indicator | Top right | Current eV usage / capacity |
| Prestige button | Top left | Lights up when prestige is available |
| Settings / menu | Top right corner | Gear icon |
| Panel launcher buttons | Bottom bar | Buildings, Recipes, Codex, Research, Upgrades |

### On-Demand Panels (summoned via bottom bar buttons)
Slide up from the bottom or appear as an overlay — dismissible with a swipe or back tap:

| Panel | Trigger | Content |
|-------|---------|---------|
| Buildings Panel | Building icon | Browse and place buildings |
| Recipe Viewer | Flask icon | All known/locked recipes, filterable by tier |
| Codex | Book icon | All discovered entries, searchable |
| Research Panel | Atom icon | Research tree, active/available/locked nodes |
| Upgrade Screen | Star icon | In-build non-persistent upgrades |
| Prestige Screen | Reset icon | Net worth breakdown, prestige currency preview |

### Grid Interactions
- Tap empty tile → opens Buildings Panel filtered to placeable buildings
- Tap placed building → opens building detail (status, upgrade, remove)
- Long press → enter "edit mode" for moving/removing buildings
- Pinch to zoom in/out on the grid
- Power overlay toggle in HUD — highlights coverage radii

## UI Components

| Component | Purpose |
|-----------|---------|
| `ItemSlot` | Displays any item with icon + quantity |
| `RecipePanel` | Shows inputs → output for any recipe |
| `BuildingCard` | Info card for any building type |
| `ProgressBar` | Reusable fill bar (crafting, upgrades, research, XP) |
| `TooltipPopup` | Educational tooltip / codex preview overlay |
| `NotificationBanner` | Unlock / achievement / prestige ready notifications |
| `PowerRadiusOverlay` | Grid overlay showing eV coverage circles |
| `PanelContainer` | Standardised slide-up panel wrapper used by all panels |
| `TierBadge` | Color-coded tier indicator shown on items and buildings |

## ScriptableObjects (Data Layer)
- `ItemSO` — defines every item in the game
- `RecipeSO` — defines every crafting recipe
- `BuildingSO` — defines every building type
- `TierSO` — defines unlock conditions per tier

## Design Principles
- All game data lives in ScriptableObjects — no magic strings
- UI components bind to data, not hardcoded values
- New content = new ScriptableObject, no code changes required

## Notes / Open Questions
- [ ] UI Toolkit vs uGUI for component system?
- [ ] Localization support from the start?
