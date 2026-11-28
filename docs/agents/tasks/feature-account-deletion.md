# Feature: In-app account deletion

**Status:** built on `feat/account-deletion` (2026-11-27); on-device check pending.
**Size:** M (one PR: client button + one Cloud Code endpoint + docs).
**Required reading:** `architecture.md`, `save-system.md`, `ui-toolkit.md`. Support runbook:
`docs/runbooks/player-support.md`.

## Why

Players can link a Google account (and Sign in with Apple is planned, #564), so the stores require a way to
delete the account from inside the app. Apple requires the deletion to start in the app; Google Play
requires an in-app path plus the web link we already have (`docs/privacy-policy.html#delete`). Email
requests (`docs/runbooks/player-support.md`) remain the fallback and the route for analytics records.

## What the player sees

Settings > Account > **Delete account** (red text). A `ConfirmPrompt` explains that progress, crystals,
league standings and the player name are deleted on every device, that store purchases are not refunded,
and that ad-free can be restored from the Shop. Confirming needs the "I understand this can't be undone"
checkbox ticked (otherwise nothing happens and the account row says so). While it runs the button reads
"Deleting account..." and is disabled. On success the scene reloads as a brand-new game with the tutorial.
On failure the account row shows why, and nothing on the device changes.

## Flow (server data first, account last, local data only after both)

`AccountDeletion.RunAsync(IAccountDeletionBackend)`, `Assets/Scripts/Services/AccountDeletion.cs`:

1. **Stop cloud pushes**: `SaveManager.SuspendCloudPushesAsync` (sets `CloudPushSuspended`, which
   `PushAndRecord` honours, and waits for a push in flight), so nothing recreates the save after step 2.
2. **Cloud Code `DeleteMyData`** (`PveTournament.cs`): caller only, no player id argument. Removes them from
   the current bracket (`PveRules.WithoutPlayer` on `meta.playerIds`, then deletes `m_<playerId>`), then
   `PveStore.DeleteAllPlayerData`: every Cloud Save key in all four access classes (game save, `pve_profile`,
   public, private). Older brackets are not touched: the profile records only the current one, and the
   user decided old entries do not matter (2026-11-27).
3. **Delete the UGS account**: `ICloudSaveService.DeleteAccountAsync` (also removes the Google link, signs
   out, clears the session token).

A failure at 2 or 3 resumes cloud pushes and returns `AccountDeletionFailure` (`Offline`, `ServerData`,
`Account`, `Busy`); a failure at 3 after 2 leaves an empty account, and tapping the button again finishes it.

4. **Local reset**: `AccountDeletion.ResetLocalGameAsync`: `SaveWipe.WipeEverything` (save, sidecars, all
   PlayerPrefs), then `StartNewGuestAsync` (after the wipe, because the wipe also clears the auth session
   keys), `ResetToFreshSave(newPlayerId)`, `ResumeCloudPushes`, the dev console's "clear all save"
   service resets plus `TournamentService.ResetForNewAccount`, and a scene reload.

Google: there is no local Google sign-out in our bridge, so `auth_google_autolink_off` (PlayerPrefs, set by
`DeleteAccountAsync` / `StartNewGuestAsync`) stops the silent Google link on later launches and the fresh
sign-in path. Only Settings "Sign in with Google" links Google again, which clears the flag.

## Non-goals
- No grace period / undo. Deletion is immediate.
- Store purchases are not refunded; ad-free is restored from the Shop (`IAPService` restores from the receipt).
- Analytics events already sent are not deleted by the button (13-month retention; email request deletes them).

## Tests
- EditMode `AccountDeletionTests`: step order, stops at the first failure with saving resumed, offline
  deletes nothing, failure messages, `PveRules.WithoutPlayer`, and the backend's `DeleteMyData` call.
- Cloud Code `ModuleTests.DeleteMyData_RemovesOnlyTheCallerFromTheBracketMeta_AndTheResponseRoundTrips`.
- On device (pending): delete, confirm the player is gone from Player Management (Access + State tabs) and
  from their bracket, the game restarts with the tutorial, and a relaunch keeps the new guest (same player
  id) without re-linking Google.
