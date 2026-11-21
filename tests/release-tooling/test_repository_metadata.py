"""Metadata-driven plugin discovery must enforce the actual server dependency ABI."""

from __future__ import annotations

import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / ".github" / "scripts"))
from validate_repository import build_summary, validate_plugins


class RepositoryMetadataTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name)
        self.plugin = self.root / "plugins" / "Example"
        self.plugin.mkdir(parents=True)
        self.metadata = {
            "slug": "Example",
            "guid": "eb5d7894-8eef-4b36-aa6f-5d124e828ce1",
            "version": "1.0.0.0",
            "targetAbi": "12.2.0.0",
            "framework": "net10.0",
            "project": "plugins/Example/Example.csproj",
            "assembly": "Example.dll",
            "packagePrefix": "Example",
            "artifacts": ["Example.dll"],
        }
        (self.plugin / "plugin.json").write_text(json.dumps(self.metadata), encoding="utf-8")
        (self.root / "Jellyfin.Plugins.sln").write_text('"plugins\\Example\\Example.csproj"', encoding="utf-8")
        (self.plugin / "Example.csproj").write_text("""<Project><PropertyGroup>
          <TargetFramework>net10.0</TargetFramework><AssemblyName>Example</AssemblyName>
          <AssemblyVersion>1.0.0.0</AssemblyVersion><FileVersion>1.0.0.0</FileVersion><Version>1.0.0.0</Version>
          </PropertyGroup><ItemGroup><PackageReference Include="Jellyfin.Controller" /></ItemGroup></Project>""", encoding="utf-8")
        (self.root / "Directory.Packages.props").write_text('<Project><ItemGroup><PackageVersion Include="Jellyfin.Controller" Version="12.2.0" /></ItemGroup></Project>', encoding="utf-8")

    def test_central_package_versions_resolved(self):
        self.assertEqual(validate_plugins(self.root), ["Example"])

    def test_inherited_framework_is_resolved(self):
        project = self.plugin / "Example.csproj"
        project.write_text(project.read_text(encoding="utf-8").replace("<TargetFramework>net10.0</TargetFramework>", ""), encoding="utf-8")
        (self.root / "Directory.Build.props").write_text("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", encoding="utf-8")
        self.assertEqual(validate_plugins(self.root), ["Example"])

    def test_build_summary_is_deterministic_and_complete(self):
        summary = build_summary(self.root, ["Example"])
        self.assertIn('slug: "Example"', summary)
        self.assertIn('targetAbi: "12.2.0.0"', summary)
        self.assertIn('framework: "net10.0"', summary)
        self.assertEqual(summary, build_summary(self.root, ["Example"]))

    def test_source_cannot_claim_older_server_abi(self):
        self.metadata["targetAbi"] = "12.1.0.0"
        (self.plugin / "plugin.json").write_text(json.dumps(self.metadata), encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "older than referenced"):
            validate_plugins(self.root)

    def test_missing_central_version_is_not_silently_skipped(self):
        (self.root / "Directory.Packages.props").unlink()
        with self.assertRaisesRegex(SystemExit, "cannot resolve"):
            validate_plugins(self.root)

    def test_unregistered_plugin_is_rejected(self):
        (self.root / "Jellyfin.Plugins.sln").write_text("", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "solution"):
            validate_plugins(self.root)

    def test_missing_metadata_is_rejected(self):
        another = self.root / "plugins" / "Another"
        another.mkdir()
        (another / "Another.csproj").write_text("<Project />", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "requires plugin.json"):
            validate_plugins(self.root)

    def test_framework_mismatch_is_rejected(self):
        self.metadata["framework"] = "net9.0"
        (self.plugin / "plugin.json").write_text(json.dumps(self.metadata), encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "TargetFramework"):
            validate_plugins(self.root)


if __name__ == "__main__":
    unittest.main()
