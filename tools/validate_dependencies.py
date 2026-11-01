#!/usr/bin/env python3
"""Validate runtime dependency metadata and exact Git submodule pins, offline."""

from __future__ import annotations

import argparse
import configparser
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]


def git(root: pathlib.Path, *args: str) -> str:
    return subprocess.check_output(["git", *args], cwd=root, text=True).strip()


def validate(root: pathlib.Path, initialized: bool = False) -> int:
    document = json.loads((root / "dependencies/plugins.json").read_text())
    if document.get("schemaVersion") != 1:
        raise ValueError("Unsupported dependency metadata schema")
    modules = configparser.RawConfigParser()
    modules.read(root / ".gitmodules")
    configured: dict[str, str] = {}
    for section in modules.sections():
        path = modules.get(section, "path")
        if path in configured:
            raise ValueError(f"Duplicate .gitmodules path: {path}")
        configured[path] = modules.get(section, "url")
    dependencies = document["plugins"]
    by_id = {entry["id"]: entry for entry in dependencies}
    if len(by_id) != len(dependencies):
        raise ValueError("Duplicate dependency ID")
    paths: set[str] = set()
    for entry in dependencies:
        path = pathlib.PurePosixPath(entry["path"])
        if not path.parts or path.is_absolute() or ".." in path.parts or path.parts[0] != "external":
            raise ValueError(f"Unsafe dependency path: {path}")
        if entry["path"] in paths:
            raise ValueError(f"Duplicate dependency path: {path}")
        paths.add(entry["path"])
        if configured.get(entry["path"]) != entry["repository"]:
            raise ValueError(f"Submodule URL mismatch: {path}")
        if not re.fullmatch(r"[0-9a-f]{40}", entry["commit"]):
            raise ValueError(f"Invalid pinned commit: {path}")
        if not re.fullmatch(r"\d+\.\d+\.\d+", entry["jellyfinVersion"]):
            raise ValueError(f"Invalid Jellyfin version: {path}")
        staged = git(root, "ls-files", "--stage", "--", str(path)).split()
        if len(staged) < 4 or staged[:3] != ["160000", entry["commit"], "0"]:
            raise ValueError(f"Gitlink does not match dependency metadata: {path}")
        for required in entry.get("requiredPlugins", []):
            if required not in by_id:
                raise ValueError(f"Unknown dependency {required} required by {entry['id']}")
            if by_id[required]["jellyfinVersion"] != entry["jellyfinVersion"]:
                raise ValueError(f"Incompatible runtime dependency versions: {entry['id']} and {required}")
        if initialized:
            checkout = root / path
            if git(checkout, "rev-parse", "HEAD") != entry["commit"]:
                raise ValueError(f"Submodule checkout is not pinned: {path}")
            if git(checkout, "status", "--porcelain"):
                raise ValueError(f"Submodule has local changes: {path}")
            license_path = pathlib.PurePosixPath(entry["licenseFile"])
            if license_path.is_absolute() or ".." in license_path.parts:
                raise ValueError(f"Unsafe license path: {license_path}")
            if not (checkout / license_path).is_file():
                raise ValueError(f"Upstream license missing: {path}")
    if paths != set(configured):
        raise ValueError("Every submodule must have dependency metadata")

    def visit(dependency_id: str, active: set[str], complete: set[str]) -> None:
        if dependency_id in active:
            raise ValueError(f"Cyclic runtime dependency: {dependency_id}")
        if dependency_id in complete:
            return
        active.add(dependency_id)
        for required in by_id[dependency_id].get("requiredPlugins", []):
            visit(required, active, complete)
        active.remove(dependency_id)
        complete.add(dependency_id)

    complete: set[str] = set()
    for dependency_id in by_id:
        visit(dependency_id, set(), complete)
    count = 0
    for metadata_path in sorted((root / "plugins").glob("*/plugin.json")):
        metadata = json.loads(metadata_path.read_text())
        for dependency_id in metadata.get("runtimeDependencies", []):
            if dependency_id not in by_id:
                raise ValueError(f"Unknown runtime dependency in {metadata_path}: {dependency_id}")
            # Dependency assets must match the plugin's declared server family/version.
            version = by_id[dependency_id]["jellyfinVersion"]
            if metadata["targetAbi"].split(".")[:3] != version.split(".")[:3]:
                raise ValueError(f"Runtime dependency ABI mismatch for {metadata['slug']}: {dependency_id}")
        count += 1
    print(f"Validated {len(dependencies)} pinned submodules and {count} plugin dependency declarations")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--initialized", action="store_true", help="Also verify local upstream checkouts and licenses")
    args = parser.parse_args()
    try:
        return validate(ROOT, args.initialized)
    except (KeyError, ValueError, OSError, configparser.Error, subprocess.CalledProcessError) as exc:
        print(f"Dependency validation failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
