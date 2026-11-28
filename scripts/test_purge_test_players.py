"""Unit tests for scripts/purge_test_players.py decisions (no network).

    python -m unittest scripts/test_purge_test_players.py
"""
import json
import os
import sys
import unittest
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import purge_test_players as p  # noqa: E402

D = lambda *a: datetime(*a, tzinfo=timezone.utc)  # noqa: E731


class Decide(unittest.TestCase):
    def test_linked_identity_is_always_kept(self):
        pl = {"id": "a", "externalIds": [{"providerId": "google.com"}]}
        self.assertEqual("keep", p.decide(pl, False, "none")[0])
        self.assertIn("google.com", p.decide(pl, False, "none")[1])

    def test_production_data_is_always_kept(self):
        self.assertEqual("keep", p.decide({"id": "a"}, True, "none")[0])

    def test_no_save_anywhere_is_purged(self):
        self.assertEqual(("purge", "no save anywhere"), p.decide({"id": "a", "externalIds": []}, False, "none"))

    def test_phone_stamp_is_kept_even_if_the_editor_also_wrote_it(self):
        self.assertEqual("keep", p.decide({"id": "a"}, False, ["Editor", "Android"])[0])
        self.assertEqual("keep", p.decide({"id": "a"}, False, ["iOS"])[0])

    def test_editor_only_save_is_purged(self):
        self.assertEqual("purge", p.decide({"id": "a"}, False, ["Editor"])[0])

    def test_unstamped_save_is_kept_unless_asked(self):
        self.assertEqual("keep", p.decide({"id": "a"}, False, None)[0])
        self.assertEqual("purge", p.decide({"id": "a"}, False, None, include_unstamped=True)[0])

    def test_creation_window(self):
        pl = {"id": "a", "createdAt": "2026-11-10T00:00:00Z"}
        self.assertEqual("purge", p.decide(pl, False, "none", created_before=D(2026, 11, 20))[0])
        self.assertEqual("keep", p.decide(pl, False, "none", created_before=D(2026, 11, 1))[0])
        self.assertEqual("keep", p.decide(pl, False, "none", created_after=D(2026, 11, 15))[0])
        self.assertEqual("keep", p.decide({"id": "b"}, False, "none", created_before=D(2026, 11, 20))[0],
                         "an unreadable date is never purged when a window is given")


class Parsing(unittest.TestCase):
    def test_created_accepts_epoch_seconds_ms_and_iso(self):
        want = D(2026, 11, 27)
        secs = int(want.timestamp())
        self.assertEqual(want, p.parse_created(str(secs)))
        self.assertEqual(want, p.parse_created(str(secs * 1000)))
        self.assertEqual(want, p.parse_created("2026-11-27T00:00:00Z"))
        self.assertIsNone(p.parse_created("yesterday"))
        self.assertIsNone(p.parse_created(None))

    def test_save_platforms_from_string_or_object(self):
        save = {"playerId": "x", "platforms": ["Editor"]}
        self.assertEqual(["Editor"], p.save_platforms(json.dumps(save)))
        self.assertEqual(["Editor"], p.save_platforms(save))
        self.assertIsNone(p.save_platforms(json.dumps({"playerId": "x"})), "older save without the stamp")
        self.assertIsNone(p.save_platforms(json.dumps({"platforms": []})))
        self.assertIsNone(p.save_platforms("not json"))

    def test_next_start_from_link_id_or_last_result(self):
        self.assertEqual("p2", p.next_start("/cloud-save/v1/data/projects/x/environments/y/players?start=p2&limit=100", "p1"))
        self.assertEqual("p9", p.next_start("p9", "p1"))
        self.assertEqual("p1", p.next_start("/cloud-save/v1/.../players?limit=100", "p1"))


if __name__ == "__main__":
    unittest.main()
