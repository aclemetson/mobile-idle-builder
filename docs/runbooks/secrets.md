# Secrets: inventory, storage and rotation

Every key the game, CI and the live services depend on: where it lives, what reads it, what a leak would cost, and
how to rotate it. Issue #534.

> Inventory taken 2026-11-26 from `.github/workflows/*.yml`, `gh secret list`, the local `secrets/` folder and the
> runbooks. Re-check it whenever a workflow gains a `secrets.*` reference or a new service is added.

## Rules

- No secret is ever committed. `/secrets/`, `*.keystore` and `google-services.json` are gitignored; the history scan
  below found nothing.
- CI secrets live in GitHub Actions secrets only. GitHub cannot show a secret's value again, so the
  **master copy of every value lives in a password manager** (and the local `secrets/` folder for files). If the
  password manager doesn't have it, rotate it rather than trying to recover it.
- Client-side IDs that ship inside the app (LevelPlay App Keys, Google OAuth client IDs, Firebase identifiers,
  UGS project ID) are **not secrets**. Anyone can pull them out of the APK. They stay in source.
- Repo protections, checked 2026-11-26: secret scanning **on**, push protection **on**, Dependabot security updates
  **on**. Non-provider patterns and validity checks are **off** (see "To do").

## Inventory

Blast radius is what someone holding the value could do. "Rotates" says whether a new key can replace the old one
without touching shipped builds.

### GitHub Actions repository secrets

| Secret | What it is | Read by | Blast radius if leaked | Rotates |
|---|---|---|---|---|
| `UGS_GAME_CONFIG_SERVICE_KEY_ID` / `UGS_GAME_CONFIG_SERVICE_SECRET_KEY` | key of the UGS service account `ci-cd-automation` | `release-internal`, `release-ios`, `prod-release`, `publish-gamedata` | **High**: edit Remote Config (maintenance mode, kill-switches, game data overrides) and deploy Cloud Code (PvE module) in every environment the account's roles cover | Yes: add a second key, swap the secret, delete the old key |
| `UGS_PROJECT_ID`, `UGS_ENV_ID_DEVELOPMENT`, `UGS_ENV_ID_PRODUCTION` | UGS identifiers | the same workflows | None alone: identifiers, not credentials | n/a |
| `GOOGLE_PLAY_JSON_KEY` | Google Play service account JSON (Release manager) | `pr-tests` (version-code guard), `release-internal`, `prod-release` | **High**: upload and roll out builds to any Play track, change store listing | Yes: new JSON key on the same service account, swap, delete the old key |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS` | Android **upload** keystore and its passwords | `release-internal`, `prod-release` | **Medium**: sign an APK/AAB Play will accept as ours. Still needs the Play JSON or console access to publish | Not freely. Play App Signing holds the real app key, so a lost or leaked upload key is replaced through Play Console support (upload key reset) |
| `APP_STORE_CONNECT_KEY_ID`, `APP_STORE_CONNECT_ISSUER_ID`, `APP_STORE_CONNECT_API_KEY_P8` | App Store Connect API key (role **Admin**, needed for cloud signing) | `release-ios` | **High**: Admin on App Store Connect (builds, TestFlight, users, agreements) | Yes: create a new key, swap three secrets, revoke the old one. Apple lets you download a `.p8` only once |
| `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` | Unity license file and Unity ID login for game-ci activation | `release-ios` | **High**: the password is your **personal Unity ID** | Password: change in Unity ID, then update the secret. License: re-activate |
| `DISCORD_BOT_TOKEN` | Foundry Bot token | `discord-sync`, `discord-check` | **Medium**: post, edit and delete as the bot in the server (its role's permissions) | Yes: Reset Token in the Developer Portal, swap the secret |
| `DISCORD_BETA_WEBHOOK`, `DISCORD_PATCH_NOTES_WEBHOOK`, `DISCORD_STAFF_WEBHOOK` | channel webhook URLs | `discord-announce`, `discord-check`, release workflows (alerts) | **Low**: post spam into that one channel | Yes: delete the webhook, create a new one, swap |
| `DISCORD_LEAGUE_RESULTS` | webhook URL (set 2026-11-26) | **nothing yet**. Reserved for the weekly league report (#529) | Low | Yes |
| `GITHUB_TOKEN` | per-run token GitHub issues automatically | several | Scoped to the run | Automatic |

Referenced but **not set** (only in commented-out steps of `prod-release.yml`, the future fastlane iOS lane):
`MATCH_PASSWORD`, `APPLE_APP_SPECIFIC_PASSWORD`. Set them only if that lane is revived. The ASC API key replaces both.

Repository **variables** (not secret): `APPLE_TEAM_ID`, `DISCORD_GUILD_ID`, `DISCORD_*_FORUM_ID`,
`DISCORD_*_PING_ROLE_ID`.

### Secrets held outside GitHub

| Secret | Where it lives | Used for | Blast radius | Rotates |
|---|---|---|---|---|
| APNs key (`.p8`) | uploaded to UGS Push Notifications > Settings > Apple key, per environment; copy in `secrets/` | iOS push | Medium: send push to our iOS users | Yes: new key in the Apple Developer portal, re-upload in each environment, revoke the old one |
| FCM sender service account JSON (`unity-push-sender`) | uploaded to UGS Push Notifications > Google key, per environment | Android push | Medium: send push to our Android users | Yes: new JSON key, re-upload, delete old |
| Google Play service account JSON | `secrets/mobile-idle-builder-*.json` (master copy of `GOOGLE_PLAY_JSON_KEY`) | as above | see above | see above |
| Upload keystore | `secrets/mobile-idle-builder.keystore` (master copy). Builds read `secrets/user.keystore` (`BuildScript`, `sign-aab.ps1`; CI writes it from `ANDROID_KEYSTORE_BASE64` and deletes it after), so a local signed build needs a copy under that name | as above | see above | see above |
| `secrets/mobile-idle-builder.pem` | exported upload certificate (public part of the keystore) | Play upload key registration | None: a certificate, not a key | n/a |
| App Store Connect / APNs `.p8` files | `secrets/AuthKey_<KEYID>.p8` (two keys; the file name is the Key ID, match it against `APP_STORE_CONNECT_KEY_ID` to tell them apart) | as above | see above | see above |
| `secrets/Unity_v*.alf` | license *request* file | manual Unity activation | None | n/a |
| `google-services.json` | repo root, gitignored | local Android builds (Firebase/FCM config) | Low: client config, ships in the APK | n/a |
| Discord bot owner account, Google Play Console, Apple ID, Unity ID, GitHub account | password manager + 2FA | everything above | **Critical**: each one can mint all the keys above | Password change; keep 2FA on |

### Client-side IDs (in source, not secret)

LevelPlay App Keys (`LevelPlayAdProvider.cs`), Google Sign-In client IDs (`Assets/GoogleSignIn/`), Firebase identifiers
(`Assets/Resources/pushNotificationsSettings.asset`), Discord invite and feedback email (Remote Config).

## History scan

2026-11-26: `git log --all -p -G` over every commit for private-key headers, Google API keys (`AIza…`), Discord
webhook URLs, AWS keys, GitHub tokens, Slack tokens and OpenAI-style keys, plus every file ever added named
`*.keystore`, `*.jks`, `*.p8`, `*.p12`, `*.pem`, `*.env`, `google-services*`, `AuthKey*`. **Nothing found**. The only
hit was the placeholder `https://discord.com/api/webhooks/123/...` in a test.

To repeat it with a real scanner (recommended once a quarter or after any scare):

```
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:latest git /repo -v
```

GitHub secret scanning also watches every push for provider tokens.

## Rotation schedule

| Secret | How often | How |
|---|---|---|
| UGS `ci-cd-automation` key | every 90 days, and right away after any suspected leak | procedure A |
| Google Play service account key | every 180 days | procedure B |
| App Store Connect API key | every 180 days | procedure C |
| Discord bot token and webhooks | only on suspected leak, or when someone with server admin leaves | procedure D |
| APNs key, FCM sender key | only on suspected leak | re-upload in **each** UGS environment |
| Upload keystore | **never** routinely. Only on loss or leak, through Play Console support | procedure E |
| Unity ID password | on suspected leak. Prefer replacing it with a dedicated CI Unity account | change, then `gh secret set UNITY_PASSWORD` |

Log each rotation in the table at the bottom so the next one is easy to date.

### A. UGS service account key (`ci-cd-automation`)

1. Unity Cloud Dashboard > Administration > Service accounts > `ci-cd-automation` > Keys > **Add key**. Copy the
   Key ID and Secret key. The secret is shown once.
2. Update GitHub:
   ```
   gh secret set UGS_GAME_CONFIG_SERVICE_KEY_ID      # paste the Key ID
   gh secret set UGS_GAME_CONFIG_SERVICE_SECRET_KEY  # paste the secret
   ```
3. Check it works: Actions > **Publish Game Data** > Run workflow > environment `development`, or re-run the latest
   release-internal `cloud-code` job. It must authenticate.
4. Delete the **old** key in the dashboard. Update the password manager entry.

### B. Google Play service account key

1. Google Cloud Console > IAM > Service accounts > the Play account > Keys > Add key > JSON.
2. `gh secret set GOOGLE_PLAY_JSON_KEY < path/to/new.json`. Replace the copy in `secrets/`.
3. Check it: open any PR into an `integration/**` branch. The PR Tests version-code guard reads Play with this key.
4. Delete the old key in Google Cloud.

### C. App Store Connect API key

1. App Store Connect > Users and Access > Integrations > App Store Connect API > **Generate API Key**, role
   **Admin** (cloud signing fails with App Manager). Download the `.p8` (once only) into `secrets/`.
2. ```
   gh secret set APP_STORE_CONNECT_KEY_ID            # the new Key ID
   base64 -w0 secrets/AuthKey_<NEWID>.p8 | gh secret set APP_STORE_CONNECT_API_KEY_P8
   ```
   The Issuer ID does not change.
3. Check it: run **Release iOS** by hand (workflow_dispatch) and confirm the TestFlight upload.
4. Revoke the old key. Delete its `.p8`.

### D. Discord

- Bot token: Developer Portal > the application > Bot > **Reset Token** > `gh secret set DISCORD_BOT_TOKEN`. The
  old token stops working at once, so set the secret right away. Check: run **Discord Forum Sync** by hand.
- Webhook: Server Settings > Integrations > Webhooks > delete the old one, create a new one in the same channel >
  `gh secret set DISCORD_<NAME>_WEBHOOK`. Check: run **Discord Announce** with a test version, or wait for the next alert.

### E. Upload keystore (loss or leak only)

1. Generate a new upload key: `keytool -genkeypair -v -keystore new-upload.keystore -alias mobile-idle-builder -keyalg RSA -keysize 2048 -validity 10000`,
   then export its certificate: `keytool -export -rfc -keystore new-upload.keystore -alias mobile-idle-builder -file upload_certificate.pem`.
2. Play Console > the app > Setup > App signing > **Request upload key reset**, attach the `.pem`. Google takes a
   few days.
3. Once approved, update all four `ANDROID_*` secrets (`base64 -w0 new-upload.keystore | gh secret set ANDROID_KEYSTORE_BASE64`)
   and replace the files in `secrets/` (both `mobile-idle-builder.keystore` and any local `user.keystore`).

## Moving production secrets behind a protected environment

Today every secret is repository-wide, so any workflow on any branch can read the production UGS environment ID and
the Play key. The target:

| GitHub environment | Holds | Protection |
|---|---|---|
| `production` | `UGS_ENV_ID_PRODUCTION`; later a production-only UGS service account key and the Play key used by `prod-release` | required reviewer (you), deployment branches: `main` only |
| `development` | everything the internal and iOS builds use | none, or branches `release/**` + `develop` |

Steps (manual, because GitHub never shows a secret's value again, so it can't be copied over):

1. Settings > Environments > New environment `production` > Required reviewers: yourself > Deployment branches:
   Selected > `main`.
2. Set the secret on the environment from your password manager, then delete the repository copy:
   ```
   gh secret set UGS_ENV_ID_PRODUCTION --env production
   gh secret delete UGS_ENV_ID_PRODUCTION
   ```
3. Add `environment: production` to the jobs that read it (`publish-gamedata.yml` when publishing to production,
   `prod-release.yml`). Ask Claude to make that workflow change in the same PR as step 2, or the jobs lose the secret.
4. Stronger, later: create a second UGS service account with roles **only** in the production environment, keep its key
   only in `production`, and remove production roles from `ci-cd-automation`. A leaked CI key then can't touch live players.

## To do (#534 follow-ups)

- [ ] Turn on **non-provider patterns** and **validity checks**: Settings > Code security > Secret scanning.
- [ ] Create the `production` environment and move `UGS_ENV_ID_PRODUCTION` (section above).
- [ ] Split the UGS service account into dev and prod accounts.
- [ ] Automate procedure A as a scheduled workflow (create a key through the UGS Admin API, `gh secret set` with a
      fine-grained PAT that has `secrets: write`, revoke the old key). It needs a PAT stored as a secret itself, so
      decide whether a 90-day manual rotation with a calendar reminder is enough first.
- [ ] Give game-ci its own Unity account instead of the personal Unity ID.

## Rotation log

| Date | Secret | By | Notes |
|---|---|---|---|
| 2026-11-16 | UGS service account key, project/env IDs | owner | set (initial) |
| 2026-11-26 | Discord bot token + webhooks | owner | set (initial, #533) |
