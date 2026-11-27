# Secrets: inventory, storage and rotation

Every key the game, CI and the live services depend on: where it lives, what reads it, what a leak would cost, and
how to rotate it. Issue #534.

> Inventory taken 2026-11-26 from `.github/workflows/*.yml`, `gh secret list`, the local `secrets/` folder and the
> runbooks. Re-check it whenever a workflow gains a `secrets.*` reference or a new service is added.

## Rules

- No secret is ever committed. `/secrets/`, `*.keystore` and `google-services.json` are gitignored. The history scan
  below found one client key (the Firebase API key, which ships in the app anyway) and no credentials.
- CI secrets live in GitHub Actions secrets only. GitHub cannot show a secret's value again, so the
  **master copy of every value lives in a password manager** (and the local `secrets/` folder for files). If the
  password manager doesn't have it, rotate it rather than trying to recover it.
- Client-side IDs that ship inside the app (LevelPlay App Keys, Google OAuth client IDs, Firebase identifiers,
  UGS project ID) are **not secrets**. Anyone can pull them out of the APK. They stay in source.
- Repo protections, checked 2026-11-26: secret scanning **on**, push protection **on**, Dependabot security updates
  **on**. Non-provider patterns and validity checks are **off**: enabling them through the API on 2026-11-26 was
  accepted but they stayed disabled, most likely because they need GitHub Secret Protection (paid) on a personal
  account. Check Settings > Advanced Security if the plan changes.

## Inventory

Blast radius is what someone holding the value could do. "Rotates" says whether a new key can replace the old one
without touching shipped builds.

### GitHub Actions repository secrets

| Secret | What it is | Read by | Blast radius if leaked | Rotates |
|---|---|---|---|---|
| `UGS_PROJECT_SERVICE_KEY_ID` / `UGS_PROJECT_SERVICE_KEY_SECRET` | key of `ci-project` (project role **Remote Config Admin**) | `release-internal` (upload job), `release-ios` (release job), `publish-gamedata` | **High**: edit Remote Config in **every** environment, production included (maintenance mode, kill-switches, game data overrides). Remote Config has no environment roles, so this can't be narrowed. Only GitHub-hosted jobs get it | Yes: procedure A |
| `UGS_DEV_SERVICE_KEY_ID` / `UGS_DEV_SERVICE_KEY_SECRET` | key of `ci-development` (environment role **Cloud Code Editor** on development; project role **Unity Environment Viewer**, read-only, because `ugs deploy` lists environments) | `release-internal` (cloud-code job) | **Low**: change the development PvE module (internal testers). Refused in production | Yes: procedure A |
| `UGS_SYMBOLS_SERVICE_KEY_ID` / `UGS_SYMBOLS_SERVICE_KEY_SECRET` | key of `ci-symbols` (project role **Observability Symbols Uploader** only) | `release-internal`, `prod-release` (build jobs, **self-hosted runner**) | **Low**: upload junk debug symbols | Yes: procedure A |
| `UGS_PROJECT_ID`, `UGS_ENV_ID_DEVELOPMENT`, `UGS_ENV_ID_PRODUCTION` | UGS identifiers (`UGS_ENV_ID_PRODUCTION` belongs in the `production` environment, below) | the same workflows | None alone: identifiers, not credentials | n/a |
| `GOOGLE_PLAY_JSON_KEY` | Google Play service account JSON (Release manager) | `pr-tests` (version-code guard), `release-internal`, `prod-release` | **High**: upload and roll out builds to any Play track, change store listing | Yes: new JSON key on the same service account, swap, delete the old key |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASS`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS` | Android **upload** keystore and its passwords | `release-internal`, `prod-release` | **Medium**: sign an APK/AAB Play will accept as ours. Still needs the Play JSON or console access to publish | Not freely. Play App Signing holds the real app key, so a lost or leaked upload key is replaced through Play Console support (upload key reset) |
| `APP_STORE_CONNECT_KEY_ID`, `APP_STORE_CONNECT_ISSUER_ID`, `APP_STORE_CONNECT_API_KEY_P8` | App Store Connect API key (role **Admin**, needed for cloud signing) | `release-ios` | **High**: Admin on App Store Connect (builds, TestFlight, users, agreements) | Yes: create a new key, swap three secrets, revoke the old one. Apple lets you download a `.p8` only once |
| `IOS_SIGNING_CERT_P12_BASE64`, `IOS_SIGNING_CERT_PASSWORD` | the fixed **Apple Development** certificate + private key (`.p12`, base64) the iOS archive step signs with | `release-ios` (macOS job; imported into a temporary keychain, deleted at job end) | **Low-Medium**: sign development builds for our team's registered devices. Not App Store or TestFlight signing (export uses cloud-managed distribution) | Yes: procedure F, yearly (the certificate expires after 1 year) |
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

**The Firebase API key** (`firebaseWebApiKey` in `pushNotificationsSettings.asset`, also in `google-services.json`) is
the one GitHub secret scanning flags (alert #1, "Google API Key", publicly leaked). It identifies the Firebase project
and ships inside every APK, so rotating it changes nothing. What protects it is **restriction** in Google Cloud
Console > APIs & Services > Credentials > the key:
- Application restrictions: **Android apps**, package `com.clemtek.mobileidlebuilder`, with the SHA-1 of the **Play
  app signing** certificate (Play Console > Setup > App signing) and of the **upload** certificate (local and
  internal builds). Add an iOS restriction (bundle id) when iOS push ships.
- API restrictions: only **Firebase Installations API** and **FCM Registration API** (add others only if a Firebase
  feature fails without them).

Without restrictions anyone can call any API enabled on the project with it, billed to the project. After
restricting it, close the alert as "Used in tests / won't fix" with the reason "client key, restricted".

## History scan

2026-11-26: `git log --all -p -G` over every commit for private-key headers, Google API keys (`AIza…`), Discord
webhook URLs, AWS keys, GitHub tokens, Slack tokens and OpenAI-style keys, plus every file ever added named
`*.keystore`, `*.jks`, `*.p8`, `*.p12`, `*.pem`, `*.env`, `google-services*`, `AuthKey*`, over **every** file type
(a first pass that skipped `*.asset` missed the Firebase key). Found:
- the Firebase API key in `Assets/Resources/pushNotificationsSettings.asset` (a client key, see above; restrict it)
- the placeholder `https://discord.com/api/webhooks/123/...` in a test
- no private keys, service account JSON, tokens or passwords

To repeat it with a real scanner (recommended once a quarter or after any scare):

```
docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:latest git /repo -v
```

GitHub secret scanning also watches every push for provider tokens.

## CI audit (2026-11-26)

The repo is **public** with a **self-hosted** runner (`server-windows`), so the questions are who can run code on that
machine and who can read secrets.

| Check | Result |
|---|---|
| Fork PRs | need approval for all outside contributors (Actions setting). Fork PRs get no secrets |
| Self-hosted runner | only same-repo PRs reach it (`pr-tests`, `release-internal` check `head.repo.full_name`); non-owner PRs also wait on the `self-hosted-approval` environment (required reviewer) |
| `pull_request_target` / `workflow_run` | not used |
| Default `GITHUB_TOKEN` | read-only; every workflow sets `permissions:` explicitly |
| Third-party actions | all pinned to commit SHAs; Dependabot updates the pins monthly |
| Script injection | no untrusted event text (issue/PR titles, bodies, branch names) in `run:` steps; `workflow_dispatch` inputs are typed or passed through `env:` |
| Keystore on the runner | written to `secrets\user.keystore` for the build, removed in an `if: always()` step |
| Build log artifact | scanned: no passwords or tokens. It does show the Windows user name and runner paths (low risk) |
| Collaborators | owner only |
| Branch protection | `main` and `develop`: PR + 1 review, no force push, no deletion (admin can bypass, needed as a solo owner) |
| Open alerts | secret scanning: 1 (Firebase key, above). Dependabot: 0. Code scanning: 0 |

**Public artifacts.** On a public repo any signed-in GitHub user can download workflow artifacts, including the
internal-build AAB and its symbols. The source is public, so this exposes unreleased builds rather than secrets.
Since 2026-11-26 the internal AAB is kept **3 days** (was 30): long enough to re-run a failed upload, and Play Console
keeps every uploaded bundle anyway. Symbols stay 90 days because crash reports need them; they hold no secrets.
`prod-release` and `release-ios` artifacts are unchanged (production builds are public once released).

**Where the real risk sits.** Every secret is repository-wide, and only the owner can push, so the attack paths are
the owner's accounts (GitHub, Unity, Google, Apple, Discord) and a compromised dependency. Strong passwords and 2FA
everywhere matter more than anything in this file. The UGS keys are split per job (2026-11-26): the self-hosted runner
only ever holds the symbols key, and the Cloud Code key can't reach production.

## Rotation schedule

| Secret | How often | How |
|---|---|---|
| UGS `ci-project` key (Remote Config) | every 90 days, and right away after any suspected leak | procedure A |
| UGS `ci-development`, `ci-symbols` keys | every 180 days | procedure A |
| Google Play service account key | every 180 days | procedure B |
| App Store Connect API key | every 180 days | procedure C |
| Discord bot token and webhooks | only on suspected leak, or when someone with server admin leaves | procedure D |
| APNs key, FCM sender key | only on suspected leak | re-upload in **each** UGS environment |
| iOS CI signing certificate | yearly, before it expires (the date is in the portal) | procedure F |
| Upload keystore | **never** routinely. Only on loss or leak, through Play Console support | procedure E |
| Unity ID password | on suspected leak. Prefer replacing it with a dedicated CI Unity account | change, then `gh secret set UNITY_PASSWORD` |

Log each rotation in the table at the bottom so the next one is easy to date.

### A. UGS service account keys (`ci-project`, `ci-development`, `ci-symbols`)

The three accounts and their roles (Unity Cloud Dashboard > Administration > Service accounts). Never add a role
to make a job pass without writing it here first.

| Account | Project roles | Environment roles | GitHub secrets |
|---|---|---|---|
| `ci-project` | Remote Config Admin | none (Remote Config has none) | `UGS_PROJECT_SERVICE_KEY_ID` / `_SECRET` |
| `ci-development` | Unity Environment Viewer | development: Cloud Code Editor | `UGS_DEV_SERVICE_KEY_ID` / `_SECRET` |
| `ci-symbols` | LiveOps > Observability Symbols Uploader | none | `UGS_SYMBOLS_SERVICE_KEY_ID` / `_SECRET` |

None of them has Unity Environments Admin, Authentication, Cloud Save, Economy, Leaderboards or any organization role.
A `ci-production` account (production environment roles only) gets created when CI first needs production Cloud
Code, Cloud Save or Leaderboards (for example the #529 league report); its key lives only in the `production`
GitHub environment as `UGS_PROD_SERVICE_KEY_ID` / `_SECRET`.

To rotate one:
1. The account > Keys > **Add key**. Copy the Key ID and Secret key. The secret is shown once.
2. Update GitHub, for example for `ci-project`:
   ```
   gh secret set UGS_PROJECT_SERVICE_KEY_ID      # paste the Key ID
   gh secret set UGS_PROJECT_SERVICE_KEY_SECRET  # paste the secret
   ```
3. Check it: Actions > **UGS Key Check** > Run workflow. Every line must be PASS: each key's allowed calls return
   200 and its refused calls 403 (a 403 where 200 was expected means a missing role; a 200 where 403 was
   expected means the account has too much).
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

### F. iOS CI signing certificate

Why it exists: on a fresh macOS runner, `xcodebuild archive -allowProvisioningUpdates` creates a new
"Apple Development: Created via API" certificate on every run when it has no signing identity. Those pile up until
Apple refuses new ones ("Your account has reached the maximum number of certificates"), which broke Release iOS on
2026-11-26. `scripts/archive-upload-ios.sh` now imports this fixed certificate first, so Xcode reuses it.

No Mac needed:
1. Key and request (keep both in `secrets/ios-ci/`, gitignored; the key never leaves your machine):
   ```
   MSYS_NO_PATHCONV=1 openssl req -new -newkey rsa:2048 -nodes -keyout secrets/ios-ci/ios-ci-signing.key      -out secrets/ios-ci/ios-ci-signing.csr -subj "/CN=Quantum Foundry CI/C=US"
   ```
2. developer.apple.com > Certificates > **+** > **Apple Development** > upload the `.csr` > download the `.cer` as
   `secrets/ios-ci/ios-ci-signing.cer`.
3. Package and store it (a new random password each time; keep it in the password manager):
   ```
   openssl x509 -inform DER -in secrets/ios-ci/ios-ci-signing.cer -out secrets/ios-ci/ios-ci-signing.pem
   openssl pkcs12 -export -legacy -inkey secrets/ios-ci/ios-ci-signing.key -in secrets/ios-ci/ios-ci-signing.pem      -out secrets/ios-ci/ios-ci-signing.p12 -passout pass:<password>
   base64 -w0 secrets/ios-ci/ios-ci-signing.p12 | gh secret set IOS_SIGNING_CERT_P12_BASE64
   gh secret set IOS_SIGNING_CERT_PASSWORD   # paste <password>
   ```
   `-legacy` matters: macOS `security import` can't read OpenSSL 3's default p12 encryption.
4. Check it: re-run **Release iOS**. The archive step logs `CI signing keychain: 1 valid identities found`, and
   the portal shows no new "Created via API" certificate.
5. Revoke the old CI certificate and any "Created via API" ones. Development certificates don't affect
   TestFlight or App Store builds.

## The `production` GitHub environment

Holds `UGS_ENV_ID_PRODUCTION` (and later `UGS_PROD_SERVICE_KEY_*`). Only a job that declares `environment: production`
can read it, and that job waits for your approval. Today that is the production publish in `publish-gamedata.yml`
(a development publish declares no environment and runs straight away).

It protects the *production publish*, not the Remote Config key: `ci-project` can write production from any job
that holds it, because UGS has no environment roles for Remote Config. That's why its key is only given to
GitHub-hosted jobs and rotates every 90 days.

Set it up (manual, because GitHub never shows a secret's value again):
1. Settings > Environments > `production` (the first production publish creates it if it doesn't exist) >
   Required reviewers: yourself > Deployment branches: Selected > `main` (add `develop` if you publish from there).
2. Move the ID from your password manager:
   ```
   gh secret set UGS_ENV_ID_PRODUCTION --env production
   gh secret delete UGS_ENV_ID_PRODUCTION
   ```
3. Check it: Actions > **Publish Game Data** > environment `production`, confirm `production`, `dry_run` on. The run
   must wait for your approval, then show the payload without writing. UGS Key Check then skips its production
   checks (it has no access to the ID any more), which is expected.

## To do (#534 follow-ups)

- [x] Restrict the Firebase API key (Android apps + API restrictions, above); alert #1 closed as won't fix (2026-11-26).
- [x] Confirm 2FA on GitHub, Unity ID, the Google account behind Play Console and Google Cloud, the Apple ID, and Discord (2026-11-26).
- [x] Internal AAB artifact retention cut to 3 days (2026-11-26).
- [ ] Non-provider patterns and validity checks: not available on this plan (tried 2026-11-26).
- [ ] Protect the `production` environment and move `UGS_ENV_ID_PRODUCTION` into it (section above).
- [x] Split the UGS service account per job: `ci-project`, `ci-development`, `ci-symbols` (2026-11-26, procedure A).
- [ ] Automate procedure A as a scheduled workflow (create a key through the UGS Admin API, `gh secret set` with a
      fine-grained PAT that has `secrets: write`, revoke the old key). It needs a PAT stored as a secret itself, so
      decide whether a 90-day manual rotation with a calendar reminder is enough first.
- [ ] Give game-ci its own Unity account instead of the personal Unity ID.

## Rotation log

| Date | Secret | By | Notes |
|---|---|---|---|
| 2026-11-16 | UGS service account key, project/env IDs | owner | set (initial) |
| 2026-11-26 | Discord bot token + webhooks | owner | set (initial, #533) |
| 2026-11-26 | UGS: `ci-project`, `ci-development`, `ci-symbols` keys | owner | created; `ci-cd-automation` and `UGS_GAME_CONFIG_*` retired |
