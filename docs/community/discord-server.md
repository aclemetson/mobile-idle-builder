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
| **Moderator** | trusted volunteers (start with none or one) | Manage Messages, Timeout Members, Kick Members, Manage Threads, View Audit Log, Mention @everyone off |
| **Bots** | AutoMod helpers, webhooks | only what each bot needs; never Administrator |
| **Beta Tester** | testers in an active beta phase | sees the **BETA** category |
| **League Regular** | optional cosmetic: players who post league results often | none (a colour and recognition) |
| **Player** | everyone who finished onboarding | Send Messages, Add Reactions, Attach Files, Embed Links, Use External Emoji, Create Public Threads, Connect/Speak in voice |
| **@everyone** | new arrivals | View the welcome and rules channels only; no posting |

Ping roles (opt-in, no permissions): **Patch Notes Ping**, **League Ping**, **Beta News Ping**, picked in
onboarding so announcements only ping people who asked for them.

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
