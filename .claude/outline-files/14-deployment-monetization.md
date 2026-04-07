# 14 — Deployment & Monetization

## Currency System

### Three-Currency Economy
| Currency | Earned By | Spent On |
|----------|-----------|---------|
| **Base Currency** | In-game production and selling items | Buildings, non-persistent upgrades, grid expansion |
| **Prestige Currency** | Prestiging (based on build net worth) | Permanent upgrades (main menu) |
| **Premium Currency** | Slow in-game earn (achievements, events) OR real money purchase | Speed-ups, premium cosmetics, premium music packs |

### Premium Currency Design Rules
- Never sold directly as gameplay power — only time savings and cosmetics
- Earnable through play (achievements, weekly competition rewards, seasonal events) — not locked behind paywall
- Speed-ups purchased with premium currency reduce wait times but don't bypass progression gates
- All cosmetics available via achievements OR premium currency — no achievement-exclusive items are premium-locked

## Monetization Model

### Free-to-Play Base
- Full crafting loop, all tiers, prestige system, PVP — free with no paywalls
- Every item earneable through play

### Premium Currency IAP
| Pack | Price (USD, approx) |
|------|-------------------|
| Small Pack | $0.99 |
| Medium Pack | $4.99 |
| Large Pack | $9.99 |
| Mega Pack | $19.99 |

### Speed-Ups (Premium Currency)
- Instant-complete current research
- Skip isotope build timer
- 2x production boost for 1/4/12 hours
- Instant grid expansion (skip currency cost)

### Cosmetic Packs (Premium Currency)
- Premium building skin sets
- Premium particle effect trails
- Premium music packs (Synthwave, Orchestral Sci-Fi)
- Premium HUD color themes
- Premium grid tile sets

### Supporter Pack (One-Time IAP)
- Remove rewarded ads permanently
- Bonus premium currency
- Exclusive supporter badge on PVP profile
- Price: ~$4.99

### Battle Pass (Seasonal)
- 4-week seasonal pass
- Free track: basic rewards earnable by all
- Premium track: exclusive cosmetics, premium currency bonuses
- Price: ~$4.99/season

### Rewarded Ads (Opt-In Only)
- Never forced or interstitial
- Examples: "Watch ad for 30 min 2x production", "Watch ad for bonus prestige currency"
- Removed permanently with Supporter Pack purchase

## Launch Strategy

### Platforms
- iOS (App Store) and Android (Google Play) — simultaneous launch
- No PC/console at launch

### Release Regions
- Phase 1: English-speaking markets (US, UK, AU, CA)
- Phase 2: EU markets + localization
- Phase 3: Global rollout

### Store Presence
- Polished screenshots showing build grid, megastructures, and prestige screen
- Preview video: 30-second trailer showing progression from quark to Dyson sphere
- ASO: keyword research for idle, crafting, science, factory game categories
- Press kit for indie game media outreach

## Post-Launch Content
- Content updates every 4–6 weeks (new tiers, buildings, seasonal events)
- Weekly PVP competition runs continuously
- Seasonal events with limited-time crafting challenges and exclusive cosmetics
- Community roadmap — player votes on next features via Discord

## Analytics
- Firebase Analytics for funnel tracking (tutorial completion, first prestige, PVP unlock)
- AWS CloudWatch for server-side performance and error monitoring
- Key events tracked: tutorial_complete, first_prestige, pvp_entered, iap_purchased, ad_watched

## Revenue Targets
- [ ] Define ARPU targets per region
- [ ] Define breakeven point based on dev cost
- [ ] Define marketing budget for soft launch phase

## Notes / Open Questions
- [ ] Premium currency earn rate through play — needs balancing to feel rewarding without undermining IAP
- [ ] Battle pass season cadence — align with PVP season resets?
- [ ] Localize pricing per region (e.g. lower price points for SEA markets)
