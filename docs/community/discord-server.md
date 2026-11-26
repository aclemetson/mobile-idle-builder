# Discord Server

The structure of the Quantum Foundry Discord: roles, channels, permissions, bots, incentives and moderation.
Issue #533. It is the community hub the beta plan relies on (`docs/testing/beta-plan.md`): tester recruiting,
the beta feedback channel, patch notes and weekly league results.

Keep this file in step with the real server. Change the server, then update this file (or the other way round).

## Server settings

Turn on **Community** (Server Settings > Enable Community). That unlocks the Rules screen, Onboarding,
forum channels, announcement channels and Server Insights.

| Setting | Value | Why |
|---|---|---|
| Verification level | **Medium** (registered on Discord for 5+ minutes) | stops most throwaway spam accounts |
| Explicit media content filter | Filter messages from **all members** | a game for all ages |
| 2FA requirement for moderation | **On** | a stolen moderator account can't wipe channels |
| Default notifications | **Only @mentions** | new members aren't flooded |
| Rules screening | On, with the rules below | members accept the rules before they can post |
| Onboarding | On (see below) | gives roles and shows the right channels |

## Roles

Order matters: a role can only manage roles below it. Top to bottom:

| Role | Who | Permissions beyond @everyone |
|---|---|---|
| **Developer** | the owner | Administrator |
| **Moderator** | trusted volunteers (start with none or one) | Manage Messages, Timeout Members, Kick Members, Manage Threads, View Audit Log (full list below) |
| **Bots** | AutoMod helpers, webhooks | only what each bot needs; never Administrator |
| **Beta Tester** | testers in an active beta phase | sees the **BETA** category |
| **League Regular** | optional cosmetic: players who post league results often | none (a colour and recognition) |
| **Player** | everyone who finished onboarding | Send Messages, Add Reactions, Attach Files, Embed Links, Use External Emoji, Create Public Threads, Connect/Speak in voice |
| **@everyone** | new arrivals | View the welcome and rules channels only; no posting |

Ping roles (opt-in, no permissions): **Patch Notes Ping**, **League Ping**, **Beta News Ping**, picked in
onboarding so announcements only ping people who asked for them.

### Role settings (Server Settings > Roles > the role > Display)

| Role | Colour | Display separately | Anyone can @mention | How it's given |
|---|---|---|---|---|
| Developer | teal `#22D3EE` (the game's accent) | yes | no | by hand (owner) |
| Moderator | green `#34D399` | yes | yes (members can call a mod) | by hand |
| Bots | grey `#9CA3AF` | yes | no | automatically when a bot joins (give its own role this one's place) |
| Beta Tester | amber `#FBBF24` | yes | no | by hand, after adding the tester to Play / TestFlight |
| League Regular | purple `#A78BFA` | no | no | by hand, weekly |
| Player | none (default name colour) | no | no | Onboarding |
| Android / iPhone | none | no | no | Onboarding |
| Patch Notes / League / Beta News Ping | none | no | **yes** (used for announcements) | Onboarding |
| @everyone | n/a | n/a | n/a | everyone |

### Server-level permissions (Server Settings > Roles > the role > Permissions)

✅ = on. Blank = off (leave it on "inherit"; a role never needs to turn something off, because @everyone
starts with almost nothing). Developer has **Administrator**, which includes everything, so it is not listed.
Beta Tester, League Regular, platform and ping roles have **no** server-level permissions: Beta Tester access
comes from the channel overrides below.

| Permission (Discord's name) | @everyone | Player | Moderator | Bots (AutoMod helper, e.g. Carl-bot) |
|---|---|---|---|---|
| **General** | | | | |
| View Channels | ✅ | ✅ | ✅ | ✅ |
| Manage Channels | | | | |
| Manage Roles | | | | ✅ only if it gives reaction roles (keep its role above those roles) |
| Create Expressions | | | | |
| Manage Expressions | | | | |
| View Audit Log | | | ✅ | ✅ if it logs |
| View Server Insights | | | | |
| Manage Webhooks | | | | |
| Manage Server | | | | |
| **Membership** | | | | |
| Create Invite | | ✅ | ✅ | |
| Change Nickname | | ✅ | ✅ | |
| Manage Nicknames | | | ✅ | |
| Kick, Approve, and Reject Members | | | ✅ | |
| Ban Members | | | | |
| Timeout Members | | | ✅ | ✅ if it moderates |
| **Text channels** | | | | |
| Send Messages and Create Posts | | ✅ | ✅ | ✅ |
| Send Messages in Threads and Posts | | ✅ | ✅ | ✅ |
| Create Public Threads | | ✅ | ✅ | |
| Create Private Threads | | | ✅ | |
| Embed Links | | ✅ | ✅ | ✅ |
| Attach Files | | ✅ | ✅ | ✅ |
| Add Reactions | | ✅ | ✅ | ✅ |
| Use External Emoji | | ✅ | ✅ | ✅ |
| Use External Stickers | | ✅ | ✅ | |
| Mention @everyone, @here, and All Roles | | | | |
| Manage Messages | | | ✅ | ✅ if it moderates |
| Pin Messages | | | ✅ | |
| Bypass Slowmode | | | ✅ | ✅ |
| Manage Threads and Posts | | | ✅ | |
| Read Message History | ✅ | ✅ | ✅ | ✅ |
| Send Text-to-Speech Messages | | | | |
| Send Voice Messages | | ✅ | ✅ | |
| Create Polls | | ✅ | ✅ | |
| **Voice** | | | | |
| Connect | | ✅ | ✅ | |
| Speak | | ✅ | ✅ | |
| Video | | ✅ | ✅ | |
| Use Soundboard / Use External Sounds | | | | |
| Use Voice Activity | | ✅ | ✅ | |
| Priority Speaker | | | ✅ | |
| Mute Members / Deafen Members / Move Members | | | ✅ | |
| Set Voice Channel Status | | | ✅ | |
| **Apps** | | | | |
| Use Application Commands | | ✅ | ✅ | ✅ |
| Use Activities | | | | |
| Use External Apps | | | | |
| **Events** | | | | |
| Create Events / Manage Events | | | ✅ | |
| **Advanced** | | | | |
| Administrator | | | | never |

Why these choices:
- @everyone can read but not post: new arrivals see `#welcome` and `#rules`, then Onboarding gives **Player**.
- Nobody but the Developer can @everyone; announcements ping the opt-in ping roles instead.
- Ban Members stays with the Developer, so a compromised or rogue Moderator can't mass-ban. Moderators use
  timeouts and kicks.
- Soundboard, text-to-speech and external apps are off: noise and spam vectors with little upside.
- Webhooks (patch notes, league results) don't use a role: each is created on its channel by the Developer.

### Channel overrides (channel > Edit Channel > Permissions)

Set these on the **category** and let channels sync, except where a single channel differs.

| Category / channel | Role | Override |
|---|---|---|
| START HERE: `#welcome`, `#announcements` | @everyone | Send Messages ❌, Add Reactions ✅ |
| | Player | Send Messages ❌ (read-only) |
| `#rules` | (rules screen) | Discord manages it |
| GAME: `#patch-notes`, `#league-results` | Player | Send Messages ❌, Send Messages in Threads ✅ (discuss in threads), Create Public Threads ✅ |
| GAME: `#general`, `#screenshots`, `#strategy` | (none) | inherit the server settings |
| `#general` | Player | Slowmode 5 seconds (channel setting, not a permission) |
| FEEDBACK: `#bug-reports`, `#suggestions` | Player | inherit (posting creates a forum post). Require a tag on posts (forum setting) |
| BETA category | @everyone | View Channel ❌ |
| | Beta Tester | View Channel ✅ |
| | Moderator | View Channel ✅ |
| `#beta-announcements` | Beta Tester | Send Messages ❌ |
| VOICE category | (none) | inherit |
| STAFF category | @everyone | View Channel ❌ |
| | Moderator | View Channel ✅ |
| | Bots | View Channel ✅ (only `#mod-log`, for logging) |

The Developer sees and does everything through Administrator, so it never needs an override.

## Onboarding

Server Settings > Onboarding. Default channels: `#welcome`, `#rules`, `#announcements`, `#general`.

Questions:
1. "What brings you here?" (multiple choice): Playing the game -> Player; Beta testing -> show the beta sign-up
   form (the Beta Tester role is given by hand, after the tester is added to the Play / TestFlight list);
   Just looking -> Player.
2. "Which pings do you want?" -> Patch Notes Ping, League Ping, Beta News Ping (all optional).
3. "Which platform do you play on?" -> Android / iPhone (cosmetic roles, useful when triaging bugs).

## Channels

| Category | Channel | Type | Who can post | Purpose |
|---|---|---|---|---|
| **START HERE** | `#welcome` | text, read-only | Developer | what the game is, store links, how to get roles |
| | `#rules` | rules screen | Developer | the rules below |
| | `#announcements` | announcement | Developer | releases, events, beta phases; other servers can follow it |
| **GAME** | `#patch-notes` | announcement | Developer, bot | what's new in each release, posted by CI (see Bots) |
| | `#general` | text | Player | general chat |
| | `#strategy` | forum, tags: Physics, Chemistry, Biology, Metallurgy, Engineering, Prestige, Leagues | Player | builds, layouts, prestige timing |
| | `#screenshots` | text (media) | Player | factory screenshots and clips; good source of marketing material (ask first) |
| | `#league-results` | text | bot, Developer | weekly PvE league results, posted by the #529 job; players react and discuss in threads |
| **FEEDBACK** | `#bug-reports` | forum, tags: Crash, Save/Cloud, UI, Balance, Purchases/Ads, Android, iPhone, Fixed | Player | one post per bug; template asks for version, device and steps (Settings shows the version) |
| | `#suggestions` | forum, tags: Gameplay, UI, Content, Leagues, Planned, Not planned, Done | Player | one post per idea; reactions to vote |
| **BETA** (Beta Tester only) | `#beta-announcements` | text | Developer | new beta builds, survey links |
| | `#beta-feedback` | forum | Beta Tester | beta-only feedback; triaged into GitHub issues labelled `beta` |
| **VOICE** | `Factory Floor` | voice | Player | hang out; optional, can be added later |
| **STAFF** (hidden) | `#mod-log` | text | Moderator, bots | AutoMod alerts and moderation actions |
| | `#mod-chat` | text | Moderator, Developer | moderation discussion |

Start small. Add `VOICE` and `League Regular` once there are enough people to use them.

## Bots and automation

1. **AutoMod** (built in, set up first). Rules:
   - Block commonly flagged words (all three Discord lists), alert in `#mod-log`.
   - Block mention spam (more than 5 mentions in one message), timeout 10 minutes.
   - Block suspected spam content (Discord's model).
   - Block invite links from other servers, except in Moderator messages.
   - Custom keywords: scam bait such as "free nitro", "steam gift", fake "crystal generator" or "hack".
2. **Patch notes from CI.** A Discord **webhook** on `#patch-notes`, stored as the GitHub secret
   `DISCORD_PATCH_NOTES_WEBHOOK`. The release workflow already writes the what's-new notes from
   `docs/whats-new.md` (#502); a later step can post the same text there. Skipped while the secret is unset.
3. **Weekly league results.** The #529 job posts a short summary (top players by league, link to the report)
   to a webhook on `#league-results`. Player names only, never player IDs.
4. **Extra bot (only if needed).** Carl-bot or MEE6 for reaction roles, a welcome message or logs, if AutoMod and
   Onboarding are not enough. Give it only the permissions its features need.

Webhook URLs are secrets: anyone with the URL can post as the webhook. Keep them in GitHub secrets only.

## Incentives

| Incentive | When | Notes |
|---|---|---|
| **Beta Tester** role + credit in the patch notes | beta phases | no code needed |
| Weekly shout-outs: best factory screenshot, league top 3 | from the open beta | posted in `#announcements` |
| Crystal reward codes (join the server, finish a beta) | after the redeem-code feature exists | follow-up issue #569 |
| Name colour or title for testers in game | later | reuses the PvE cosmetics (name colours, titles) |

Never reward store ratings or reviews: both Google Play and the App Store ban incentivised reviews. A reward
for joining the Discord or finishing a beta is fine.

## Rules (for the Rules screen)

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
| Bug reports | Developer: tag, reproduce, open a GitHub issue, tag the post **Fixed** when it ships | within 2 days |
| Purchases, lost crystals, account problems | Developer only, by DM; ask for the player ID from Settings | within 1 day |
| Rule breaks | Moderators | as seen |
| Suggestions | Developer: tag **Planned** / **Not planned** every week or two | weekly |
| Balance questions | Community first; Developer confirms when numbers change | as available |

Never ask for passwords, emails or payment details in public channels.

## Set-up checklist

- [ ] Create the server; name "Quantum Foundry"; icon `docs/store/icon-512.png`; banner from
      `docs/store/feature-graphic.png`
- [ ] Enable Community; apply the server settings above
- [ ] Create roles in the order above, then categories and channels with their permissions
- [ ] Write `#welcome` (store links from `docs/store/store-listing.md`) and the rules screen
- [ ] Set up Onboarding questions and the default channels
- [ ] Set up AutoMod rules; point alerts to `#mod-log`
- [ ] Forum post guidelines and tags for `#bug-reports`, `#suggestions`, `#beta-feedback`
- [ ] Create a **non-expiring, unlimited-use** invite from `#welcome` (a custom vanity URL needs a high boost level)
- [ ] Put the invite in Remote Config `links.discordUrl` (development, then production): the **Join our Discord**
      button appears in Settings > Support on the next launch, no build needed. Only `https://discord.gg/...` or
      `https://discord.com/invite/...` links show the button
- [ ] Add the invite to the store listing's full description and the press kit when they mention it
- [ ] Webhooks for `#patch-notes` and `#league-results`; store their URLs as GitHub secrets
