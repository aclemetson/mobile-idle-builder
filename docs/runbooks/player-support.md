# Player support in the Unity Cloud dashboard

How to handle player support from **Unity Cloud -> (project) -> Player Management**. We use these tabs:
Access, Logs, and State (Cloud Save). We do not use Economy, Progression (Leaderboards) or Moderation; see
"Tabs we do not use" for why.

## Find the player

1. Get the player's **UGS player ID**. Settings shows the first 8 characters (Account row); ask the player for that, or search by the PvE
   display name if they are in a league.
2. Player Management -> search by player ID. Check the environment selector first: `production` for store
   builds, `development` for editor and dev builds.

## Common tasks

### Ban a cheater (Access tab)
- **Ban** the player in the Access tab. Verified on a device (2026-11-28): the player stays signed in, but
  every service call is refused with HTTP 403 and Unity error code 57 ("Principal is not authorized to
  access resource"): Cloud Save load and push, player name, tournament Cloud Code.
- The game recognizes that (`AccountSuspension.cs`). After the first refused call it shows a one-time
  "Account suspended" card with the support email. Settings shows the account as suspended, cloud sync and
  tournament calls stop for the session, and local play goes on. It never treats the refused cloud load as
  "no cloud save" or makes a new player, so the cloud copy is left as it was.
- A ban applied while the game is running takes effect on the next refused call. The card shows then, or on
  the next launch if nothing calls a service before that.
- Their existing PvE bracket entry stays on the board until settlement. To remove it before then, edit the
  bracket's `m_<playerId>` custom item in Cloud Save (Custom items -> the bracket id) or wait for the
  week to settle.
- **Lift** the ban in the same tab. The player is back after relaunching the app (the suspension is never
  saved), and their next push uploads the progress they made on the device meanwhile. Note the reason and
  date in the issue tracking the report.
- *Disable player* (a different dashboard action) has not been tested with the game; use the ban.

### Delete a player's data (email deletion requests)
Players can delete their own account in the game (Settings > Delete account,
`docs/agents/tasks/feature-account-deletion.md`). Email requests (`docs/privacy-policy.html#delete`) are the
fallback, for example a player who can no longer open the game, and the only route for analytics records:
1. Confirm the request comes from the player (they send the player ID prefix from Settings and match their PvE name or Google email if linked).
2. Access tab -> **Delete player**. Check that the player's Cloud Save data (State tab) is gone afterwards;
   delete the keys by hand if not.
3. Remove their PvE bracket entries (`m_<playerId>` in any bracket custom item they joined) so their
   display name stops showing. Their current bracket id is `bracketId` in the `pve_profile` protected item (older weeks may also hold an entry); read it before
   deleting the player.
4. Reply to the player confirming deletion, within 30 days of the request.

### Investigate a bug report (Logs tab, State tab)
- **Logs:** Cloud Code calls from this player (PvE join, snapshot, claim) with errors and timings. Start
  here for "my tournament score didn't save" or "can't claim rewards".
- **State:** the player's Cloud Save keys. The game save is one key; `pve_profile` is a protected item
  written only by Cloud Code. Download the save JSON to reproduce locally (dev console snapshot loader).
  Do not edit the save key while the player is online: the next autosave overwrites it.

### Grant or refund crystals
Crystals live in the save (client-side), not in Economy, so the dashboard cannot grant them. Edit the
save key's crystal balance in the State tab while the player is offline, and ask them to relaunch. If
this becomes common, that is the signal to move crystals to UGS Economy.

### Purge test players (the Editor's leftover guests)
Every "clear all save" or lost Editor sign-in creates an anonymous player nobody can sign in as again.
`scripts/purge_test_players.py` deletes them in bulk: Cloud Save data in both environments first, then the
account (deleting only the account would leave the data behind). It never touches a player with a linked
identity (Google, username), any production data, or a save whose `platforms` stamp lists Android or iOS
(`SaveData.platforms`, `SavePlatform.cs`). Saves written before the stamp existed (2026-11-28) are kept
unless you pass `--include-unstamped`.

1. Unity Cloud -> Administration -> Service accounts -> create `ops-player-purge` with project roles for
   Player Authentication admin (list + delete players) and **Cloud Save Editor**. Keep the key in the
   password manager only (`docs/runbooks/secrets.md`).
2. In a shell, set `UGS_PURGE_SERVICE_KEY_ID`, `UGS_PURGE_SERVICE_KEY_SECRET`, `UGS_PROJECT_ID`,
   `UGS_ENV_ID_DEVELOPMENT`, `UGS_ENV_ID_PRODUCTION` (ids from Projects -> Environments).
3. Dry run: `python scripts/purge_test_players.py` (optionally `--created-before YYYY-MM-DD`). It prints a
   keep/purge tally by reason and writes `purge-<timestamp>.jsonl`; nothing changes. Check a few of the
   purge ids in Player Management before going on.
4. Delete: the same command plus `--confirm` (at most `--max`, default 100, per run). Rate limits allow
   roughly 500 player deletions per 30 minutes; the script waits on HTTP 429.
5. Delete the service account key afterwards.

The log files hold player ids only; delete them once you're done.

## Tabs we do not use
- **Economy:** crystals are client-side for now. A move to Economy (server-authoritative balance, server
  receipt checks, dashboard grants) is a separate design task once crystal tampering matters; it needs an
  offline policy first, because every crystal spend would become a network call.
- **Progression / Leaderboards:** PvE standings are built in Cloud Code (`CloudCode/PveModule`).
- **Moderation:** built for player reports and chat. Our only player-written content is display names;
  disable the player (Access) for an abusive name.
