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
| Private custom item `pve_settings` | `settings` | test switches (below) |
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

## 3. Weekly settlement schedule

Every week's brackets are settled at Sunday 00:00 UTC, once every 24h run has ended. A player's own bracket is also settled when they next open the tournament (the `Status` call), so a missed schedule only delays other players' history and rewards until they open it.

1. Dashboard → **Triggers** (Scheduler):
   - Create a **schedule**: name `pve-settle`, recurring cron `5 0 * * 0` (Sundays 00:05 UTC), payload `{}`.
   - Create a **trigger** on that schedule's event (the dashboard offers it by name, `com.unity.services.scheduler.pve-settle.v1`). Action type **Cloud Code module**, URN `urn:ugs:cloud-code:PveModule/SettleWeek`.
2. `SettleWeek` refuses calls made by a player, so only the trigger (a service call) can run it. With no `week` argument it settles the most recent week whose runs have all ended. It is safe to repeat: settled brackets and already-credited players are skipped.

The UGS CLI can do the same from a terminal (`ugs scheduler` / `ugs triggers`) if you prefer config files.

## 4. Test switches (`pve_settings`)

Dashboard → Cloud Save → Custom Items → create the custom ID `pve_settings` (**private** access class), key `settings`, value:

```json
{ "smallBrackets": true, "allowDebugTime": true }
```

| Switch | Effect |
|---|---|
| `smallBrackets` | every new bracket uses `game_config.pve.test_bracket_size` (5) instead of the league's size |
| `allowDebugTime` | the server honours the client's `debugNow` (dev console `pve time`) instead of its own clock |

**Set both to false (or delete the item) in production.** With `allowDebugTime` on, any client could choose its own time.

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

Errors the module returns: `outside_window`, `already_joined`, `name_required`, `name_blocked`, `not_joined`, `run_over`, `snapshot_rejected:<reason>`, `nothing_to_claim`, `config_missing`. A `network:` prefix means the call itself failed: the module isn't deployed, Cloud Code is off, or the device is offline.

## Limits and costs

- One join reads or writes about 5 Cloud Save items, and a snapshot about 3. Phase 4 sends a snapshot every 5 minutes of play, which is roughly 300 snapshots per player per tournament.
- A bracket of 100 players with 24h of 10-minute snapshots is about 100 keys of about 6 KB each. `Board` reads them all, paging through Cloud Save.
- Cloud Code and Cloud Save have free tiers. Check current pricing in the dashboard before the player base grows.
