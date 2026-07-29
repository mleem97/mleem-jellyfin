#!/usr/bin/env python3
"""Publish Jellyfin plugins with optional exact version selection.

This wrapper reuses the shared release implementation from
``auto_publish_plugins.py`` while adding validation for an explicit four-part
release version supplied through ``RELEASE_VERSION``/``INPUT_RELEASE_VERSION``.
"""

from __future__ import annotations

import os
import pathlib
import re
import sys

import auto_publish_plugins as publisher
import release_validation as validator

VERSION_PATTERN = re.compile(r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")


def requested_release_version() -> str | None:
    """Return a validated exact release version, when configured."""
    value = (
        os.environ.get("RELEASE_VERSION")
        or os.environ.get("INPUT_RELEASE_VERSION")
        or ""
    ).strip()
    if not value:
        return None
    if not VERSION_PATTERN.fullmatch(value):
        raise SystemExit(
            "Invalid RELEASE_VERSION. Expected four numeric components, "
            "for example 0.1.0.0."
        )
    return value


def ensure_version_available(metadata: dict, version: str) -> None:
    """Reject an exact version that already exists as a tag or manifest entry."""
    tag_name = f"{metadata['slug']}-v{version}"
    existing_tags = publisher.run(
        ["git", "tag", "--list", tag_name],
        capture=True,
    ).splitlines()
    if existing_tags:
        raise SystemExit(f"Release tag already exists: {tag_name}")

    manifest = publisher.load_json(publisher.MANIFEST_PATH)
    entry = next(
        (item for item in manifest if item.get("guid") == metadata["guid"]),
        None,
    )
    versions = (entry or {}).get("versions") or []
    if any(item.get("version") == version for item in versions):
        raise SystemExit(
            f"Manifest already contains {metadata['slug']} version {version}."
        )


def resolve_version(metadata: dict, changelog: str, exact_version: str | None) -> str:
    """Choose either the requested exact version or the normal automatic bump."""
    if exact_version is not None:
        ensure_version_available(metadata, exact_version)
        return exact_version

    level = publisher.bump_level(changelog)
    return publisher.bump_version(metadata["version"], level)


def main() -> int:
    """Build, validate, package, publish and record all selected plugins."""
    slugs = publisher.selected_plugins()
    if not slugs:
        print("No changed plugin projects detected.")
        return 0

    known = publisher.discover_plugins()
    for slug in slugs:
        if slug not in known:
            raise SystemExit(f"Unknown plugin slug: {slug}")

    exact_version = requested_release_version()
    if exact_version is not None and len(slugs) != 1:
        raise SystemExit(
            "RELEASE_VERSION can only be used when exactly one plugin is selected."
        )

    release_data: list[tuple[dict, str, str, pathlib.Path]] = []
    for slug in slugs:
        metadata_path = publisher.PLUGINS_DIR / slug / "plugin.json"
        metadata = publisher.load_json(metadata_path)
        changelog = publisher.changelog_for(slug, publisher.latest_tag(slug))
        new_version = resolve_version(metadata, changelog, exact_version)
        metadata["version"] = new_version
        publisher.save_json(metadata_path, metadata)

        project = publisher.ROOT / metadata["project"]
        publisher.update_csproj(project, new_version)
        publisher.build_plugin(project)
        zip_path = publisher.package_plugin(metadata, new_version)
        validator.validate_package(metadata, zip_path)
        publisher.update_manifest(metadata, new_version, changelog, zip_path)
        validator.validate_manifest(
            publisher.MANIFEST_PATH,
            metadata["guid"],
            new_version,
            zip_path,
        )
        release_data.append((metadata, new_version, changelog, zip_path))

    publisher.run(["git", "config", "user.name", "github-actions[bot]"])
    publisher.run(
        [
            "git",
            "config",
            "user.email",
            "41898282+github-actions[bot]@users.noreply.github.com",
        ]
    )
    publisher.commit_and_push(slugs)

    for metadata, version, changelog, zip_path in release_data:
        publisher.release_with_gh(metadata, version, changelog, zip_path)
        validator.verify_published_asset(metadata, version, zip_path)
    publisher.run(["git", "push", "origin", "main", "--follow-tags"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
