# UI Toolkit: GameHUD, Controllers, Panels

**Scope:** How the runtime UI is structured and how to add a panel without falling into the known traps. Read before any UI/UXML/USS work.

> Verified against: `deea0a4`, 2026-06-11. If code contradicts this doc, trust the code and update this doc.

## The one true document

`Assets/UI/GameHUD.uxml` is the ONLY gameplay HUD loaded at runtime (via `UIDocument` on the HUD GameObject in GameScene). `SplashScreen.uxml` and `LoadingScreen.uxml` cover their own scenes. **Any other UXML file is suspect — grep that it is loaded before editing it** (CLAUDE.md rule; standalone modal UXMLs have burned us before).

### Verified element inventory in GameHUD.uxml (by `name=`)

`top-bar` (:21), `left-drawer` (:42), `recipe-panel` (:73), `buildings-panel` (:84), `codex-panel` (:96), `research-panel` (:107), `upgrades-panel` (:118 — this is the prestige shop), `achievements-panel` (:132), `prestige-panel` (:152), `pvp-panel` (:165), `shop-panel` (:191 — premium/crystal shop), `settings-panel` (:210), `placement-overlay`/`placement-bar` (:249), `conveyor-overlay` (:260), `deconstruct-overlay` (:269), `building-inspector-panel` (:295), `demon-panel` (:306), `idle-return-modal` (:378).

Slide-in panels share `class="slide-panel hidden"` — visibility is toggled by adding/removing `hidden`.

## Controller pattern

- `Assets/Scripts/UI/HUDController.cs` is the orchestrator (large file, ~1300 lines).
- Sub-controllers are **MonoBehaviour components on the SAME GameObject as HUDController**, marked `[RequireComponent(typeof(HUDController))]`, and fetched with `GetComponent<>()` in `HUDController` (`HUDController.cs:106-111`). They are NOT constructed in code and NOT separate scene objects.
  - **Trap:** a new sub-controller class does nothing until the component is added to the HUD GameObject in `GameScene.unity`. `[RequireComponent]` does not retro-add it to an existing scene object — this is a manual Unity Editor step; flag it to the user in your final report.
- Lifecycle calls made by HUDController on each sub-controller: `Init(VisualElement root, HUDController hud)` (query elements by name, e.g., `root.Q("upgrades-panel")`) then `SetECSContext(EntityManager em)` once ECS is ready, then `Refresh()` when the panel opens. Copy `Assets/Scripts/UI/PrestigeShopSubController.cs` — it is the cleanest reference (panel + currency label + ScrollView list rebuilt in `Refresh()`).
- Drawer nav buttons are wired in HUDController around `HUDController.cs:347-369` (`root.Q<Button>("btn-recipes").clicked += () => TryOpenPanel(OpenRecipePanel);`). New panels add a button + `OpenXPanel` method here.
- Building rows/list items are built **in C#** (`new VisualElement()` + `AddToClassList`), not via UXML templates, in sub-controllers — follow that style.

## Styling

- `Assets/UI/tokens.uss` — design tokens (colors/spacing/typography vars). `Assets/UI/components.uss` — shared classes. `Assets/UI/GameHUD.uss` — HUD layout. Panel-specific: `AchievementsMenu.uss`, `PremiumShop.uss`.
- Reuse existing classes (e.g., `upgrade-row`, `upgrade-row--locked`, `slide-panel`) before writing new USS. Grep `GameHUD.uss`/`components.uss` for a class before inventing one.

## Checklist: adding a new panel

1. In `GameHUD.uxml`, copy an existing `slide-panel` block (e.g., `upgrades-panel`: header label, currency label, `ScrollView`) and rename ids.
2. Add a nav `Button` in the `left-drawer` nav scroll (copy `btn-upgrades` markup).
3. New sub-controller class copying `PrestigeShopSubController.cs` (`Init` / `SetECSContext` / `Refresh`).
4. In `HUDController.cs`: add the `GetComponent<>()` field (`:106-111` region), wire the button (`:347-369` region), add the `OpenXPanel` method mirroring an existing one (closes others, removes `hidden`, calls `Refresh()`).
5. Tell the user to add the component to the HUD GameObject in GameScene (manual step).
6. Use `ToastService` for transient feedback, not ad-hoc labels.
7. Verify visibility: panel must not be clipped by a zero-height wrapper; check parent sizing classes (CLAUDE.md recurring bug).
8. Mobile: respect safe area (`SafeAreaAdapter` exists); test portrait sizing.

## Pitfalls

- `root.Q<T>("name")` returns null silently for typos — null-check and log, especially in `Init`.
- Per-frame ECS reads belong in `HUDController.Update` flow or `HUDStatusBarController`; panel `Refresh()` is on-open only.
- Don't edit `GameHUD.uss` design tokens for one panel — add a panel-specific USS if needed and register it on the panel root.
