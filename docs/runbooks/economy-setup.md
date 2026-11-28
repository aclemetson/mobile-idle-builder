# Crystals on UGS Economy: setup and rollout

Design and phases: `docs/agents/tasks/feature-economy-crystals.md`. Everything is behind the Remote Config flag
`economy.enabled` (default **false**): off, the save holds the balance as before. Do these steps per environment
(development first), and turn the flag on only after phases 1-4 have shipped in the build players run.

## 1. Economy configuration (dashboard, once per environment)

The UGS CLI has no Economy deploy support, so this is set up by hand (it changes rarely).

1. Unity Cloud -> (project, environment) -> **Economy** -> Configuration.
2. **Currency**: ID `CRYSTALS` (exactly; `CrystalRules.CurrencyId`), name "Crystals", initial balance `0`, no maximum.
3. **Publish** the configuration. Until it is published the Cloud Code functions return `economy_config` and the
   game keeps showing the cached balance.
4. Phase 4 adds the real-money purchases (one per crystal pack product id) and the store keys (Google Play
   license key, App Store shared secret). Not needed for phases 1-3.

## 2. Access Control (block client Economy writes)

`live-ops/access-policy-economy.json` denies players every Economy write. Cloud Code uses the service token, so
only our functions (and receipt redemption, phase 4) can change a balance. Apply it per environment: dashboard
**Access Control** -> Project policies -> add the statement from the file, or `PATCH`
`https://services.api.unity.com/access/v1/projects/{projectId}/environments/{environmentId}/resource-policy` with
the file's `statements`. A request it blocks returns 403 with code **56**, which the game does not treat as a ban
(`AccountSuspension` keys on the per-player code 57 only).

## 3. Cloud Code

The functions (`CrystalWallet`, later `Spend` / `Grant`) live in `PveModule` and ship with its normal deploy
(`docs/runbooks/pve-backend-setup.md`). Nothing extra to deploy.

## 4. Turning it on

1. Development: Remote Config `economy.enabled = true`. Launch an internal build: the log shows
   `[Crystals] Moved N◆ from the save to the server (one time).` on the first launch and the HUD shows the server
   balance. Player Management -> the player -> Economy shows the same balance.
2. Production: the same, after the device checks in the task file pass.

To turn it off again, set `economy.enabled = false`: the game goes back to the save balance, which is the last
server balance the device saw (spends and grants made while on are in it).
