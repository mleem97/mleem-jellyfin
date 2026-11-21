"""Release gates must reject broken packages before any repository mutation."""

from __future__ import annotations

import copy
import json
import pathlib
import sys
import tempfile
import unittest
import warnings
import zipfile
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / ".github" / "scripts"))
import auto_publish_plugins as publisher
import publish_plugins
import release_validation as validation


class ReleaseValidationTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.package = self.root / "Example_1.0.0.0.zip"
        self.metadata = {
            "slug": "Example",
            "guid": "eb5d7894-8eef-4b36-aa6f-5d124e828ce1",
            "assembly": "Example.dll",
            "artifacts": ["Example.dll", "Dependency.dll"],
            "version": "1.0.0.0",
            "targetAbi": "12.1.0.0",
        }
        self.write_zip({"Example.dll": b"plugin", "Dependency.dll": b"dependency"})
        self.catalog = self.root / "manifest.json"
        self.version = {
            "version": "1.0.0.0",
            "targetAbi": "12.1.0.0",
            "checksum": validation.md5_upper(self.package),
            "sourceUrl": "https://example.com/Example_1.0.0.0.zip",
        }
        self.write_manifest([self.version])

    def write_zip(self, entries):
        with zipfile.ZipFile(self.package, "w") as archive:
            for name, content in entries.items():
                archive.writestr(name, content)

    def write_manifest(self, versions):
        self.catalog.write_text(json.dumps([{"guid": self.metadata["guid"], "versions": versions}]), encoding="utf-8")

    def test_valid_package_and_manifest(self):
        validation.validate_package(self.metadata, self.package)
        validation.validate_manifest(self.catalog, self.metadata["guid"], "1.0.0.0", self.package, "12.1.0.0")

    def test_missing_dependency_rejected(self):
        self.write_zip({"Example.dll": b"plugin"})
        with self.assertRaisesRegex(SystemExit, "missing required"):
            validation.validate_package(self.metadata, self.package)

    def test_empty_artifact_rejected(self):
        self.write_zip({"Example.dll": b"", "Dependency.dll": b"dependency"})
        with self.assertRaisesRegex(SystemExit, "empty"):
            validation.validate_package(self.metadata, self.package)

    def test_nested_and_windows_paths_rejected(self):
        for name in ("../Example.dll", "/Example.dll", "folder/Example.dll", "..\\Example.dll", "C:Example.dll"):
            with self.subTest(name=name):
                self.write_zip({name: b"plugin"})
                with self.assertRaisesRegex(SystemExit, "Unsafe"):
                    validation.validate_package(self.metadata, self.package)

    def test_symlink_rejected(self):
        member = zipfile.ZipInfo("Example.dll")
        member.create_system = 3
        member.external_attr = 0o120777 << 16
        with zipfile.ZipFile(self.package, "w") as archive:
            archive.writestr(member, "outside.dll")
        with self.assertRaisesRegex(SystemExit, "Unsafe"):
            validation.validate_package(self.metadata, self.package)

    def test_duplicate_artifact_rejected(self):
        with warnings.catch_warnings(), zipfile.ZipFile(self.package, "w") as archive:
            warnings.simplefilter("ignore", UserWarning)
            archive.writestr("Example.dll", b"one")
            archive.writestr("example.DLL", b"two")
        with self.assertRaisesRegex(SystemExit, "duplicate"):
            validation.validate_package(self.metadata, self.package)

    def test_unknown_artifact_rejected(self):
        self.write_zip({"Example.dll": b"plugin", "Dependency.dll": b"dependency", "secret.txt": b"secret"})
        with self.assertRaisesRegex(SystemExit, "unexpected"):
            validation.validate_package(self.metadata, self.package)

    def test_same_version_distinct_abi_is_valid(self):
        another = dict(self.version, targetAbi="10.11.0.0", sourceUrl="https://example.com/Example_net9.zip")
        self.write_manifest([self.version, another])
        validation.validate_manifest(self.catalog, self.metadata["guid"], "1.0.0.0", self.package, "12.1.0.0")
        with self.assertRaisesRegex(SystemExit, "identify one build"):
            validation.validate_manifest(self.catalog, self.metadata["guid"], "1.0.0.0", self.package)

    def test_duplicate_version_and_abi_rejected(self):
        self.write_manifest([self.version, dict(self.version, targetAbi="12.1.0")])
        with self.assertRaisesRegex(SystemExit, "Duplicate manifest"):
            validation.validate_manifest_structure(self.catalog)

    def test_bad_checksum_fails_cleanly(self):
        self.write_manifest([dict(self.version, checksum="not-an-md5")])
        with self.assertRaisesRegex(SystemExit, "not an MD5"):
            validation.validate_manifest_structure(self.catalog)

    def test_wrong_package_checksum_rejected(self):
        self.write_manifest([dict(self.version, checksum="0" * 32)])
        with self.assertRaisesRegex(SystemExit, "does not match"):
            validation.validate_manifest(self.catalog, self.metadata["guid"], "1.0.0.0", self.package)

    def test_http_and_embedded_credentials_rejected(self):
        for url in ("http://example.com/a.zip", "https://", "https://secret@example.com/a.zip"):
            with self.subTest(url=url):
                self.write_manifest([dict(self.version, sourceUrl=url)])
                with self.assertRaisesRegex(SystemExit, "Invalid sourceUrl"):
                    validation.validate_manifest_structure(self.catalog)

    def test_published_verification_uses_sha256(self):
        def download(args):
            destination = pathlib.Path(args[args.index("--dir") + 1]) / self.package.name
            destination.write_bytes(b"different package")

        with patch.object(publisher, "run", side_effect=download), patch.object(validation, "md5_upper", return_value="0" * 32):
            with self.assertRaisesRegex(SystemExit, "SHA-256"):
                validation.verify_published_asset(self.metadata, "1.0.0.0", self.package)

    def test_release_downgrade_rejected(self):
        with patch.object(publisher, "MANIFEST_PATH", self.catalog), patch.object(publisher, "run", return_value=""):
            with self.assertRaisesRegex(SystemExit, "newer"):
                publish_plugins.ensure_version_available(self.metadata, "0.2.0.0")

    def test_catalog_update_preserves_other_abi_and_plugins(self):
        older = dict(self.version, targetAbi="10.11.0.0")
        other = {"guid": "6b6c96ac-3f60-4ac3-8b8a-0d0cb8acb9dd", "versions": [copy.deepcopy(older)]}
        self.catalog.write_text(json.dumps([{"guid": self.metadata["guid"], "versions": [older]}, other]), encoding="utf-8")
        metadata = dict(self.metadata, name="Example", description="Description", overview="Overview", owner="owner", category="General")
        with patch.object(publisher, "MANIFEST_PATH", self.catalog):
            publisher.update_manifest(metadata, "1.0.0.0", "changes", self.package)
        result = json.loads(self.catalog.read_text(encoding="utf-8"))
        self.assertEqual(result[1], other)
        self.assertEqual([item["targetAbi"] for item in result[0]["versions"]], ["12.1.0.0", "10.11.0.0"])


if __name__ == "__main__":
    unittest.main()
