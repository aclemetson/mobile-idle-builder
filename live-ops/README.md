# Live ops: game-data publisher (#210)

`gamedata.json` is the reviewed source for the `gamedata.*` Remote Config keys. Use it to re-tune allow-listed
numbers and to announce the change, without a store release. Players get the new values on their next cold start.

| File field | Remote Config key | Notes |
|---|---|---|
| `version` | `gamedata.version` (int) | Raise it with each publish. Analytics uses it as a label on `game_update_notice`. |
| `notice.title` / `notice.message` | `gamedata.noticeTitle` / `gamedata.noticeMessage` | Text of the one-time update card (title max 60 chars, message max 600). |
| `overrides` | `gamedata.overrides` (string) | Same schema as `Assets/Data/overrides.sample.json`. `{}` sections run the baked values. |
| (workflow input `announce`) | `gamedata.updatedUtc` | Set to the publish time, so every player sees the card once. |

## Changing values

1. Edit `gamedata.json` in a PR. The **Publish Game Data** check validates it: JSON shape, fields allowed by
   the `Set*Field` switches in `Assets/Scripts/RemoteConfig/GameDataOverrides.cs`, ids that exist in
   `Assets/Data/game_data.json`, and finite, non-negative values. Run it locally with
   `python scripts/gamedata_publish.py validate`.
2. Merge, then in GitHub go to Actions > **Publish Game Data** > Run workflow. Pick the branch that holds the
   change, set the environment (default `development`) and leave `announce` on to show the update card.
   Turn on `dry_run` to see the payload without writing it.
3. Production also needs `confirm` set to `production`.
4. Cold-start a build pointed at that environment and check the card and the changed value.

The workflow writes all keys in one Remote Config PUT through `scripts/set-remote-config.ps1`, using the
`ci-project` service account (`UGS_PROJECT_SERVICE_KEY_ID` / `_SECRET`, Remote Config Admin), the same one
the release workflows use for Remote Config. A production publish waits for approval on the `production`
GitHub environment, which holds `UGS_ENV_ID_PRODUCTION` (docs/runbooks/secrets.md).

A development publish can run from any branch, which is how to try values on an internal build before
merging them. Publish `main` again afterwards to put development back on the merged values. Publishing replaces
`gamedata.overrides` entirely, so edits made by hand in the dashboard are overwritten. Keep this file as
the only source.

Manual workflows only show up once the workflow file is on `main`.
