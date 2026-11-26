# Discord Server

The structure of the Quantum Foundry Discord: security, roles, channels, onboarding, templates, automation,
incentives and moderation. Issue #533. It is the community hub the beta plan relies on (`docs/testing/beta-plan.md`):
tester recruiting, the beta feedback channel, patch notes and weekly league results.

**Invite (permanent):** https://discord.gg/aVt8Z9ntkC

Keep this file in step with the real server. Change the server, then update this file (or the other way round).

## How it fits together

| Piece | What it does | Where it lives |
|---|---|---|
| **AutoMod** (built in) | blocks slurs, spam, scam links and mention spam; alerts in `#mod-log` | Discord |
| **Onboarding + rules screening** (built in) | new members accept the rules, pick platform and ping roles | Discord |
| **Foundry Bot** (our own app, no hosting) | enforces the bug-report template, opens GitHub issues from forum posts, tags posts Fixed / Done when the issue closes | `scripts/discord/forum_sync.py`, run every 20 minutes by `.github/workflows/discord-sync.yml` |
| **Webhooks** | beta build announcements, patch notes, CI failure alerts | `scripts/discord/notify.py`, called by the release workflows |

No third-party bot. Everything that writes to the server is either Discord itself or code in this repo, with
its credentials in GitHub secrets.

## Security

### Server settings

Turn on **Community** (Server Settings > Enable Community). That unlocks the Rules screen, Onboarding,
forum channels, announcement channels and Server Insights.

| Setting (Server Settings > ...) | Value | Why |
|---|---|---|
| Safety Setup > Verification level | **Medium** (registered on Discord for 5+ minutes) | stops most throwaway spam accounts |
| Safety Setup > Members must accept rules before they can talk or DM | **On** | the posting gate: nobody can post until they've accepted the rules |
| Safety Setup > Raid protection: Activity alerts | **On**, sent to `#mod-log` | warns on join or DM spam waves |
| Safety Setup > CAPTCHA suspicious accounts / CAPTCHA all accounts during a raid | **On** / **On** | bots fail the CAPTCHA |
| Safety Setup > Hide DMs from suspicious users / Filter DMs from unknown users | **On** / **On** | protects members from DM scams |
| Safety Setup > Sensitive content filter | Filter for **all members** | a game for all ages |
| Safety Setup > Require 2FA for moderator actions | **On** | a stolen moderator account can't wipe channels |
| Safety Setup > Permissions: "Remove risky permissions from @everyone" | do it (check the matrix below afterwards) | removes Mention @everyone, Manage... from @everyone |
| Overview > Default notifications | **Only @mentions** | new members aren't flooded |
| Overview > System messages channel | `#mod-log`, join messages **off** | no "X joined" spam in public channels |
| Invites | only the one permanent invite; **Create Invite** off for everyone but Developer and Moderator | the invite in the game and store listing is the only way in, and it can be paused (Safety Setup > Pause invites) during a raid |

### Accounts and credentials

- **Owner account:** 2FA on (authenticator app, not SMS), backup codes stored offline. Losing the owner account
  loses the server.
- **No Administrator except the owner.** No bot ever gets Administrator.
- **Ban Members** stays with the Developer, so a compromised or rogue Moderator can't mass-ban. Moderators use
  timeouts and kicks.
- **Webhook URLs and the bot token are passwords.** Anyone holding one can post as it. They live only in GitHub
  secrets, never in the repo, in chat or in a screenshot. If one leaks: delete the webhook (or reset the bot token
  in the Developer Portal), make a new one and update the secret. Nothing else needs to change.
- **Least privilege for the bot:** it has no server-wide permissions at all. It gets channel overrides on the three
  forums only (see Channel overrides), so a leaked token can at worst lock, retag or post in feedback threads.
- **Untrusted input:** Discord post text is copied into public GitHub issues by the bot. The script defuses
  `@mentions`, `#123` references and HTML comments, caps the length, and sends it only as JSON to the GitHub API
  (never through a shell). Issues carry the `from-discord` label: treat their text as untrusted, including when an
  AI agent reads them. Beta posts are **not** copied (the beta is private and the repo is public); their issue has
  the version, the device and a link.
- **No personal data in issues:** issues never name the Discord user. Forum guidelines tell players not to post
  emails, player IDs or purchase receipts; purchase and account problems go to the Developer by DM.
- **Mentions are locked down** on every automated message (`allowed_mentions`): announcements can only ping their
  own opt-in role, and nothing automated can ping @everyone, even if the notes text contains it.

## Roles

Order matters: a role can only manage roles below it. Top to bottom:

| Role | Who | Permissions beyond @everyone |
|---|---|---|
| **Developer** | the owner | Administrator |
| **Moderator** | trusted volunteers (start with none or one) | Manage Messages, Timeout Members, Kick Members, Manage Threads, View Audit Log (full list below) |
| **Foundry Bot** | our bot (Discord creates this role when the bot joins) | none server-wide; channel overrides on the forums only |
| **Beta Tester** | testers in an active beta phase | sees the **BETA** category |
| **League Regular** | optional cosmetic: players who post league results often | none (a colour and recognition) |
| **Android** / **iPhone** | picked in Onboarding | none (shown on bug reports, helps triage) |
| **Patch Notes Ping** / **League Ping** / **Beta News Ping** | picked in Onboarding | none; announcements ping these instead of @everyone |
| **@everyone** | every member | read and post in public channels, once they've accepted the rules |

There is no "Player" role. The posting gate is rules screening ("Members must accept rules before they can talk"),
which Discord enforces for @everyone. That keeps Onboarding valid: Discord requires several default channels that
@everyone can post in.

### Role settings (Server Settings > Roles > the role > Display)

| Role | Colour | Display separately | Anyone can @mention | How it's given |
|---|---|---|---|---|
| Developer | teal `#22D3EE` (the game's accent) | yes | no | by hand (owner) |
| Moderator | green `#34D399` | yes | yes (members can call a mod) | by hand |
| Foundry Bot | grey `#9CA3AF` | yes | no | automatically when the bot joins |
| Beta Tester | amber `#FBBF24` | yes | no | by hand, after adding the tester to Play / TestFlight |
| League Regular | purple `#A78BFA` | no | no | by hand, weekly |
| Android / iPhone | none | no | no | Onboarding |
| Patch Notes / League / Beta News Ping | none | no | no (only the Developer and the webhooks ping them) | Onboarding |
| @everyone | n/a | n/a | n/a | everyone |

### Server-level permissions (Server Settings > Roles > the role > Permissions)

✅ = on. Blank = off. Developer has **Administrator**, which includes everything, so it is not listed.
Beta Tester, League Regular, platform and ping roles have **no** server-level permissions: Beta Tester access
comes from the channel overrides below. Foundry Bot has none either.

| Permission (Discord's name) | @everyone | Moderator |
|---|---|---|
| **General** | | |
| View Channels | ✅ | ✅ |
| Manage Channels / Roles / Expressions / Webhooks / Server | | |
| Create Expressions | | |
| View Audit Log | | ✅ |
| View Server Insights | | |
| **Membership** | | |
| Create Invite | | ✅ |
| Change Nickname | ✅ | ✅ |
| Manage Nicknames | | ✅ |
| Kick, Approve, and Reject Members | | ✅ |
| Ban Members | | |
| Timeout Members | | ✅ |
| **Text channels** | | |
| Send Messages and Create Posts | ✅ | ✅ |
| Send Messages in Threads and Posts | ✅ | ✅ |
| Create Public Threads | ✅ | ✅ |
| Create Private Threads | | ✅ |
| Embed Links | ✅ | ✅ |
| Attach Files | ✅ | ✅ |
| Add Reactions | ✅ | ✅ |
| Use External Emoji | ✅ | ✅ |
| Use External Stickers | | ✅ |
| Mention @everyone, @here, and All Roles | | |
| Manage Messages | | ✅ |
| Pin Messages | | ✅ |
| Bypass Slowmode | | ✅ |
| Manage Threads and Posts | | ✅ |
| Read Message History | ✅ | ✅ |
| Send Text-to-Speech Messages | | |
| Send Voice Messages | ✅ | ✅ |
| Create Polls | ✅ | ✅ |
| **Voice** | | |
| Connect / Speak / Video / Use Voice Activity | ✅ | ✅ |
| Use Soundboard / Use External Sounds | | |
| Priority Speaker | | ✅ |
| Mute / Deafen / Move Members, Set Voice Channel Status | | ✅ |
| **Apps** | | |
| Use Application Commands | ✅ | ✅ |
| Use Activities / Use External Apps | | |
| **Events** | | |
| Create Events / Manage Events | | ✅ |
| **Advanced** | | |
| Administrator | | |

Why these choices:
- Nobody but the Developer can @everyone; announcements ping the opt-in ping roles instead.
- Soundboard, text-to-speech, external apps and external stickers are off: noise and spam vectors with little upside.
- Create Invite is off for @everyone so the one permanent invite stays the only door (and can be paused).

### Channel overrides (channel > Edit Channel > Permissions)

Set these on the **category** and let channels sync, except where a single channel differs.

| Category / channel | Role | Override |
|---|---|---|
| START HERE: `#welcome`, `#announcements` | @everyone | Send Messages ❌, Create Public Threads ❌, Add Reactions ✅ |
| `#rules` | @everyone | Send Messages ❌ |
| GAME: `#patch-notes`, `#league-results` | @everyone | Send Messages ❌, Create Public Threads ✅, Send Messages in Threads ✅ (discuss in threads) |
| GAME: `#general`, `#screenshots`, `#strategy` | (none) | inherit |
| FEEDBACK category | Foundry Bot | View Channel ✅, Read Message History ✅, Send Messages in Threads ✅, Manage Threads ✅, Embed Links ✅ |
| BETA category (private) | @everyone | View Channel ❌ |
| | Beta Tester | View Channel ✅ |
| | Moderator | View Channel ✅ |
| `#beta-announcements` | Beta Tester | Send Messages ❌, Add Reactions ✅ |
| `#beta-feedback` | Foundry Bot | View Channel ✅, Read Message History ✅, Send Messages in Threads ✅, Manage Threads ✅, Embed Links ✅ |
| VOICE category | (none) | inherit |
| STAFF category (private) | @everyone | View Channel ❌ |
| | Moderator | View Channel ✅ (not `#ci-alerts`) |

The Developer sees and does everything through Administrator, so it never needs an override. Webhooks don't
need a role: each is created on its channel by the Developer and can only post there.

## Channels

**Private** means: turn on "Private Category" / "Private Channel" when you create it and add only the roles
listed. **Public** means: leave it off. Channels inside a category inherit its privacy; keep them synced.

| Category | Privacy | Who can see it |
|---|---|---|
| **START HERE** | Public | everyone |
| **GAME** | Public | everyone |
| **FEEDBACK** | Public | everyone (+ Foundry Bot override) |
| **BETA** | **Private** | Beta Tester, Moderator (+ Foundry Bot on `#beta-feedback`) |
| **VOICE** | Public | everyone |
| **STAFF** | **Private** | Moderator (`#ci-alerts`: Developer only) |

"Posts only" below means members can't chat in the channel itself: forum channels only accept posts (each post is
its own thread), and read-only channels only allow reactions or threads.

| Category | Channel | Privacy | Type | Who can post | Purpose |
|---|---|---|---|---|---|
| **START HERE** | `#welcome` | Public | text, read-only | Developer | the welcome message below |
| | `#rules` | Public | text, read-only | Developer | the rules below; pick it as the Rules channel when enabling Community |
| | `#announcements` | Public | announcement, read-only | Developer | events, beta phases, big news; other servers can follow it |
| **GAME** | `#patch-notes` | Public | announcement, read-only (threads allowed) | Developer, webhook | release notes; posted with the **Discord Announce** workflow when a production release rolls out |
| | `#general` | Public | text, slowmode 5 s | everyone | general chat |
| | `#strategy` | Public | forum (posts only), tags: Physics, Chemistry, Biology, Metallurgy, Engineering, Prestige, Leagues | everyone | builds, layouts, prestige timing |
| | `#screenshots` | Public | media channel (posts only) | everyone | factory screenshots and clips; good source of marketing material (ask first) |
| | `#league-results` | Public | text, read-only (threads allowed) | webhook, Developer | weekly PvE league results (#529); players react and discuss in threads |
| **FEEDBACK** | `#bug-reports` | Public | forum (posts only), **template enforced by Foundry Bot** | everyone | one post per bug; becomes a GitHub issue |
| | `#suggestions` | Public | forum (posts only), template in guidelines | everyone | one post per idea; 👍 to vote; "Planned" ones become GitHub issues |
| **BETA** | `#beta-announcements` | **Private** | text, read-only | Developer, webhook | each new beta build (automatic from CI), survey links |
| | `#beta-feedback` | **Private** | forum (posts only), **template enforced by Foundry Bot** | Beta Tester | beta bugs and feedback; becomes a GitHub issue labelled `beta` |
| **VOICE** | `Factory Floor` | Public | voice | everyone | hang out; optional, add later |
| **STAFF** | `#mod-log` | **Private** | text | Discord (AutoMod, raid alerts, system messages) | moderation alerts |
| | `#mod-chat` | **Private** | text | Moderator, Developer | moderation discussion |
| | `#ci-alerts` | **Private** (Developer only) | text | webhook | release workflow failures |
| | `#discord-updates` | **Private** | text | Discord | pick it as the "Community updates" channel when enabling Community |

Start small. Add `VOICE` and `League Regular` once there are enough people to use them.

### Forum settings

Forum channel > Edit Channel. Status tags are **moderator-only** (tick "Only allow moderators to apply this tag"):
players can't apply them, and the bot (Manage Threads) can. The bot looks tags up by name, so spell them exactly.

| Setting | `#bug-reports` | `#beta-feedback` | `#suggestions` | `#strategy` |
|---|---|---|---|---|
| Topic tags (players pick) | Crash, Save/Cloud, UI, Balance, Purchases/Ads, Tournaments, Android, iPhone | Crash, UI, Balance, Tutorial, Performance, Other | Gameplay, UI, Content, Tournaments, Quality of life | Physics, Chemistry, Biology, Metallurgy, Engineering, Prestige, Leagues |
| Status tags (moderator-only) | `Tracked`, `Needs info`, `Fixed`, `Won't fix` | `Tracked`, `Needs info`, `Fixed`, `Won't fix` | `Planned`, `Done`, `Not planned` | none |
| Require a tag when posting | on | on | on | on |
| Post guidelines | see Templates | see Templates | see Templates | see Templates |
| Default reaction | none | none | 👍 (the vote) | none |
| Slowmode (new posts per member) | 10 minutes | 2 minutes | 30 minutes | 10 minutes |
| Hide after inactivity | 1 week | 1 week | 1 month | 1 week |
| Default sort / layout | Latest activity / List | Latest activity / List | Latest activity / List | Latest activity / Gallery |

## Onboarding

Server Settings > Onboarding. Turn on rules screening first (Security above).

**Default channels** (everyone gets these): `#welcome`, `#rules`, `#announcements`, `#patch-notes`, `#general`,
`#strategy`, `#screenshots`, `#bug-reports`, `#suggestions`. Five of them (`#general`, `#strategy`, `#screenshots`,
`#bug-reports`, `#suggestions`) are ones @everyone can post in, which meets Discord's onboarding requirement.

**Questions:**

| # | Question | Shown | Type | Answers -> roles / channels |
|---|---|---|---|---|
| 1 | Where do you play Quantum Foundry? | before joining, **required** | single choice | 🤖 Android -> **Android** · 🍎 iPhone or iPad -> **iPhone** · 👀 Not playing yet -> (none) |
| 2 | Which announcements should ping you? | before joining, optional | multiple choice | 📝 New updates and patch notes -> **Patch Notes Ping** · 🏆 Weekly tournament results -> **League Ping** + `#league-results` · 🧪 Beta builds and tester calls -> **Beta News Ping** |
| 3 | What are you here for? | after joining, optional | multiple choice | 🏭 Sharing factories -> `#screenshots` · 🧠 Strategy and optimising -> `#strategy` · 🐞 Reporting bugs and ideas -> `#bug-reports`, `#suggestions` · 🧪 I want to beta test -> **Beta News Ping** + `#announcements` |

Beta Tester itself is never self-assigned: the Developer gives it after adding the person to the Play internal
testing list or TestFlight (beta plan, "Recruiting").

**Server Guide** (Onboarding > Server Guide):
- Welcome sign: "Welcome to the Quantum Foundry community! Build, prestige, compare factories."
- To-do list: 1) Read the rules (`#rules`). 2) Say hi in `#general`. 3) Share your factory in `#screenshots`.
  4) Found a bug? Use the template in `#bug-reports`.
- Resource pages: `#welcome`, `#patch-notes`.

## Templates

### `#welcome` (post as the Developer, then lock the channel)

```
**Welcome to Quantum Foundry!** ⚛️
An idle factory game where you build matter from quarks up: atoms, molecules, life.

**Get the game**
Google Play: <link from docs/store/store-listing.md>
App Store: <link>

**Find your way**
📝 #patch-notes  what changed in each update
💬 #general  chat
🧠 #strategy  builds, layouts, prestige timing
🏭 #screenshots  show off your factory
🐞 #bug-reports  one post per bug, use the template
💡 #suggestions  one post per idea, 👍 the ones you want

Change your roles and pings any time: **Channels & Roles** at the top of the channel list.
Problems with a purchase or your account? DM the Developer. Never share your password or payment details.
```

### `#bug-reports` post guidelines

```
One post per bug. Search first: if someone already posted it, add a 👍 and your details there.

Fill in this template in your first message. Posts without it are flagged by Foundry Bot and closed after
3 days; edit your first message to fix it.

**Version:** (bottom of Settings in the game, e.g. 0.6.133)
**Device:** (phone model and Android / iOS version)
**What happened:**
**Steps to reproduce:**
**Expected:**

Screenshots and screen recordings help a lot. Complete reports are copied to our public GitHub issue tracker,
so don't include emails, player IDs, receipts or anything personal. Purchase or account problems: DM the
Developer instead.
```

### `#beta-feedback` post guidelines

Same template as `#bug-reports`, with this first paragraph instead:

```
Beta builds only. One post per bug or piece of feedback. Please keep beta content (screenshots, new features)
inside this server until it's released. Your report is tracked privately; only the version, device and a link
reach our issue tracker.
```

### `#suggestions` post guidelines

```
One idea per post. Search first and 👍 existing ideas instead of reposting.

**Idea:** what you'd like
**Why:** what problem it solves or what it would make more fun
**Where:** which world, menu or system (if any)

We tag ideas **Planned**, **Not planned** or **Done**. Planned ones go on our tracker, and the post is tagged
Done when it ships.
```

### `#strategy` post guidelines

```
Share a build, layout or prestige plan. Pick the world tag. Screenshots welcome. No exploits: report those
privately to a Moderator or in #bug-reports.
```

### Automatic messages

The bot's replies (template reminder, "now tracked as", "fixed", "won't fix", closing note) are in
`scripts/discord/forum_sync.py` (`needs_info_reply`, `tracked_reply`, `closed_reply`, `EXPIRED_REPLY`).
Announcement embeds are in `scripts/discord/notify.py`; their notes text is `docs/whats-new.md`, the same text the
game's what's-new card shows.

## Automation

### What runs, and when

| Trigger | Workflow | Posts to | Content |
|---|---|---|---|
| a PR merges into `release/**` (internal build to Play) | `release-internal.yml`, last step of the upload job | `#beta-announcements` (webhook) | "Quantum Foundry X.Y.Z beta build is ready" + what's-new notes, pings **Beta News Ping** |
| any release workflow fails (internal, prod, iOS) | `discord-alert` job in each | `#ci-alerts` (webhook) | "<workflow> failed on <branch>" + run link |
| by hand, after a production rollout | `discord-announce.yml` (Actions > Discord Announce > version, channel) | `#patch-notes` (or `#beta-announcements`) | "Quantum Foundry X.Y.Z is out" + what's-new notes, pings **Patch Notes Ping** |
| every 20 minutes, and when a `from-discord` issue closes or reopens | `discord-sync.yml` | the forum posts | template checks, GitHub issues, status tags (rules in the script's docstring) |
| weekly (#529, not built yet) | the league job | `#league-results` (webhook) | top players per league; player names only, never player IDs |

Production uploads go to Play as a **draft**, so `#patch-notes` waits for the manual Announce run when the rollout
actually starts. Publishing `#patch-notes` (an announcement channel) also lets followers' servers get it.

All of it skips cleanly while its secret is unset, and none of it can fail a release (`continue-on-error` or a
separate job).

`discord-sync.yml` uses a schedule and the `issues` event, which GitHub only runs from the **default branch
(`main`)**: the sync goes live when this work is promoted to `main`. Before that, test it with a manual run on the
branch with **dry run** ticked (Actions > Discord Forum Sync > Run workflow).

### Set up the bot (once)

1. https://discord.com/developers/applications > **New Application** "Foundry Bot" (icon: `docs/store/icon-512.png`).
2. **Bot** tab:
   - **Public Bot: off** (only you can add it to servers).
   - **Message Content Intent: on** (it reads the first message of each forum post; free and unreviewed under
     100 servers). Presence and Server Members intents: off.
   - **Reset Token**, copy it straight into the GitHub secret `DISCORD_BOT_TOKEN` (below). Don't paste it anywhere else.
3. **Installation** tab: Install Link "None"; **OAuth2** tab > URL Generator: scope `bot`, **no permissions**
   (permissions = 0). Open the URL, add the bot to the server.
4. In Discord, give the **Foundry Bot** role the FEEDBACK category and `#beta-feedback` overrides from the
   Channel overrides table.
5. Create the moderator-only status tags in each forum (Forum settings table).
6. Turn on **Developer Mode** (User Settings > Advanced) so right-click > **Copy ID** works for the IDs below.

### Webhooks (once)

Channel > Edit Channel > Integrations > Webhooks > New Webhook, name it, Copy Webhook URL, save it as the secret:

| Channel | Webhook name | GitHub secret |
|---|---|---|
| `#beta-announcements` | Beta Builds | `DISCORD_BETA_WEBHOOK` |
| `#patch-notes` | Patch Notes | `DISCORD_PATCH_NOTES_WEBHOOK` |
| `#ci-alerts` | CI | `DISCORD_STAFF_WEBHOOK` |
| `#league-results` | League Results | `DISCORD_LEAGUE_WEBHOOK` (when #529 lands) |

### GitHub secrets and variables

Repo > Settings > Secrets and variables > Actions. Secrets are hidden in logs; variables are plain IDs (not
secret: IDs give no access on their own).

| Kind | Name | Value |
|---|---|---|
| Secret | `DISCORD_BOT_TOKEN` | the bot token (step 2) |
| Secret | `DISCORD_BETA_WEBHOOK`, `DISCORD_PATCH_NOTES_WEBHOOK`, `DISCORD_STAFF_WEBHOOK` | webhook URLs |
| Variable | `DISCORD_GUILD_ID` | right-click the server icon > Copy Server ID |
| Variable | `DISCORD_BUG_FORUM_ID`, `DISCORD_BETA_FORUM_ID`, `DISCORD_IDEA_FORUM_ID` | right-click each forum > Copy Channel ID |
| Variable | `DISCORD_BETA_PING_ROLE_ID`, `DISCORD_PATCH_PING_ROLE_ID` | Server Settings > Roles > right-click the role > Copy Role ID |

### Checking it works

Manual and scheduled workflows only run once their file is on `main`. **Discord Check**
(`.github/workflows/discord-check.yml`) runs on any PR that touches the Discord files, with the real secrets:

1. **Validate (read-only):** `scripts/discord/validate_server.py` logs in as the bot and checks the server against
   this doc: safety settings the API exposes, roles and their permissions, categories and channels (privacy,
   read-only), forum status tags (moderator-only), guidelines, the bot's overrides, the ping role and forum IDs,
   and that each webhook points at the right channel (a GET, nothing is posted). It ends with the few things a
   bot can't read (AutoMod, raid/DM options, Onboarding questions): check those by hand.
2. **Forum sync dry run:** the bot reads every forum post and prints what it would do.
3. **Webhook post test:** add the label **`discord-post-test`** to the PR and each webhook posts one test message.
   Delete them afterwards.

After that, the real pipeline: the next integration -> `release/**` merge announces the beta build in
`#beta-announcements`, and a failed release posts in `#ci-alerts`. Once on `main`: Actions > Discord Announce
(channel beta) for a manual post, and the forum flow: post a bug without the template; within 20 minutes it's
tagged **Needs info**. Fill the template: the next run opens an issue and tags it **Tracked**. Close the issue:
**Fixed** and archived.

Locally: `python -m unittest scripts/discord/test_discord.py`.

## AutoMod (Server Settings > AutoMod)

Set up first. Every rule: block the message, **send an alert to `#mod-log`**, exempt Developer and Moderator.

| Rule | Settings | Action |
|---|---|---|
| Commonly flagged words | all three Discord lists (insults/slurs, sexual content, severe profanity) | block + alert |
| Mention spam | more than 5 unique mentions; also "raid protection" on | block + alert + timeout 10 minutes |
| Suspected spam content | Discord's model | block + alert |
| Custom: scams | `free nitro`, `nitro gift`, `steam gift`, `*discord.gift*`, `crystal generator`, `free crystals`, `mod apk`, `hack apk`, `airdrop`, `wallet connect` | block + alert + timeout 1 hour |
| Custom: invites | keywords `*discord.gg/*`, `*discord.com/invite/*`, `*discordapp.com/invite/*`; **Allowed**: `discord.gg/aVt8Z9ntkC` (AutoMod regex has no lookaheads, so use the allow list) | block + alert |
| Custom: links in usernames | member profile rule: block names with `discord.gg`, `http` | block from interacting |

## Incentives

| Incentive | When | Notes |
|---|---|---|
| **Beta Tester** role + credit in the patch notes | beta phases | no code needed |
| Weekly shout-outs: best factory screenshot, league top 3 | from the open beta | posted in `#announcements` |
| Crystal reward codes (join the server, finish a beta) | after the redeem-code feature exists | follow-up issue #569 |
| Name colour or title for testers in game | later | reuses the PvE cosmetics (name colours, titles) |

Never reward store ratings or reviews: both Google Play and the App Store ban incentivised reviews. A reward
for joining the Discord or finishing a beta is fine.

## Rules (for the Rules screen and `#rules`)

1. Be kind. No harassment, hate speech, slurs or personal attacks.
2. Keep it suitable for all ages. No NSFW content.
3. No spam, advertising or self-promotion, including invites to other servers.
4. No cheats, exploits or hacked builds. Report exploits privately to a Moderator or in `#bug-reports`.
5. No real-money trading of accounts or crystals.
6. Don't share personal information, yours or anyone else's.
7. English in public channels so moderators can follow.
8. Moderators' decisions are final; appeal by DM to the Developer.

Enforcement: warning, then a timeout (1 hour to 1 day), then a kick, then a ban. Scams, hate speech and NSFW
can skip straight to a ban.

## Who answers what

| Topic | Who | Response target |
|---|---|---|
| Bug reports | Foundry Bot files them; Developer triages the `from-discord` issues and closes them when fixed (the post updates itself) | within 2 days |
| Purchases, lost crystals, account problems | Developer only, by DM; ask for the player ID from Settings | within 1 day |
| Rule breaks | Moderators | as seen |
| Suggestions | Developer: tag **Planned** / **Not planned** every week or two | weekly |
| Balance questions | Community first; Developer confirms when numbers change | as available |

Never ask for passwords, emails or payment details in public channels.

## Set-up checklist

- [ ] Create the server; name "Quantum Foundry"; icon `docs/store/icon-512.png`; banner from
      `docs/store/feature-graphic.png`
- [ ] Owner account 2FA (authenticator app) and backup codes
- [ ] Enable Community; apply the Security settings above
- [ ] Create roles in the order above, then categories and channels with their privacy and overrides
- [ ] Forum tags (status tags moderator-only), post guidelines and settings from the Forum settings table
- [ ] Post `#welcome` (store links from `docs/store/store-listing.md`) and `#rules`; set up the rules screen
- [ ] Onboarding: default channels, the three questions, the Server Guide
- [ ] AutoMod rules; alerts to `#mod-log`
- [x] Create a **non-expiring, unlimited-use** invite: **https://discord.gg/aVt8Z9ntkC** (2026-11-25)
- [x] Ship it as the `links.discordUrl` default, so **Join our Discord** shows in Settings > Support from the
      next build. To change or hide it without a build, set `links.discordUrl` in Remote Config (blank hides
      the button; only `https://discord.gg/...` or `https://discord.com/invite/...` links show it)
- [x] Mention it in the store listing's full description (`docs/store/store-listing.md`); add it to the press
      kit when that exists
- [ ] Foundry Bot application, token secret, bot added with no permissions, forum overrides
- [ ] Webhooks for `#beta-announcements`, `#patch-notes`, `#ci-alerts`; their secrets
- [ ] GitHub variables: server ID, the three forum IDs, the two ping role IDs
- [ ] Discord Check passes on the PR; `discord-post-test` label posts in all three webhook channels
- [ ] After promotion to `main`: confirm the scheduled sync runs (Actions > Discord Forum Sync)
