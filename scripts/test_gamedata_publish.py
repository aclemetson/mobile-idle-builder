"""Unit tests for scripts/gamedata_publish.py (#210).  python -m unittest scripts/test_gamedata_publish.py"""
import copy
import json
import sys
import unittest
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gamedata_publish as gp  # noqa: E402

FIELDS = {
    "config": {"prestigeBaseValue", "startingEntropy"},
    "items": {"baseSellValue"},
    "research": {"durationSeconds"},
}
IDS = {"items": {"iron"}, "research": {"automation_1"}}
GOOD = {
    "version": 3,
    "notice": {"title": "Game Updated", "message": "Balance pass."},
    "overrides": {
        "config": [{"field": "prestigeBaseValue", "value": 600}],
        "items": [{"id": "iron", "field": "baseSellValue", "value": 2.5}],
        "research": [],
    },
}


def errors_for(mutate):
    doc = copy.deepcopy(GOOD)
    mutate(doc)
    return gp.validate(doc, FIELDS, IDS)


class ValidateTests(unittest.TestCase):
    def test_good_document_has_no_errors(self):
        self.assertEqual(gp.validate(copy.deepcopy(GOOD), FIELDS, IDS), [])

    def test_unknown_field_is_rejected(self):
        errs = errors_for(lambda d: d["overrides"]["config"].append({"field": "nope", "value": 1}))
        self.assertTrue(any("'nope' is not overridable" in e for e in errs), errs)

    def test_unknown_item_id_is_rejected(self):
        errs = errors_for(lambda d: d["overrides"]["items"].append(
            {"id": "unobtainium", "field": "baseSellValue", "value": 1}))
        self.assertTrue(any("unknown items id 'unobtainium'" in e for e in errs), errs)

    def test_non_numeric_and_negative_values_are_rejected(self):
        errs = errors_for(lambda d: d["overrides"]["config"].extend([
            {"field": "startingEntropy", "value": "250"}]))
        self.assertTrue(any("finite number" in e for e in errs), errs)
        errs = errors_for(lambda d: d["overrides"]["config"].__setitem__(0, {"field": "prestigeBaseValue", "value": -1}))
        self.assertTrue(any("must not be negative" in e for e in errs), errs)

    def test_bool_is_not_a_number(self):
        errs = errors_for(lambda d: d["overrides"]["config"].__setitem__(0, {"field": "prestigeBaseValue", "value": True}))
        self.assertTrue(any("finite number" in e for e in errs), errs)

    def test_duplicate_override_is_rejected(self):
        errs = errors_for(lambda d: d["overrides"]["config"].append({"field": "prestigeBaseValue", "value": 700}))
        self.assertTrue(any("duplicate" in e for e in errs), errs)

    def test_unknown_section_and_top_level_keys_are_rejected(self):
        errs = errors_for(lambda d: d["overrides"].__setitem__("buildings", []))
        self.assertTrue(any("unknown section" in e for e in errs), errs)
        errs = errors_for(lambda d: d.__setitem__("overides", {}))
        self.assertTrue(any("unknown top-level" in e for e in errs), errs)

    def test_version_and_notice_are_required(self):
        self.assertTrue(errors_for(lambda d: d.__setitem__("version", -1)))
        self.assertTrue(errors_for(lambda d: d.__setitem__("version", "3")))
        self.assertTrue(errors_for(lambda d: d["notice"].__setitem__("title", "  ")))
        self.assertTrue(errors_for(lambda d: d["notice"].__setitem__("message", "x" * (gp.MAX_MESSAGE + 1))))

    def test_config_entry_must_not_carry_an_id(self):
        errs = errors_for(lambda d: d["overrides"]["config"][0].__setitem__("id", "x"))
        self.assertTrue(any("unknown key(s): id" in e for e in errs), errs)


class PayloadTests(unittest.TestCase):
    def test_blob_is_compact_and_drops_empty_sections(self):
        blob = gp.overrides_blob(GOOD["overrides"])
        self.assertNotIn(" ", blob)
        self.assertEqual(set(json.loads(blob)), {"config", "items"})
        self.assertEqual(gp.overrides_blob({}), "{}")

    def test_announce_sets_updated_utc(self):
        now = datetime(2026, 11, 26, 9, 5, 7, tzinfo=timezone.utc)
        p = gp.build_payload(GOOD, announce=True, now=now)
        self.assertEqual(p["gamedata.updatedUtc"], "2026-11-26T09:05:07Z")
        self.assertEqual(p["gamedata.version"], 3)
        self.assertNotIn("gamedata.updatedUtc", gp.build_payload(GOOD, announce=False))


class RepoTests(unittest.TestCase):
    """The real allow-list, game data and live file."""

    def test_allow_list_matches_game_data_overrides_cs(self):
        fields = gp.allowed_fields(gp.OVERRIDES_CS.read_text(encoding="utf-8"))
        self.assertIn("prestigeBaseValue", fields["config"])
        self.assertIn("baseSellValue", fields["items"])
        self.assertIn("durationSeconds", fields["research"])
        self.assertNotIn("baseSellValue", fields["config"])

    def test_sample_overrides_are_valid(self):
        sample = json.loads((gp.ROOT / "Assets" / "Data" / "overrides.sample.json").read_text(encoding="utf-8"))
        sample.pop("_comment", None)
        doc = {"version": 0, "notice": {"title": "t", "message": "m"}, "overrides": sample}
        fields = gp.allowed_fields(gp.OVERRIDES_CS.read_text(encoding="utf-8"))
        ids = gp.known_ids(json.loads(gp.GAME_DATA.read_text(encoding="utf-8")))
        errs = gp.validate(doc, fields, ids)
        # The sample may name ids that the item timeline later renamed; only field names must stay valid.
        self.assertFalse([e for e in errs if "not overridable" in e], errs)

    def test_live_file_is_valid(self):
        doc, errors = gp.load_and_validate(gp.DEFAULT_FILE)
        self.assertEqual(errors, [])


if __name__ == "__main__":
    unittest.main()
