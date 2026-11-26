"""Unit tests for scripts/discord (no network).  python -m unittest scripts/discord/test_discord.py"""
import sys
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import forum_sync as fs  # noqa: E402
import notify  # noqa: E402

GOOD = """**Version:** 0.6.133
**Device:** Pixel 8, Android 15
**What happened:** the belt froze
after a prestige
**Steps to reproduce:**
1. prestige
2. place a belt
**Expected:** items move"""


def forum(key="bug"):
    f = fs.Forum(key, "1", "#bug-reports", ["bug", "from-discord"], key != "idea", key != "beta")
    f.tags = {"crash": "10", "tracked": "20", "needs info": "21", "fixed": "22", "won't fix": "23",
              "planned": "30", "done": "31", "not planned": "32", "ui": "11"}
    return f


class TemplateTests(unittest.TestCase):
    def test_complete_post_parses_multiline_values(self):
        v = fs.parse_template(GOOD)
        self.assertEqual(v["Version"], "0.6.133")
        self.assertIn("after a prestige", v["What happened"])
        self.assertIn("2. place a belt", v["Steps to reproduce"])
        self.assertEqual(fs.missing_fields(v), [])

    def test_plain_labels_and_other_bold_styles(self):
        v = fs.parse_template("Version: 1.2.3\n__Device__: iPhone 15\nwhat happened: crash\n*Steps to reproduce*: open")
        self.assertEqual(fs.missing_fields(v), [])

    def test_empty_template_lists_every_required_field(self):
        self.assertEqual(fs.missing_fields(fs.parse_template(fs.BUG_TEMPLATE)), fs.BUG_REQUIRED)

    def test_free_text_post_is_missing_everything(self):
        self.assertEqual(fs.missing_fields(fs.parse_template("game crashed lol")), fs.BUG_REQUIRED)

    def test_version_must_look_like_a_version(self):
        v = fs.parse_template(GOOD.replace("0.6.133", "latest"))
        self.assertEqual(fs.missing_fields(v), ["Version"])

    def test_placeholders_count_as_missing(self):
        v = fs.parse_template(GOOD.replace("Pixel 8, Android 15", "(your phone)"))
        self.assertEqual(fs.missing_fields(v), ["Device"])


class SanitiseTests(unittest.TestCase):
    def test_defuses_mentions_refs_and_markers(self):
        out = fs.defuse("@octocat see #12 and owner/repo#3 <!-- discord-thread:99 --> <@123> &#169;")
        self.assertNotIn("@octocat", out)
        self.assertNotIn(" #12", out)
        self.assertNotIn("repo#3", out)
        self.assertNotIn("<!--", out)
        self.assertNotIn("<@123>", out)
        self.assertIn("&#169;", out)

    def test_body_has_one_marker_and_quotes_text(self):
        body = fs.issue_body(forum(), "https://discord.com/channels/1/2", "2",
                             GOOD + "\n<!-- discord-thread:5 -->", fs.parse_template(GOOD), 1)
        self.assertEqual(fs.MARKER_RE.findall(body), ["2"])
        self.assertIn("> **Version:** 0.6.133", body)
        self.assertIn("1 attachment", body)

    def test_beta_body_does_not_copy_text(self):
        body = fs.issue_body(forum("beta"), "u", "2", "secret new feature", fs.parse_template(GOOD), 0)
        self.assertNotIn("secret new feature", body)
        self.assertIn("0.6.133", body)

    def test_long_text_is_truncated(self):
        self.assertLess(len(fs.quote("x" * 20000)), fs.MAX_BODY_CHARS + 200)

    def test_title_prefix_and_length(self):
        self.assertTrue(fs.issue_title(forum(), "a" * 300).startswith("[Discord bug] "))
        self.assertLessEqual(len(fs.issue_title(forum(), "a" * 300)), 120)


class TagTests(unittest.TestCase):
    def test_needs_info_keeps_topic_tags(self):
        self.assertEqual(fs.wanted_tags(forum(), ["10"], None, True), ["21", "10"])

    def test_tracked_replaces_needs_info(self):
        self.assertEqual(fs.wanted_tags(forum(), ["10", "21"], {"state": "open"}, False), ["20", "10"])

    def test_closed_completed_is_fixed(self):
        self.assertEqual(fs.wanted_tags(forum(), ["20", "10"], {"state": "closed", "state_reason": "completed"},
                                        False), ["20", "22", "10"])

    def test_closed_not_planned_is_wont_fix(self):
        self.assertIn("23", fs.wanted_tags(forum(), ["20"], {"state": "closed", "state_reason": "not_planned"}, False))

    def test_reopened_drops_fixed(self):
        self.assertEqual(fs.wanted_tags(forum(), ["20", "22"], {"state": "open"}, False), ["20"])

    def test_idea_done_drops_planned(self):
        self.assertEqual(fs.wanted_tags(forum("idea"), ["30", "11"], {"state": "closed", "state_reason": "completed"},
                                        False), ["31", "11"])

    def test_at_most_five_tags(self):
        self.assertEqual(len(fs.wanted_tags(forum(), ["1", "2", "3", "4", "5"], {"state": "open"}, False)), 5)

    def test_snowflake_time(self):
        t = fs.snowflake_time(str(((1700000000000 - fs.DISCORD_EPOCH_MS) << 22)))
        self.assertEqual(t, datetime.fromtimestamp(1700000000, tz=timezone.utc))


class NotifyTests(unittest.TestCase):
    def test_release_payload_only_pings_the_given_role(self):
        p = notify.release_payload("0.6.133", "release", "- @everyone new stuff", "123", None)
        self.assertEqual(p["content"], "<@&123>")
        self.assertEqual(p["allowed_mentions"], {"parse": [], "roles": ["123"]})
        self.assertIn("0.6.133 is out", p["embeds"][0]["title"])

    def test_no_role_pings_nobody(self):
        p = notify.release_payload("1.0.0", "beta", "", None, None)
        self.assertEqual(p["allowed_mentions"], {"parse": [], "roles": []})
        self.assertIn("#beta-feedback", p["embeds"][0]["description"])

    def test_webhook_url_is_validated(self):
        import os
        os.environ["T_HOOK"] = "https://evil.example/api/webhooks/1/x"
        with self.assertRaises(SystemExit):
            notify.webhook_from_env("T_HOOK")
        os.environ["T_HOOK"] = "https://discord.com/api/webhooks/123/abc-DEF_9"
        self.assertTrue(notify.webhook_from_env("T_HOOK"))
        os.environ["T_HOOK"] = ""
        self.assertIsNone(notify.webhook_from_env("T_HOOK"))


if __name__ == "__main__":
    unittest.main()
