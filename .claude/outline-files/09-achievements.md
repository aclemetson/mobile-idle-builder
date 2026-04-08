# 09 — Achievement System

## Goals
- Reward exploration and milestones
- Reinforce educational moments
- Drive retention through long-term goals
- **Unlock cosmetics and other rewards** — achievements are the primary free path to cosmetics

## Achievement Categories

| Category | Examples |
|----------|---------|
| Crafting | "First Atom" — craft your first atom |
| Automation | "Hands Free" — run 1 min without tapping |
| Science | "Periodic Pioneer" — unlock 20 elements |
| Isotopes | "Unstable Genius" — craft your first radioactive isotope |
| Nuclear | "Chain Reaction" — trigger alpha decay for the first time |
| Scale | "Factory Floor" — have 10 buildings active simultaneously |
| Prestige | "Fresh Start" — complete your first prestige |
| Prestige | "Veteran" — complete 10 prestiges |
| Exploration | "Deep Matter" — reach subatomic tier 3 |
| Codex | "Curious Mind" — unlock 50 codex entries |
| Research | "Mad Scientist" — complete 10 research projects |
| Megastructure | "Dyson Dreamer" — place your first Dyson swarm node |
| Social/PVP | "Competitive Chemist" — win your first PVP match |

## Reward Types
| Reward | Examples |
|--------|---------|
| **Building skins** | Alternate visual themes for core buildings (neon, retro, crystal) |
| **Particle effects** | Custom item trail effects on conveyors |
| **Grid themes** | Alternate grid tile appearances (dark metal, holographic, organic) |
| **HUD accents** | Color scheme variations for the floating HUD |
| **Codex covers** | Decorative codex panel themes |
| **Profile badges** | Displayed on PVP profile and leaderboards |

## Implementation
- Achievements tracked locally and synced to AWS (DynamoDB)
- Platform achievements mirrored to **Google Play Games** and **Apple Game Center**
- In-game achievement panel with progress bars and reward previews
- Cosmetics unlocked immediately on achievement completion — no additional steps

## Hidden Achievements
- A subset of achievements are hidden until unlocked — reward curious players who experiment
- Examples: discover a rare isotope, trigger a decay chain, find an obscure molecule
- Hidden achievements marked with "???" in the achievement panel until earned

## Notes / Open Questions
- [ ] Define full cosmetic catalog so achievement rewards feel meaningful from launch
- [ ] Decide if any achievements are prestige-count gated (e.g. only available after prestige 5)
- [ ] Should cosmetics also be purchasable via IAP, or achievement-exclusive?
