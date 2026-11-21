#!/usr/bin/env python3
"""Validate plugin metadata and discover the CI matrix without a second plugin list."""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import uuid
import xml.etree.ElementTree as ET

import release_validation


def validate_plugins(root: pathlib.Path) -> list[str]:
    """Require every plugin to have consistent metadata and solution registration."""
    solution = (root / "Jellyfin.Plugins.sln").read_text(encoding="utf-8-sig").replace("\\", "/")
    central_versions: dict[str, str] = {}
    central_file = root / "Directory.Packages.props"
    if central_file.is_file():
        central_versions = {
            reference.get("Include", reference.get("Update", "")): reference.get("Version", "")
            for reference in ET.parse(central_file).findall(".//PackageVersion")
        }
    shared_properties: dict[str, str | None] = {}
    build_properties = root / "Directory.Build.props"
    if build_properties.is_file():
        shared_properties = {
            element.tag: element.text
            for group in ET.parse(build_properties).getroot().findall("PropertyGroup")
            for element in group
            if not element.get("Condition")
        }
    slugs: list[str] = []
    guids: set[uuid.UUID] = set()
    for metadata_path in sorted((root / "plugins").glob("*/plugin.json")):
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        slug = metadata_path.parent.name
        if metadata.get("slug") != slug or not re.fullmatch(r"[A-Za-z][A-Za-z0-9]*", slug):
            raise SystemExit(f"Plugin folder/slug mismatch: {metadata_path}")
        guid = uuid.UUID(metadata["guid"])
        if guid in guids:
            raise SystemExit(f"Duplicate plugin GUID: {guid}")
        guids.add(guid)
        version = metadata.get("version", "")
        target_abi = metadata.get("targetAbi", "")
        if not release_validation.VERSION_PATTERN.fullmatch(version):
            raise SystemExit(f"{slug}: version must have four numeric components")
        if not release_validation.VERSION_PATTERN.fullmatch(target_abi):
            raise SystemExit(f"{slug}: targetAbi must have four numeric components")
        expected_project = f"plugins/{slug}/{slug}.csproj"
        if metadata.get("project") != expected_project or expected_project not in solution:
            raise SystemExit(f"{slug}: project must exist in its plugin folder and solution")
        project = ET.parse(root / expected_project).getroot()
        properties = dict(shared_properties)
        properties.update({element.tag: element.text for group in project.findall("PropertyGroup") for element in group})
        for property_name in ("AssemblyVersion", "FileVersion", "Version"):
            if properties.get(property_name) != version:
                raise SystemExit(f"{slug}: {property_name} differs from plugin.json version")
        if properties.get("TargetFramework") != metadata.get("framework"):
            raise SystemExit(f"{slug}: TargetFramework differs from plugin.json framework")
        if properties.get("AssemblyName", "") + ".dll" != metadata.get("assembly"):
            raise SystemExit(f"{slug}: AssemblyName differs from plugin.json assembly")
        artifacts = metadata.get("artifacts") or [metadata["assembly"]]
        if metadata["assembly"] not in artifacts:
            raise SystemExit(f"{slug}: artifact list must contain the plugin assembly")
        if len(artifacts) != len({name.casefold() for name in artifacts}):
            raise SystemExit(f"{slug}: duplicate artifact names")
        for name in artifacts:
            if not re.fullmatch(r"[A-Za-z0-9_.-]+\.dll", name):
                raise SystemExit(f"{slug}: unsafe artifact name {name!r}")
        abi = tuple(int(part) for part in target_abi.split("."))
        for reference in project.findall(".//PackageReference"):
            if reference.get("Include") not in {"Jellyfin.Common", "Jellyfin.Controller", "Jellyfin.Model"}:
                continue
            package_version = reference.get("Version") or central_versions.get(reference.get("Include", ""), "")
            if not re.fullmatch(r"\d+\.\d+\.\d+", package_version):
                raise SystemExit(f"{slug}: cannot resolve a pinned version for {reference.get('Include')}")
            minimum_abi = tuple(int(part) for part in package_version.split(".")) + (0,)
            if abi < minimum_abi:
                raise SystemExit(f"{slug}: targetAbi is older than referenced {reference.get('Include')} {package_version}")
        slugs.append(slug)
    if not slugs:
        raise SystemExit("No plugins discovered.")
    projects = {path.parent.name for path in (root / "plugins").glob("*/*.csproj")}
    if projects != set(slugs):
        raise SystemExit("Every plugin project requires plugin.json metadata.")
    return slugs


def build_summary(root: pathlib.Path, slugs: list[str]) -> str:
    """Generate the legacy build.yaml index from the authoritative plugin metadata."""
    lines = [
        "# Generated from plugins/*/plugin.json. Run: python .github/scripts/validate_repository.py --sync-build",
        "---",
        'repository: "mleem Jellyfin Plugins"',
        'branch: "main"',
        'layout: "plugin-library"',
        'manifest: "manifest.json"',
        "plugins:",
    ]
    for slug in slugs:
        metadata_path = pathlib.PurePosixPath("plugins") / slug / "plugin.json"
        metadata = json.loads((root / metadata_path).read_text(encoding="utf-8"))
        values = {
            "slug": slug,
            "folder": str(metadata_path.parent),
            "metadata": str(metadata_path),
            "project": metadata["project"],
            "packagePrefix": metadata["packagePrefix"],
            "guid": metadata["guid"],
            "targetAbi": metadata["targetAbi"],
            "framework": metadata["framework"],
        }
        for index, (key, value) in enumerate(values.items()):
            prefix = "  - " if index == 0 else "    "
            lines.append(f"{prefix}{key}: {json.dumps(value, ensure_ascii=False)}")
    return "\n".join(lines) + "\n"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix", action="store_true", help="Print the compact GitHub Actions build matrix")
    parser.add_argument("--sync-build", action="store_true", help="Regenerate build.yaml from plugin.json metadata")
    args = parser.parse_args()
    root = pathlib.Path.cwd()
    slugs = validate_plugins(root)
    release_validation.validate_manifest_structure(root / "manifest.json")
    summary = build_summary(root, slugs)
    build_path = root / "build.yaml"
    if args.sync_build:
        build_path.write_text(summary, encoding="utf-8")
    elif not build_path.is_file() or build_path.read_text(encoding="utf-8") != summary:
        raise SystemExit("build.yaml differs from plugin metadata. Run: python .github/scripts/validate_repository.py --sync-build")
    if args.matrix:
        print(json.dumps({"plugin": slugs}, separators=(",", ":")))
    else:
        print(f"Validated {len(slugs)} plugin projects and the repository catalog.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
