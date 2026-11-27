# PvE tournament backend: setup and testing

The weekly PvE tournaments (`docs/agents/tasks/feature-pve-tournaments.md`) run on a **UGS Cloud Code C# module**, `PveModule`, which stores its data in **Cloud Save**. This runbook covers the one-time dashboard setup, deploying the module, the weekly settlement schedule, and testing from the dev console.

## What lives where

| Piece | Location |
|---|---|
| Module source | `CloudCode/PveModule/PveModule/` (`PveTournament.cs` endpoints, `PveStore.cs` Cloud Save access, `PveGameConfig.cs` config) |
| Shared rules and models (compiled into both the game and the module) | `Assets/Scripts/PvE/PveRules.cs`, `PveModels.cs`, `Assets/Scripts/Services/PlayerNameRules.cs` |
| League ladder, rewards, name filter | `Assets/Data/game_data.json` → `game_config.pve`, `game_config.name_filter_patterns`. The module **embeds this file at build time** |
| Module reference for the Deployment window | `Assets/CloudCode/PveModule.ccmr` |
| Module tests (.NET) | `CloudCode/PveModule/PveModule.Tests/` (`dotnet test` in `CloudCode/PveModule`) |
| Rules tests (Unity) | `Assets/Scripts/Tests/PveRulesTests.cs` |
| Client | `Assets/Scripts/PvE/PveApi.cs` (dev console `pve ...` commands until phase 4 adds the UI) |

Cloud Save data the module writes:

| Store | Id / key | Contents |
|---|---|---|
| Private custom item `pve_<week>` | `index` | brackets opened per league this week |
| Private custom item `pve_<week>_<league>[_dev]_<n>` | `meta`, `m_<playerId>` | bracket members; one key per member with their snapshots |
| Custom item `pve_settings` (Game Data, any access class) | `settings` | test switches (below) |
| Protected player data | `pve_profile` | league, last entry, history, pending rewards |

"Private" custom items can't be read by game clients at all, and protected player data can be read by the player but only written by the server. A client can only change tournament data by calling the module.

## 1. One-time dashboard setup

1. Unity Cloud Dashboard → your project → the environment you test in (start with a non-production environment if you have one).
2. **Cloud Code**: open it once so the service is enabled for the project.
3. **Cloud Save**: already enabled (the game's cloud save uses it).
4. **Player Names / Authentication**: nothing to do; player names come with Authentication.

## 2. Deploy the module

Needs the **.NET 9 SDK** (the module targets .NET 9, like Unity's Cloud Code module template). The Deployment window runs `dotnet publish PveModule.sln -c Release -r linux-x64` and finds the module's main project through its single publish profile, `PveModule/Properties/PublishProfiles/FolderProfile.pubxml`. Keep exactly one `.pubxml` in `CloudCode/PveModule` ("Could not find a Publish Profile" / "Too many Publish Profiles" otherwise). The test project sets `IsPublishable=false` so it stays out of the upload.

1. In the Unity Editor menu bar: **Services → Deployment** (a top-level menu next to Window; package `com.unity.services.deployment`). It opens a dockable **Deployment** window.
2. Select the environment at the top.
3. The window lists deployable files found in the project. Find **PveModule.ccmr** (`Assets/CloudCode/PveModule.ccmr`; you can also right-click that file in the Project window), tick it, then **Deploy Selected**. The window builds `CloudCode/PveModule/PveModule.sln` with `dotnet publish` and uploads the result.
4. Dashboard → Cloud Code → Modules should now list **PveModule** with the functions `Status`, `Join`, `Snapshot`, `Board`, `Claim`, `SettleWeek`.

**Redeploy whenever** you change `game_config.pve`, `game_config.name_filter_patterns`, the shared rules or the module code. The module's copy of game_data.json is fixed at build time.

Before deploying, check that it builds and its tests pass:

```
cd CloudCode/PveModule
dotnet test
```

### Module functions added later

- `SetLook(nameColor, title)` (#500): the leaderboard look, as PveCosmetics ids validated server-side. Stored on the profile and on this week's member. Join copies it onto new members, and Board returns it per row. Redeploy the module so the game's equip calls stop failing; until then the client just retries on each refresh.

## CI deploy (automatic)

`release-internal.yml` deploys the module to the **development** environment whenever integration merges into a
`release/**` branch: the same trigger and commit as the internal Android build, so the development module always
matches the newest internal APK. Job `cloud-code` (ubuntu) runs `scripts/deploy-cloud-code.sh`: module tests, then
`dotnet publish -c Release -r linux-x64`, zipped into `PveModule.ccm`, then `ugs deploy` with the UGS
service account `ci-development` (`UGS_DEV_SERVICE_KEY_ID` / `..._SECRET`), to the environment **named**
`development`. The `.ccm` and the CLI's JSON result are kept as the run's `pve-module-<n>` artifact.
A failed deploy fails that job only; the Play upload still goes out.

One-time setup: `ci-development` needs **Cloud Code Editor** as an *environment* role on development, plus the
read-only *project* role **Unity Environment Viewer**: `ugs deploy` looks the environment name up with
`GetEnvironments` even when given a name, and fails with 403 "Error calling GetEnvironments" without it. It can't
reach production (UGS Key Check verifies that).

Run it by hand (same script; `--dry-run` changes nothing):

```
UGS_CLI_SERVICE_KEY_ID=... UGS_CLI_SERVICE_SECRET_KEY=... UGS_PROJECT_ID=... \
  ./scripts/deploy-cloud-code.sh development --dry-run   # an environment ID also works
```

**Keep the module backward compatible.** Installed builds lag the server: every new function parameter needs a
default, responses only gain fields, and nothing an older client sends may be refused for being old (see `atT`
and `Retire`, both added that way). With CI deploying on every release merge, a breaking change would reach
players' older builds without anyone redeploying by hand.

The Deployment window (above) still works for deploying a branch to development before it is merged.

## 3. Weekly settlement schedule

Every week's brackets are settled shortly after Sunday 00:00 UTC, once every 24h run has ended and its final snapshot is in (00:10 at the earliest). A player's own bracket is also settled when they next open the tournament (the `Status` call), so a missed schedule only delays other players' history and rewards until they open it.

1. Dashboard → **Triggers** (Scheduler):
   - Create a **schedule**: name `pve-settle`, **recurring weekly, Sunday 00:15 UTC**, payload `{}`. (If the dashboard asks for a time zone, pick UTC; if it only takes local time, convert.) Not midnight: the last runs end at Sunday 00:00 and their final snapshot is accepted for 10 more minutes (`PveRules.FinalGraceSeconds`), so `SettleWeek` refuses to settle before 00:10 (`PveRules.SettleAfter`) and would answer `too_early`.
   - Create a **trigger** on that schedule's event (the dashboard offers it by name, `com.unity.services.scheduler.pve-settle.v1`). Action type **Cloud Code module**, URN `urn:ugs:cloud-code:PveModule/SettleWeek`.
2. `SettleWeek` refuses calls made by a player, so only the trigger (a service call) can run it. With no `week` argument it settles the most recent week whose runs have all ended. It is safe to repeat: settled brackets and already-credited players are skipped.

The UGS CLI can do the same from a terminal (`ugs scheduler` / `ugs triggers`) if you prefer config files.

## 4. Test switches (`pve_settings`)

Dashboard → **Cloud Save → Game Data** (custom items live there; the other tabs are Configure Indexes, Player Data, Player Files) → add an item with ID `pve_settings`, key `settings`, and this value. Either access class works (the module reads private first, then default); clients can't write either:

```json
{ "smallBrackets": true, "allowDebugTime": true, "maxLeague": "bronze" }
```

| Switch | Effect |
|---|---|
| `smallBrackets` | every new bracket uses `game_config.pve.test_bracket_size` (5) instead of the league's size |
| `allowDebugTime` | the server honours the client's `debugNow` (dev console `pve time`) instead of its own clock |
| `maxLeague` | highest league players can reach (a league id). `bronze` in dev while there are too few players to compete: nobody is promoted and a profile above it plays Bronze from its next join. Blank or missing = the whole ladder. Takes effect on the next call, no redeploy |

**Set `smallBrackets` and `allowDebugTime` to false (or delete the item) in production, and leave `maxLeague` out there.** With `allowDebugTime` on, any client could choose its own time.

## 5. Testing from the dev console

Using the console marks the save as a dev save, so these joins go into separate `_dev` brackets that never mix with real players. Results arrive a moment later in the console log.

```
name status                         # a player name is required to join (Settings > Account)
pve time 2026-11-27T10:00:00Z       # a Friday: entry is open (needs allowDebugTime)
pve status
pve join
pve snap 1500 2.5                   # net worth 1500, idle rate 2.5/s
pve time 2026-11-27T16:00:00Z
pve snap 90000 40
pve board                           # values at your elapsed time; ~ = projected for players behind you
pve time 2026-11-29T01:00:00Z       # Sunday: status settles your bracket
pve status                          # history + pending reward
pve claim 2026-11-27
pve time off
```

To see the 10h/14h/6h comparison from the design doc, join with two or three accounts (a second device, or another Editor sign-in) at different debug times.

**Offline at the 24h mark.** The final score is the player's last snapshot plus idle earnings up to their idle cap. A snapshot the device took but couldn't deliver (the app was suspended) is kept on the device and sent on the next launch with its own time, as long as the week isn't settled yet (Sunday 00:10 UTC). `tournament status` shows it as `keptSnapshot`.

Errors the module returns: `outside_window`, `already_joined`, `name_required`, `name_blocked`, `not_joined`, `run_over`, `snapshot_rejected:<reason>`, `nothing_to_claim`, `config_missing`, `retired` (the player left the run early with `Retire`). A `network:` prefix means the call itself failed: the module isn't deployed, Cloud Code is off, or the device is offline.

## Limits and costs

- One join reads or writes about 5 Cloud Save items, and a snapshot about 3. Phase 4 sends a snapshot every 5 minutes of play, which is roughly 300 snapshots per player per tournament.
- A bracket of 100 players with 24h of 10-minute snapshots is about 100 keys of about 6 KB each. `Board` reads them all, paging through Cloud Save.
- Cloud Code and Cloud Save have free tiers. Check current pricing in the dashboard before the player base grows.
