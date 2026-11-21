"""Offline regressions for safe plugin scaffolding and pinned runtime dependencies."""

from __future__ import annotations

import contextlib
import io
import json
import pathlib
import shutil
import subprocess
import sys
import tempfile
import unittest
import uuid
import xml.etree.ElementTree as ET
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "tools"))
import new_plugin as scaffolder
import validate_dependencies as dependencies


def snapshot(root):
    """Include empty directories as well as bytes when checking a read-only operation."""
    result = {}
    for path in root.rglob("*"):
        relative = path.relative_to(root).as_posix()
        result[relative] = ("directory", None) if path.is_dir() else ("file", path.read_bytes())
    return result


class PluginScaffolderTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name) / "repository"
        self.root.mkdir()
        (self.root / "Directory.Build.props").write_text(
            "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            encoding="utf-8",
        )
        (self.root / "Directory.Packages.props").write_text(
            '<Project><ItemGroup><PackageVersion Include="Jellyfin.Controller" Version="12.2.0" /></ItemGroup></Project>',
            encoding="utf-8",
        )
        (self.root / "Jellyfin.Plugins.sln").write_text("original solution\n", encoding="utf-8")
        (self.root / "build.yaml").write_text("original build summary\n", encoding="utf-8")
        (self.root / "manifest.json").write_text("[]\n", encoding="utf-8")
        validator = self.root / ".github/scripts/validate_repository.py"
        validator.parent.mkdir(parents=True)
        validator.write_text("# Fixture validator, subprocess invocation is controlled by the test.\n", encoding="utf-8")

    def test_preparation_is_read_only_and_generated_identifiers_agree(self):
        before = snapshot(self.root)
        display_name = 'Playback "Insights" – 音楽'
        files = scaffolder.prepare(self.root, "PlaybackInsights", display_name)
        self.assertEqual(snapshot(self.root), before)
        self.assertEqual(len(files), 7)
        self.assertTrue(all(path.is_relative_to(self.root) for path in files))
        metadata = json.loads(files[self.root / "plugins/PlaybackInsights/plugin.json"])
        plugin = files[self.root / "plugins/PlaybackInsights/Plugin.cs"]
        project = ET.fromstring(files[self.root / metadata["project"]])
        test_project = ET.fromstring(files[self.root / "tests/PlaybackInsights.Tests/PlaybackInsights.Tests.csproj"])
        namespace = "Jellyfin.Plugin.PlaybackInsights"
        self.assertEqual(metadata["slug"], "PlaybackInsights")
        self.assertEqual(metadata["name"], display_name)
        self.assertEqual(uuid.UUID(metadata["guid"]).version, 4)
        self.assertIn(f'Guid.Parse("{metadata["guid"]}")', plugin)
        self.assertIn(f"namespace {namespace};", plugin)
        self.assertIn(f"Name => {json.dumps(display_name)};", plugin)
        self.assertEqual(metadata["assembly"], namespace + ".dll")
        self.assertEqual(metadata["artifacts"], [namespace + ".dll"])
        self.assertEqual(project.findtext(".//AssemblyName"), namespace)
        self.assertEqual(project.findtext(".//RootNamespace"), namespace)
        for property_name in ("AssemblyVersion", "FileVersion", "Version"):
            self.assertEqual(project.findtext(".//" + property_name), metadata["version"])
        self.assertEqual(metadata["targetAbi"], "12.2.0.0")
        self.assertEqual(metadata["framework"], "net10.0")
        self.assertEqual(metadata["runtimeDependencies"], [])
        self.assertEqual(
            test_project.find(".//ProjectReference").get("Include"),
            "../../plugins/PlaybackInsights/PlaybackInsights.csproj",
        )
        for reference in project.findall(".//PackageReference") + test_project.findall(".//PackageReference"):
            self.assertIsNone(reference.get("Version"), "Generated projects must use central versions")
        self.assertNotIn(self.root / "manifest.json", files)
        self.assertFalse(any(path.name.endswith("Tests.cs") for path in files), "Do not generate placeholder success tests")

    def test_separate_preparations_allocate_distinct_plugin_guids(self):
        first = scaffolder.prepare(self.root, "FirstPlugin", "First")
        second = scaffolder.prepare(self.root, "SecondPlugin", "Second")
        self.assertNotEqual(
            json.loads(first[self.root / "plugins/FirstPlugin/plugin.json"])["guid"],
            json.loads(second[self.root / "plugins/SecondPlugin/plugin.json"])["guid"],
        )

    def test_empty_traversal_invalid_and_reserved_slugs_do_not_write(self):
        before = snapshot(self.root)
        for slug in ("", "A", "../Escape", "/Absolute", "Nested/Plugin", "Nested\\Plugin", "Has Space", "With_Underscore", "9Plugin", "Éxample", "A" * 65, "CON", "Nul", "Com1", "LPT9"):
            with self.subTest(slug=slug), self.assertRaises(ValueError):
                scaffolder.prepare(self.root, slug, "Example")
        self.assertEqual(snapshot(self.root), before)

    def test_invalid_display_names_are_rejected_without_writes(self):
        before = snapshot(self.root)
        for name in ("", "   ", "two\nlines", "tab\tname", "carriage\rreturn", "nul\0name"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                scaffolder.prepare(self.root, "Example", name)
        self.assertEqual(snapshot(self.root), before)

    def test_existing_plugin_and_test_names_are_protected_case_insensitively(self):
        for parent_name, existing_name in (("plugins", "example"), ("tests", "EXAMPLE.Tests")):
            with self.subTest(parent=parent_name):
                parent = self.root / parent_name
                parent.mkdir(exist_ok=True)
                existing = parent / existing_name
                existing.write_text("existing work must survive", encoding="utf-8")
                before = snapshot(self.root)
                with self.assertRaisesRegex(ValueError, "overwrite"):
                    scaffolder.prepare(self.root, "Example", "Example")
                self.assertEqual(snapshot(self.root), before)
                existing.unlink()

    def test_source_parents_cannot_be_files_or_external_symlinks(self):
        outside = pathlib.Path(self.directory.name) / "outside"
        outside.mkdir()
        for parent_name in ("plugins", "tests"):
            with self.subTest(parent=parent_name, kind="file"):
                parent = self.root / parent_name
                parent.write_text("not a directory", encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "not a directory"):
                    scaffolder.prepare(self.root, "Example", "Example")
                parent.unlink()
            with self.subTest(parent=parent_name, kind="symlink"):
                parent.symlink_to(outside, target_is_directory=True)
                with self.assertRaisesRegex(ValueError, "external|symlink"):
                    scaffolder.prepare(self.root, "Example", "Example")
                self.assertEqual(list(outside.iterdir()), [])
                parent.unlink()

    def test_cli_dry_run_needs_no_sdk_and_leaves_repository_unchanged(self):
        before = snapshot(self.root)
        output = io.StringIO()
        with patch.object(scaffolder, "ROOT", self.root), patch.object(sys, "argv", ["new_plugin.py", "Example", "--dry-run"]), patch.object(scaffolder.shutil, "which") as which, patch.object(scaffolder.subprocess, "run") as run, contextlib.redirect_stdout(output):
            self.assertEqual(scaffolder.main(), 0)
        which.assert_not_called()
        run.assert_not_called()
        self.assertEqual(snapshot(self.root), before)
        self.assertEqual(len(output.getvalue().splitlines()), 7)
        self.assertIn("plugins/Example/plugin.json", output.getvalue())

    def test_missing_sdk_does_not_create_partial_plugin(self):
        before = snapshot(self.root)
        with patch.object(scaffolder, "ROOT", self.root), patch.object(sys, "argv", ["new_plugin.py", "Example"]), patch.object(scaffolder.shutil, "which", return_value=None), patch.object(scaffolder.subprocess, "run") as run, contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(scaffolder.main(), 1)
        run.assert_not_called()
        self.assertEqual(snapshot(self.root), before)

    def test_failed_solution_or_metadata_command_rolls_back_generated_files(self):
        for failing_command in (1, 2):
            with self.subTest(command=failing_command):
                original_solution = (self.root / "Jellyfin.Plugins.sln").read_bytes()
                original_summary = (self.root / "build.yaml").read_bytes()
                original_catalog = (self.root / "manifest.json").read_bytes()
                calls = []

                def command(args, **kwargs):
                    calls.append(args)
                    self.assertEqual(kwargs["cwd"], self.root)
                    self.assertTrue(kwargs["check"])
                    self.assertTrue((self.root / "plugins/Example/plugin.json").is_file())
                    (self.root / "Jellyfin.Plugins.sln").write_text("partial solution", encoding="utf-8")
                    (self.root / "build.yaml").write_text("partial summary", encoding="utf-8")
                    if len(calls) == failing_command:
                        raise subprocess.CalledProcessError(1, args)
                    return subprocess.CompletedProcess(args, 0)

                with patch.object(scaffolder, "ROOT", self.root), patch.object(sys, "argv", ["new_plugin.py", "Example"]), patch.object(scaffolder.shutil, "which", return_value="/fixture/dotnet"), patch.object(scaffolder.subprocess, "run", side_effect=command), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(scaffolder.main(), 1)
                self.assertEqual(len(calls), failing_command)
                self.assertEqual((self.root / "Jellyfin.Plugins.sln").read_bytes(), original_solution)
                self.assertEqual((self.root / "build.yaml").read_bytes(), original_summary)
                self.assertEqual((self.root / "manifest.json").read_bytes(), original_catalog)
                self.assertFalse((self.root / "plugins/Example").exists())
                self.assertFalse((self.root / "tests/Example.Tests").exists())


@unittest.skipUnless(shutil.which("git"), "Git is needed for isolated gitlink fixtures")
class DependencyValidatorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = pathlib.Path(self.directory.name) / "repository"
        self.root.mkdir()
        self.git("init", "--quiet")
        self.entries = [
            {"id": "vendor.transform", "path": "external/vendor/transform", "repository": "https://example.com/transform.git", "commit": "1" * 40, "jellyfinVersion": "12.2.0", "licenseFile": "LICENSE", "requiredPlugins": []},
            {"id": "vendor.pages", "path": "external/vendor/pages", "repository": "https://example.com/pages.git", "commit": "2" * 40, "jellyfinVersion": "12.2.0", "licenseFile": "LICENSE", "requiredPlugins": ["vendor.transform"]},
        ]
        for entry in self.entries:
            self.git("update-index", "--add", "--cacheinfo", f"160000,{entry['commit']},{entry['path']}")
        self.consumer = {"slug": "Consumer", "targetAbi": "12.2.0.0", "runtimeDependencies": ["vendor.pages"]}
        self.write_consumer()
        self.write_metadata()

    def git(self, *arguments):
        return subprocess.check_output(["git", *arguments], cwd=self.root, text=True, stderr=subprocess.PIPE).strip()

    def write_metadata(self, *, modules=True):
        path = self.root / "dependencies/plugins.json"
        path.parent.mkdir(exist_ok=True)
        path.write_text(json.dumps({"schemaVersion": 1, "plugins": self.entries}), encoding="utf-8")
        if modules:
            self.write_gitmodules([(entry["path"], entry["repository"]) for entry in self.entries])

    def write_gitmodules(self, records):
        contents = "\n".join(f'[submodule "fixture-{index}"]\n\tpath = {path}\n\turl = {url}' for index, (path, url) in enumerate(records))
        (self.root / ".gitmodules").write_text(contents + "\n", encoding="utf-8")

    def write_consumer(self):
        path = self.root / "plugins/Consumer/plugin.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(self.consumer), encoding="utf-8")

    def validate(self, initialized=False):
        with contextlib.redirect_stdout(io.StringIO()):
            return dependencies.validate(self.root, initialized=initialized)

    def test_offline_validation_accepts_exact_gitlinks_without_upstream_checkouts(self):
        self.assertFalse((self.root / "external").exists())
        self.assertEqual(self.validate(), 0)

    def test_changed_pinned_commit_is_rejected_against_real_git_index(self):
        self.entries[0]["commit"] = "3" * 40
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "Gitlink does not match"):
            self.validate()

    def test_regular_file_cannot_substitute_for_a_submodule(self):
        entry = self.entries[0]
        self.git("update-index", "--force-remove", "--", entry["path"])
        file = self.root / entry["path"]
        file.parent.mkdir(parents=True)
        file.write_text("not a gitlink", encoding="utf-8")
        self.git("add", "--", entry["path"])
        with self.assertRaisesRegex(ValueError, "Gitlink does not match"):
            self.validate()

    def test_empty_traversal_absolute_glob_and_noncanonical_paths_are_rejected(self):
        original = self.entries[0]["path"]
        for path in ("", ".", "external", "../outside", "/external/vendor", "external/../outside", "external\\vendor", "external/vendor/..\\outside", "external/*", "external/[vendor]", "external//vendor/transform", "external/./vendor/transform", "external/vendor/transform/"):
            with self.subTest(path=path):
                self.entries[0]["path"] = path
                self.write_metadata()
                with self.assertRaisesRegex(ValueError, "Unsafe dependency path"):
                    self.validate()
        self.entries[0]["path"] = original

    def test_duplicate_gitmodule_paths_are_rejected(self):
        entry = self.entries[0]
        self.write_gitmodules([(entry["path"], entry["repository"]), (entry["path"], entry["repository"])])
        with self.assertRaisesRegex(ValueError, "Duplicate .gitmodules path"):
            self.validate()

    def test_duplicate_dependency_ids_are_rejected(self):
        self.entries[1]["id"] = self.entries[0]["id"]
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "Duplicate dependency ID"):
            self.validate()

    def test_duplicate_dependency_paths_are_case_insensitively_rejected(self):
        self.entries[1]["path"] = "external/vendor/TRANSFORM"
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "Duplicate dependency path"):
            self.validate()

    def test_repository_url_must_match_gitmodules(self):
        self.entries[0]["repository"] = "https://example.com/different-project.git"
        self.write_metadata(modules=False)
        with self.assertRaisesRegex(ValueError, "Submodule URL mismatch"):
            self.validate()

    def test_unlisted_submodule_is_rejected(self):
        records = [(entry["path"], entry["repository"]) for entry in self.entries]
        records.append(("external/vendor/unlisted", "https://example.com/unlisted.git"))
        self.write_gitmodules(records)
        with self.assertRaisesRegex(ValueError, "Every submodule must have dependency metadata"):
            self.validate()

    def test_required_dependency_server_abi_must_match(self):
        self.entries[1]["jellyfinVersion"] = "12.1.0"
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "Incompatible runtime dependency versions"):
            self.validate()

    def test_consumer_abi_must_match_runtime_dependency(self):
        self.consumer["targetAbi"] = "12.1.0.0"
        self.write_consumer()
        with self.assertRaisesRegex(ValueError, "Runtime dependency ABI mismatch"):
            self.validate()

    def test_required_dependency_cycle_is_rejected(self):
        self.entries[0]["requiredPlugins"] = [self.entries[1]["id"]]
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "Cyclic runtime dependency"):
            self.validate()

    def test_unknown_required_dependency_is_rejected(self):
        self.entries[1]["requiredPlugins"] = ["vendor.unknown"]
        self.write_metadata()
        with self.assertRaisesRegex(ValueError, "Unknown dependency"):
            self.validate()

    def test_unknown_consumer_runtime_dependency_is_rejected(self):
        self.consumer["runtimeDependencies"] = ["vendor.unknown"]
        self.write_consumer()
        with self.assertRaisesRegex(ValueError, "Unknown runtime dependency"):
            self.validate()

    def test_initialized_checkout_must_match_pinned_commit(self):
        actual_git = dependencies.git

        def git(root, *arguments):
            if "ls-files" in arguments:
                return actual_git(root, *arguments)
            if arguments == ("rev-parse", "HEAD"):
                return "9" * 40
            self.fail(f"Unexpected Git operation: {arguments}")

        with patch.object(dependencies, "git", side_effect=git), self.assertRaisesRegex(ValueError, "checkout is not pinned"):
            self.validate(initialized=True)

    def test_initialized_checkout_must_be_clean_and_have_a_license(self):
        actual_git = dependencies.git
        by_path = {self.root / entry["path"]: entry for entry in self.entries}
        for checkout in by_path:
            checkout.mkdir(parents=True)
            (checkout / "LICENSE").write_text("Fixture license", encoding="utf-8")
        dirty = False

        def git(root, *arguments):
            if "ls-files" in arguments:
                return actual_git(root, *arguments)
            if arguments == ("rev-parse", "HEAD"):
                return by_path[root]["commit"]
            if arguments == ("status", "--porcelain"):
                return " M source.cs" if dirty else ""
            self.fail(f"Unexpected Git operation: {arguments}")

        with patch.object(dependencies, "git", side_effect=git):
            self.assertEqual(self.validate(initialized=True), 0)
            dirty = True
            with self.assertRaisesRegex(ValueError, "local changes"):
                self.validate(initialized=True)
            dirty = False
            first_license = self.root / self.entries[0]["path"] / "LICENSE"
            first_license.unlink()
            with self.assertRaisesRegex(ValueError, "license missing"):
                self.validate(initialized=True)

    def test_initialized_checkout_cannot_resolve_outside_repository(self):
        outside = pathlib.Path(self.directory.name) / "outside"
        outside.mkdir()
        (self.root / "external").symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "outside the repository"):
            self.validate(initialized=True)


if __name__ == "__main__":
    unittest.main()
