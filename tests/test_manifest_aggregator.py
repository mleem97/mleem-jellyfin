import importlib.util
import json
import pathlib
import unittest

path = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "aggregate_jellyfin_manifests.py"
spec = importlib.util.spec_from_file_location("jellyfin_aggregator", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def entry(guid, name):
    return {"guid": guid, "name": name, "versions": [{"version":"1.0.0.0","targetAbi":"12.1.0.0","sourceUrl":"https://raw.githubusercontent.com/example/path/release.zip","checksum":"A"*32}]}

ONE="11111111-1111-1111-1111-111111111111"
TWO="22222222-2222-2222-2222-222222222222"

class AggregatorTests(unittest.TestCase):
    def test_combines_distinct_plugins(self):
        merged, report = module.merge_manifests([entry(ONE,"Own")],[("external",[entry(TWO,"External")])])
        self.assertEqual([x["name"] for x in merged], ["Own","External"])
        self.assertFalse(report["conflicts"])

    def test_preserves_owned_plugin_on_guid_conflict(self):
        merged, report = module.merge_manifests([entry(ONE,"Own")],[("external",[entry(ONE,"Fake")])])
        self.assertEqual(len(merged), 1)
        self.assertEqual(merged[0]["name"], "Own")
        self.assertEqual(len(report["conflicts"]), 1)

    def test_rejects_duplicate_guid(self):
        with self.assertRaises(module.ManifestError):
            module.validate_manifest([entry(ONE,"A"),entry(ONE,"B")],"feed")

    def test_rejects_untrusted_download(self):
        x=entry(ONE,"A")
        x["versions"][0]["sourceUrl"]="http://127.0.0.1/private"
        with self.assertRaises(module.ManifestError):
            module.validate_manifest([x],"feed")

    def test_rejects_duplicate_abi(self):
        x=entry(ONE,"A")
        x["versions"].append(dict(x["versions"][0]))
        with self.assertRaises(module.ManifestError):
            module.validate_manifest([x],"feed")


    def test_accepts_published_manifest_with_legacy_target_abi(self):
        # Regression: the published manifest contains an existing 12.0 ABI.
        manifest_path = pathlib.Path(__file__).resolve().parents[1] / "manifest.json"
        published = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(len(published), 18)
        self.assertTrue(any(v.get("targetAbi") == "12.0" for p in published for v in p.get("versions", [])))
        self.assertEqual(module.validate_manifest(published, "published"), published)

    def test_accepts_two_part_target_abi_without_relaxing_plugin_version(self):
        x = entry(ONE, "Legacy")
        x["versions"][0]["targetAbi"] = "12.0"
        self.assertEqual(module.validate_manifest([x], "feed"), [x])
        x["versions"][0]["version"] = "1.0"
        with self.assertRaises(module.ManifestError):
            module.validate_manifest([x], "feed")

    def test_rejects_invalid_target_abi(self):
        for invalid in ("12", "12.0.", "12.0.0.0.0", "a.b", "12.-1"):
            with self.subTest(invalid=invalid):
                x = entry(ONE, "Legacy")
                x["versions"][0]["targetAbi"] = invalid
                with self.assertRaises(module.ManifestError):
                    module.validate_manifest([x], "feed")

if __name__=="__main__":
    unittest.main()
