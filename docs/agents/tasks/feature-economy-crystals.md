# Feature: Crystals on UGS Economy (server-held balance)

**Status:** spec written 2026-11-28; phase 1 in progress.
**Size:** XL (5 phases, one PR each).
**Required reading:** `architecture.md`, `save-system.md`, `economy-balance.md` (Crystals), `feature-flags.md`,
`tasks/feature-pve-tournaments.md` (Cloud Code module patterns), `../runbooks/pve-backend-setup.md` (deploy).

## Why

Crystals (paid currency) live in `SaveData.paidCurrency` on the device, so anyone can edit them, and support
can't grant or refund them except by hand-editing a save. With UGS Economy the server holds the balance:
purchases are checked against the store receipt, earned crystals are checked by Cloud Code, and the Player
Management > Economy tab shows and edits each player's balance.

## Decisions (user, 2026-11-28)

| Question | Decision |
|---|---|
| Offline | **Block crystal spends while offline.** The rest of the game keeps working offline. |
| Scope | **Crystals only.** Entropy, prestige currency, Strings, Insight stay in the save. |
| Earned crystals | **Server-checked grants**: tournament rewards paid by Cloud Code itself; achievement / daily / minigame / ad grants go through a Cloud Code function that checks the claim. |
| Existing balances | **Trust once, then server**: the save's balance moves to Economy once (capped), then the save copy is only a display cache. |

## Where crystals move today (all client-side)

| Kind | Code | Server rule after the switch |
|---|---|---|
| Grant: achievement claim | `AchievementService.ClaimReward` (`paidCurrencyReward`) | id must exist with that reward; once per achievement period (daily / weekly / monthly from server time; progression once ever) |
| Grant: daily login | `DailyEventService.GrantReward` | once per server day; amount from the daily table |
| Grant: Cyclotron Kick bonus | `CyclotronProgress.Collect` (`BonusCrystals`) | fixed amount; at most N per server day |
| Grant: rewarded ad | `AdService.GrantReward` (Crystals, SurgeRoll) | fixed amount per placement; per-placement daily cap |
| Grant: tournament reward | `TournamentRules.ApplyReward` (client adds `reward.crystals`) | Cloud Code `Claim` increments Economy itself; the client stops adding |
| Grant: IAP crystal pack | `IAPService.ProcessPurchase` -> `PremiumShopService.AwardCrystals` | Economy real-money purchase: `RedeemGooglePlayPurchaseAsync` / `RedeemAppleAppStorePurchaseAsync` validate the receipt and grant |
| Spend: shop (speed-up, entropy, prestige currency, Time Warp) | `PremiumShopService.TryBuy*` via `PremiumShopCalculator` | price from the embedded game data per tier; decrement fails on insufficient balance |
| Spend: research skip | `ResearchService.SkipActive` (`ActiveSkipCost`, time-scaled) | cost capped at the research's full skip price; must be > 0 |

Grant caps and ledgers live in a protected Cloud Save player item `crystal_ledger` (client can read, only the
server writes), like `pve_profile`.

## Architecture

- **Economy config** (Crystals currency `CRYSTALS`, real-money purchases per crystal-pack product id) as
  deployable files deployed by CI next to the Cloud Code module.
- **Access Control** project policy: players may read their balance but never write it (deny client Economy
  balance writes), so only Cloud Code and receipt redemption change it.
- **Cloud Code** module `CrystalModule` (same deploy script as `PveModule`): `Wallet` (balance + ledger,
  runs the one-time migration), `Spend(sku, tier | researchId)`, `Grant(source, id)`. Prices and rewards from
  the embedded `game_data.json`, as `PveModule` does.
- **Client** `CrystalWallet`: the balance shown in the HUD (server value, cached in `paidCurrency` for offline
  display), `CanSpend` (online and not suspended), async `SpendAsync` / `GrantAsync`, refresh on launch and
  resume. Every call site above goes through it.
- **Kill-switch** Remote Config `economy.enabled` (default false): off = today's local behaviour, unchanged.
  Lets each phase merge safely and the rollout go environment by environment.

## Phases (one PR each, all behind `economy.enabled`)

1. **Foundation**: Economy package, currency config + deploy, Access policy file, `CrystalModule` with
   `Wallet` + migration, `CrystalWallet` client (read + migrate + HUD display), flag, deploy script update.
2. **Spends**: shop tiers and research skip through `Spend`; offline / suspended shows "Connect to spend
   crystals".
3. **Grants**: achievements, daily login, Cyclotron, ads through `Grant`; tournament `Claim` pays Economy.
4. **Purchases**: real-money purchase configs; IAP redeems through Economy on Android and iOS.
5. **Rollout**: dashboard steps (Google Play license key, Apple shared secret, policy deploy), flag on in
   development, device test, then production. Update `economy-balance.md` and the support runbook (grant /
   refund from the Economy tab).

## Non-goals
- Strings, entropy, prestige currency on the server.
- Ad-view verification via LevelPlay server-to-server callbacks (the per-day cap limits abuse instead).
- Offline spending.
