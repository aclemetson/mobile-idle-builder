"""Checks the live Discord server against docs/community/discord-server.md (#533). Standard library only.

    python scripts/discord/validate_server.py [--post-test]

Read-only by default. It uses the bot token to read the server's settings, roles, channels and forums, and GETs
each webhook (which shows its channel without posting). --post-test also posts one test message per webhook.
Run by .github/workflows/discord-check.yml with the repo's secrets and variables, so it checks the real values.

Environment: DISCORD_BOT_TOKEN, DISCORD_GUILD_ID, DISCORD_BUG_FORUM_ID, DISCORD_BETA_FORUM_ID,
DISCORD_IDEA_FORUM_ID, DISCORD_BETA_PING_ROLE_ID, DISCORD_PATCH_PING_ROLE_ID, DISCORD_BETA_WEBHOOK,
DISCORD_PATCH_NOTES_WEBHOOK, DISCORD_STAFF_WEBHOOK.

Prints PASS / WARN / FAIL per check; exits 1 on any FAIL. Some settings aren't readable by a bot with no
permissions (AutoMod, Safety Setup's raid and DM options): those are listed as manual checks at the end.
"""
from __future__ import annotations

import json
import os
import re
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import notify  # noqa: E402

API = "https://discord.com/api/v10"
UA = "DiscordBot (https://github.com, 1.0)"

P = {name: 1 << bit for name, bit in {
    "CREATE_INVITE": 0, "KICK": 1, "BAN": 2, "ADMIN": 3, "MANAGE_CHANNELS": 4, "MANAGE_GUILD": 5,
    "ADD_REACTIONS": 6, "VIEW_AUDIT_LOG": 7, "VIEW_CHANNEL": 10, "SEND": 11, "TTS": 12, "MANAGE_MESSAGES": 13,
    "EMBED_LINKS": 14, "ATTACH": 15, "READ_HISTORY": 16, "MENTION_EVERYONE": 17, "MANAGE_ROLES": 28,
    "MANAGE_WEBHOOKS": 29, "MANAGE_THREADS": 34, "EXTERNAL_STICKERS": 37, "SEND_IN_THREADS": 38,
    "MODERATE_MEMBERS": 40, "SOUNDBOARD": 42, "EXTERNAL_SOUNDS": 45, "EXTERNAL_APPS": 50}.items()}
DANGEROUS = ["ADMIN", "MANAGE_GUILD", "MANAGE_ROLES", "MANAGE_CHANNELS", "MANAGE_WEBHOOKS", "BAN",
             "MENTION_EVERYONE"]
TEXT, VOICE, CATEGORY, ANNOUNCEMENT, FORUM, MEDIA = 0, 2, 4, 5, 15, 16
REQUIRE_TAG = 1 << 4

EXPECTED = {   # category -> [(channel, allowed types, read_only)]
    "start here": [("welcome", {TEXT}, True), ("rules", {TEXT}, True), ("announcements", {ANNOUNCEMENT}, True)],
    "game": [("patch-notes", {ANNOUNCEMENT}, True), ("general", {TEXT}, False), ("strategy", {FORUM}, False),
             ("screenshots", {MEDIA, TEXT}, False), ("league-results", {TEXT, ANNOUNCEMENT}, True)],
    "feedback": [("bug-reports", {FORUM}, False), ("suggestions", {FORUM}, False)],
    "beta": [("beta-announcements", {TEXT, ANNOUNCEMENT}, True), ("beta-feedback", {FORUM}, False)],
    "staff": [("mod-log", {TEXT}, False), ("mod-chat", {TEXT}, False), ("ci-alerts", {TEXT}, False),
              ("discord-updates", {TEXT}, False)],
}
PRIVATE = {"beta", "staff"}
ROLES = ["Developer", "Moderator", "Beta Tester", "League Regular", "Android", "iPhone",
         "Patch Notes Ping", "League Ping", "Beta News Ping"]
PING_ROLES = ["Patch Notes Ping", "League Ping", "Beta News Ping"]
STATUS_TAGS = {"bug-reports": ["Tracked", "Needs info", "Fixed", "Won't fix"],
               "beta-feedback": ["Tracked", "Needs info", "Fixed", "Won't fix"],
               "suggestions": ["Planned", "Done", "Not planned"]}
WEBHOOKS = [("DISCORD_BETA_WEBHOOK", "beta-announcements"), ("DISCORD_PATCH_NOTES_WEBHOOK", "patch-notes"),
            ("DISCORD_STAFF_WEBHOOK", "ci-alerts")]

results = {"PASS": 0, "WARN": 0, "FAIL": 0}


def report(level: str, msg: str) -> None:
    results[level] += 1
    if level == "PASS":
        print(f"  PASS  {msg}")
    else:
        print(f"::{'warning' if level == 'WARN' else 'error'}::{level}  {msg}")


def check(ok: bool, msg: str, level_if_bad: str = "FAIL") -> bool:
    report("PASS" if ok else level_if_bad, msg)
    return ok


def get(path: str, token: str | None = None):
    headers = {"User-Agent": UA}
    if token:
        headers["Authorization"] = f"Bot {token}"
    req = urllib.request.Request(path if path.startswith("http") else API + path, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        return {"_error": e.code}


def norm(name: str) -> str:
    """'📢┃patch-notes' -> 'patch-notes'; 'START HERE' -> 'start here'."""
    return re.sub(r"[^a-z0-9\- ']", "", name.lower()).strip(" -")


def find(items, wanted, key=lambda x: x["name"]):
    for it in items:
        n = norm(key(it))
        if n == wanted or n.endswith(wanted):
            return it
    return None


def overwrite(chan: dict, target_id: str) -> tuple[int, int]:
    for ow in chan.get("permission_overwrites", []):
        if ow["id"] == target_id:
            return int(ow["allow"]), int(ow["deny"])
    return 0, 0


def has(bits: int, *names: str) -> bool:
    return all(bits & P[n] for n in names)


def main(argv) -> int:
    post_test = "--post-test" in argv
    env = os.environ
    token = (env.get("DISCORD_BOT_TOKEN") or "").strip()
    gid = (env.get("DISCORD_GUILD_ID") or "").strip()

    print("== Bot and server")
    if not check(bool(token), "DISCORD_BOT_TOKEN secret is set") or not check(gid.isdigit(), "DISCORD_GUILD_ID variable is set"):
        return 1
    me = get("/users/@me", token)
    if not check("_error" not in me, f"bot token logs in ({me.get('username', 'HTTP %s' % me.get('_error'))})"):
        return 1
    guild = get(f"/guilds/{gid}", token)
    if not check("_error" not in guild, f"bot is in server {gid} ({guild.get('name', 'HTTP %s' % guild.get('_error'))})"):
        return 1

    print("== Server settings")
    feats = set(guild.get("features", []))
    check("COMMUNITY" in feats, "Community is enabled")
    check(guild.get("verification_level", 0) >= 2, f"verification level Medium or higher (is {guild.get('verification_level')})")
    check(guild.get("mfa_level") == 1, "2FA required for moderator actions")
    check(guild.get("explicit_content_filter") == 2, "sensitive content filter: all members")
    check(guild.get("default_message_notifications") == 1, "default notifications: only @mentions", "WARN")
    check("MEMBER_VERIFICATION_GATE_ENABLED" in feats, "members must accept rules before they can talk")
    check("GUILD_ONBOARDING" in feats or "GUILD_ONBOARDING_EVER_ENABLED" in feats, "Onboarding is on", "WARN")

    roles = guild["roles"]
    channels = get(f"/guilds/{gid}/channels", token)
    by_id = {c["id"]: c for c in channels}
    everyone = next(r for r in roles if r["id"] == gid)

    def chan_name(cid):
        return norm(by_id[cid]["name"]) if cid in by_id else None

    check((chan_name(guild.get("rules_channel_id")) or "").endswith("rules"), "Rules channel is #rules")
    check((chan_name(guild.get("public_updates_channel_id")) or "").endswith("discord-updates"),
          "Community updates channel is #discord-updates", "WARN")
    sysc = chan_name(guild.get("system_channel_id"))
    check(sysc is None or sysc.endswith("mod-log"), f"system messages go to #mod-log or nowhere (is {sysc})", "WARN")

    print("== Roles")
    found = {}
    for name in ROLES:
        r = find(roles, norm(name))
        if check(r is not None, f"role {name} exists"):
            found[name] = r
    pos = [found[n]["position"] for n in ["Developer", "Moderator", "Beta Tester"] if n in found]
    check(pos == sorted(pos, reverse=True), "order: Developer above Moderator above Beta Tester")
    ep = int(everyone["permissions"])
    check(has(ep, "VIEW_CHANNEL", "SEND", "SEND_IN_THREADS", "READ_HISTORY"),
          "@everyone can view, post and read history (the rules gate stops them until they accept)")
    for bad in ["MENTION_EVERYONE", "CREATE_INVITE", "ADMIN", "MANAGE_MESSAGES", "BAN", "KICK"]:
        check(not ep & P[bad], f"@everyone lacks {bad}")
    for noisy in ["TTS", "SOUNDBOARD", "EXTERNAL_SOUNDS", "EXTERNAL_APPS", "EXTERNAL_STICKERS"]:
        check(not ep & P[noisy], f"@everyone lacks {noisy}", "WARN")
    bot_role = next((r for r in roles if (r.get("tags") or {}).get("bot_id") == me["id"]), None)
    for r in roles:
        if int(r["permissions"]) & P["ADMIN"] and r.get("name") != "Developer":
            report("FAIL", f"role '{r['name']}' has Administrator (only Developer should)")
    if "Developer" in found:
        check(has(int(found["Developer"]["permissions"]), "ADMIN"), "Developer has Administrator")
    if "Moderator" in found:
        mp = int(found["Moderator"]["permissions"])
        check(not mp & P["BAN"], "Moderator can't ban")
        check(has(mp, "MANAGE_MESSAGES", "MODERATE_MEMBERS", "KICK", "MANAGE_THREADS"),
              "Moderator can manage messages, time out, kick, manage threads")
    for name in PING_ROLES:
        if name in found:
            check(int(found[name]["permissions"]) == 0, f"{name} has no permissions", "WARN")
            check(not found[name]["mentionable"], f"{name} isn't @mentionable by members", "WARN")
    for var, name in [("DISCORD_BETA_PING_ROLE_ID", "Beta News Ping"), ("DISCORD_PATCH_PING_ROLE_ID", "Patch Notes Ping")]:
        rid = (env.get(var) or "").strip()
        r = next((x for x in roles if x["id"] == rid), None)
        check(r is not None and norm(r["name"]).endswith(norm(name)), f"{var} is the {name} role")
    if check(bot_role is not None, f"bot role found ({bot_role['name'] if bot_role else '-'})"):
        bad = [n for n in DANGEROUS if int(bot_role["permissions"]) & P[n]]
        check(not bad, f"bot role has no dangerous server permissions {bad or ''}")
        check(int(bot_role["permissions"]) == 0, "bot role has no server-wide permissions", "WARN")

    print("== Categories and channels")
    cats = [c for c in channels if c["type"] == CATEGORY]
    named = {}
    for cat_name, expected in EXPECTED.items():
        cat = find(cats, cat_name)
        if not check(cat is not None, f"category {cat_name.upper()}"):
            continue
        if cat_name in PRIVATE:
            _, deny = overwrite(cat, gid)
            check(bool(deny & P["VIEW_CHANNEL"]), f"{cat_name.upper()} is private (@everyone can't view)")
        else:
            _, deny = overwrite(cat, gid)
            check(not deny & P["VIEW_CHANNEL"], f"{cat_name.upper()} is public")
        kids = [c for c in channels if c.get("parent_id") == cat["id"]]
        for name, types, read_only in expected:
            ch = find(kids, name)
            if not check(ch is not None and ch["type"] in types,
                         f"#{name} in {cat_name.upper()}" + (f" (type {ch['type']}, want {sorted(types)})" if ch and ch["type"] not in types else "")):
                continue
            named[name] = ch
            if cat_name in PRIVATE:
                _, deny = overwrite(ch, gid)
                check(bool(deny & P["VIEW_CHANNEL"]), f"#{name} is private (synced with its category)")
            if read_only:
                denied = overwrite(ch, gid)[1] & P["SEND"]
                if name == "beta-announcements" and "Beta Tester" in found:
                    denied = denied or overwrite(ch, found["Beta Tester"]["id"])[1] & P["SEND"]
                check(bool(denied), f"#{name} is read-only")
    if "general" in named:
        check(named["general"].get("rate_limit_per_user", 0) > 0, "#general has slowmode", "WARN")
    if "ci-alerts" in named and "Moderator" in found:
        allow, _ = overwrite(named["ci-alerts"], found["Moderator"]["id"])
        check(not allow & P["VIEW_CHANNEL"], "#ci-alerts is Developer-only (Moderator not allowed)")

    print("== Forums")
    for var, name in [("DISCORD_BUG_FORUM_ID", "bug-reports"), ("DISCORD_BETA_FORUM_ID", "beta-feedback"),
                      ("DISCORD_IDEA_FORUM_ID", "suggestions")]:
        cid = (env.get(var) or "").strip()
        ch = by_id.get(cid)
        if not check(ch is not None and norm(ch["name"]).endswith(name), f"{var} is #{name}"):
            continue
        tags = {t["name"].strip().lower(): t for t in ch.get("available_tags", [])}
        for tag in STATUS_TAGS[name]:
            t = tags.get(tag.lower())
            if check(t is not None, f"#{name} has tag '{tag}'"):
                check(t["moderated"], f"#{name} tag '{tag}' is moderator-only")
        check(bool(int(ch.get("flags", 0)) & REQUIRE_TAG), f"#{name} requires a tag", "WARN")
        guide = ch.get("topic") or ""
        if name == "suggestions":
            check("idea" in guide.lower(), f"#{name} has post guidelines", "WARN")
            emoji = (ch.get("default_reaction_emoji") or {}).get("emoji_name")
            check(emoji == "👍", f"#{name} default reaction is 👍", "WARN")
        else:
            check(all(f in guide for f in ["Version:", "Device:", "What happened:", "Steps to reproduce:"]),
                  f"#{name} post guidelines contain the template")
        if bot_role:
            allow = overwrite(ch, bot_role["id"])[0]
            if not allow and ch.get("parent_id") in by_id:   # unsynced channel: fall back to the category
                allow = overwrite(by_id[ch["parent_id"]], bot_role["id"])[0]
            need = ["VIEW_CHANNEL", "READ_HISTORY", "SEND_IN_THREADS", "MANAGE_THREADS"]
            missing = [n for n in need if not allow & P[n]]
            check(not missing, f"bot override on #{name} {('missing ' + ', '.join(missing)) if missing else 'ok'}")
        threads = get(f"/channels/{cid}/threads/archived/public?limit=1", token)
        check("_error" not in threads, f"bot can read #{name} posts" + (f" (HTTP {threads.get('_error')})" if "_error" in threads else ""))

    print("== Webhooks")
    for var, name in WEBHOOKS:
        url = (env.get(var) or "").strip()
        if not check(bool(url), f"{var} secret is set"):
            continue
        if not check(bool(notify.WEBHOOK_RE.match(url)), f"{var} looks like a Discord webhook URL"):
            continue
        hook = get(url)
        if not check("_error" not in hook, f"{var} is a live webhook" + (f" (HTTP {hook.get('_error')})" if "_error" in hook else "")):
            continue
        check(hook.get("guild_id") == gid, f"{var} belongs to this server")
        target = chan_name(hook.get("channel_id")) or "?"
        check(target.endswith(name), f"{var} posts to #{name} (it posts to #{target})")
        if post_test:
            try:
                notify.post(url, {"content": "", "allowed_mentions": {"parse": []}, "embeds": [{
                    "title": f"Webhook test: {hook.get('name', var)}",
                    "description": "Setup validation from CI (discord-check.yml). Safe to delete.",
                    "color": notify.TEAL}]})
                report("PASS", f"{var} test message posted")
            except SystemExit as e:
                report("FAIL", f"{var} test post failed: {e}")

    print("== Check by hand (a bot with no permissions can't read these)")
    for item in ["AutoMod: the six rules, alerts to #mod-log",
                 "Safety Setup: raid CAPTCHA, activity alerts, DM filters",
                 "Onboarding: the 3 questions, default channels, Server Guide",
                 "#welcome and #rules posts; owner account 2FA"]:
        print(f"  TODO  {item}")
    print(f"\n{results['PASS']} passed, {results['WARN']} warnings, {results['FAIL']} failed")
    return 1 if results["FAIL"] else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
