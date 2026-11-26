# Store Listing — Quantum Foundry

Source of truth for the store text and graphics. Edit here first, then paste into the consoles, so the
listing has history. Privacy and Data safety answers live in `docs/runbooks/store-privacy-declarations.md`.

## When to review this file

Every PR **into `develop`** (the release -> develop promotion) runs the "Store listing" check in
`.github/pull_request_template.md`. Update this file, and then the consoles, when the release:

- adds or removes a headline feature, world, mode or currency named in the descriptions
- changes what is free vs paid, ads, or the ad-free offer
- changes the app name, icon or the look of the screens in the screenshots
- adds an SDK or data type (also update the privacy declarations and `docs/privacy-policy.html`)
- adds a language to the game (add a store translation for it)

## Google Play — Main store listing

### App name (29/30)
```
Quantum Foundry: Idle Factory
```

### Short description (78/80)
```
Build matter itself: an idle factory from quarks to atoms, molecules and life.
```
Alternative (80/80): `Idle physics factory: build atoms from quarks, then molecules and a Dyson Sphere`

### Full description (about 2,000/4,000)
```
Start with a handful of quarks and electrons. End with a Dyson Sphere.

Quantum Foundry is an idle factory game where you build matter itself. Harvest particles from glowing quantum fields, lay belts, and wire up machines that turn quarks into protons and neutrons, fuse them into atoms, and bond atoms into molecules. Every building shows the physics it performs, so your factory is a working picture of how the universe is put together.

BUILD A FACTORY OUT OF PHYSICS
- Place harvesters on quantum fields and route belts of particles between machines
- Split, filter and power your production lines
- Watch fusion, decay chains and chemical reactions happen on the belts

DISCOVER THE PERIODIC TABLE
- Research your way from hydrogen to heavier elements, then on to molecules
- Unlock new worlds: Physics, then Chemistry, then Biology, each with its own maps and rules
- Real science drives the tech tree. Learn how atoms are made just by playing

IDLE AND OFFLINE PROGRESS
- Your factory keeps producing while the app is closed
- Come back to collect, upgrade and expand
- Hire managers and upgrade them to boost your buildings

PRESTIGE AND THE ENDGAME
- Reset a run for permanent upgrades that make every next run faster
- Build the Dyson Sphere, a megastructure that powers your whole empire

COMPETE EVERY WEEK
- Join weekly tournament leagues and race other players' factories
- Climb the leagues, earn Strings and spend them on upgrades, name colours, titles and belt themes

ALWAYS SOMETHING TO DO
- Daily challenges, a login calendar and achievements
- The Cyclotron Kick minigame: time your taps as a particle crosses the accelerator gap for a big entropy boost
- A guided tutorial takes you from your first quark to your first Carbon atom

Your progress is saved to the cloud, so you can pick up on a new device. Ads are optional: watch one only when you want its reward.

Quantum Foundry is in active development. Tell us what you think with Send Feedback in the game's settings.
```

Check before each release: the tournament Strings shop line needs the build that ships it (in
`docs/whats-new.md` as of 2026-11-25); the world list should match what players can reach.

### Category and tags (Store settings)
- Category: **Simulation**
- Tags (up to 5, from Google's list; pick the closest names): Idle, Tycoon/Management,
  Building/Factory/Automation, Science (or Educational), Offline/Casual

### Graphics
| Asset | Spec | File |
|---|---|---|
| App icon | 512x512 PNG | `docs/store/icon-512.png` (from `Assets/Art/Icons/quantum_foundry_icon_1024.png`) |
| Feature graphic | 1024x500 PNG, no alpha | `docs/store/feature-graphic.png` |
| Phone screenshots | 2-8, 9:16, >= 1080 px short side | `docs/store/screenshots/` |
| Tablet screenshots | optional (7" and 10") | not yet |

Regenerate the icon and feature graphic with `python scripts/store/feature_graphic.py` (Pillow; Inter font
from the Unity Editor install). Screenshot order: busy factory, fusion/decay making an element, research
tree, Chemistry molecules, tournament board, Dyson Sphere.

### Translations
English (en-US) is the default language; other languages use Play Console's machine translation
(**Grow users -> Translations**, or **Main store listing -> Manage translations**). Machine translations are
not reviewed by a person, so re-run them after every English change and spot-check the big languages.
"Import translations" is for uploading translations you already have; it does not translate.

## App Store (when the iOS listing is set up)

- Subtitle (20/30): `Idle Physics Factory`
- Keywords (99/100, without words already in the name or subtitle if that subtitle is used):
  `idle,factory,physics,science,atom,particle,tycoon,incremental,automation,conveyor,chemistry,offline`
  With the subtitle above, drop `idle,factory,physics` and add `element,builder,simulator`.
- No competitor names in keywords (App Review rejects them).
