# Mobile button icons (X / check / rotate) render blank — fix + Editor steps

## Symptom
On Android, glyph buttons show blank/tofu instead of their icon: the close `✕`
(U+2715) on every panel, plus `✓` (U+2713), `↻` (U+21BB) and `⇄` (U+21C4) on the
placement / conveyor overlays. They render fine in the Editor and on desktop.

## Root cause
The HUD panel (`Assets/UI/GameHUD.uxml`) renders through
`Assets/Data/settings/gamePanelSettings.asset`, which has **`textSettings: {fileID: 0}`**
(no `PanelTextSettings` assigned) and uses `UnityDefaultRuntimeTheme.tss`
(`@import url("unity-theme://default")`). With no explicit font + fallback, UI Toolkit
falls back to the platform default font. The desktop default font happens to contain
these symbol glyphs; the Android default font does **not**, so the glyphs are missing
at runtime. This is a font-coverage problem, not a UXML/USS problem — the buttons and
their `text=` values are correct.

## Why this needs the Editor (can't be done headlessly)
The fix is a **font asset with the required glyphs baked into its atlas**, wired through
a `PanelTextSettings`. Building/inspecting a TMP font atlas requires the Editor's Font
Asset Creator; it cannot be authored or verified as a plain text/YAML edit, and it can
only be confirmed on an actual Android build/device.

## Recommended fix — PanelTextSettings + symbol fallback (Option A)

1. **Pick a font that contains the symbols.** LiberationSans (the current default) does
   not reliably cover `✕ ✓ ↻ ⇄`. Import a symbol-complete TTF — e.g. **Noto Sans Symbols**
   or **DejaVu Sans** (both cover U+2713/2715/21BB/21C4) into `Assets/Fonts/`.
2. **Create a Font Asset** from it: `Window > TextMeshPro > Font Asset Creator`.
   - Source Font File = the imported TTF.
   - Atlas Population Mode = **Dynamic** (so any glyph in the TTF renders at runtime; no
     need to pre-list character sets).
   - Generate + **Save** next to the font (e.g. `Assets/Fonts/NotoSansSymbols SDF.asset`).
3. **Create a PanelTextSettings**: `Assets > Create > UI Toolkit > Text Settings Asset`
   (name it e.g. `Assets/Data/settings/gamePanelTextSettings.asset`).
   - **Default Font Asset** = your main UI font asset (keep the current UI look).
   - **Fallback Font Assets** list → add the **symbol font asset** from step 2.
     (Order: primary UI font first, symbol font as fallback so only missing glyphs pull
     from it.)
4. **Wire it up**: select `Assets/Data/settings/gamePanelSettings.asset`; set
   **Text Settings** = the PanelTextSettings from step 3. (In YAML this populates the
   `textSettings:` reference that is currently `{fileID: 0}`.)
5. Repeat the Text Settings assignment for the other PanelSettings if their panels show
   the same glyphs: `Assets/Settings/MainMenuPanelSettings.asset` (splash/loading).
6. **Verify on device**: build to Android (Development Phone Build profile) and confirm
   the close `✕` and the placement/conveyor `✓ ↻ ⇄` all render. Editor Play mode is not
   sufficient — it uses the desktop font fallback that already works.

## Alternative — replace glyphs with image icons (Option B)
If you'd rather not manage a symbol font, swap the `text="✕"` glyph buttons for
background-image icons in USS:
- Add small SVG/PNG icons under `Assets/UI/Icons/` (e.g. `icon-close`, `icon-check`,
  `icon-rotate`, `icon-flip`).
- In `GameHUD.uss`, give `.close-btn` / the placement-confirm button classes a
  `background-image` and clear the text, or add modifier classes.
- Pro: no font dependency, crisp at any DPI. Con: ~20 close buttons + the overlay
  buttons to convert, and a USS pass. This is a larger UXML/USS change than Option A.

## Affected buttons (for verification)
All `text="✕"` close buttons in `GameHUD.uxml` (recipes, buildings, codex, research,
upgrades, managers, daily, rewards, achievements, prestige, megastructure, sites,
worlds, pvp, shop, settings, inspector, demon), plus `btn-cancel-candidate` `✕`,
`btn-confirm-place` `✓`, `btn-rotate-candidate`/`btn-conveyor-rotate` `↻`,
`btn-rotate-output` `↻`, `btn-flip-building` `⇄`.
