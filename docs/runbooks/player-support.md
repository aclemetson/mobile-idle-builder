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
- **Disable** the player. Their next sign-in fails, so they cannot reach Cloud Save or PvE Cloud Code.
  The game falls back to local-only play; nothing else breaks.
- Their existing PvE bracket entry stays on the board until settlement. To remove it before then, edit the
  bracket's `m_<playerId>` custom item in Cloud Save (Custom items -> the bracket id) or wait for the
  week to settle.
- **Re-enable** to lift the ban. Note the reason and date in the issue tracking the report.

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

## Tabs we do not use
- **Economy:** crystals are client-side for now. A move to Economy (server-authoritative balance, server
  receipt checks, dashboard grants) is a separate design task once crystal tampering matters; it needs an
  offline policy first, because every crystal spend would become a network call.
- **Progression / Leaderboards:** PvE standings are built in Cloud Code (`CloudCode/PveModule`).
- **Moderation:** built for player reports and chat. Our only player-written content is display names;
  disable the player (Access) for an abusive name.
