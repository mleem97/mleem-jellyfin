import importlib.util
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

if __name__=="__main__":
    unittest.main()
