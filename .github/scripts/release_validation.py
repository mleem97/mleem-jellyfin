#!/usr/bin/env python3
"""Validation helpers for Jellyfin plugin release packages and manifests."""

from __future__ import annotations

import hashlib
import json
import pathlib
import re
import tempfile
import zipfile

import auto_publish_plugins as publisher

SHA256_PATTERN = re.compile(r"^[A-F0-9]{64}$")
LEGACY_MD5_PATTERN = re.compile(r"^[A-F0-9]{32}$")
LEGACY_CHECKSUM_ALLOWLIST = {
    ("f8d74b1c-3c97-4481-a3b3-6eb622d6ad58", "0.1.0.1"),
}


def sha256_upper(path: pathlib.Path) -> str:
    """Return the uppercase SHA-256 digest for a file."""
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def expected_archive_names(metadata: dict) -> set[str]:
    """Return the exact set of permitted package file names."""
    artifacts = metadata.get("artifacts") or [metadata["assembly"]]
    expected = {pathlib.PurePath(item).name for item in artifacts}
    pdb_name = pathlib.PurePath(metadata["assembly"]).with_suffix(".pdb").name
    expected.add(pdb_name)
    return expected


def validate_package(metadata: dict, zip_path: pathlib.Path) -> None:
    """Validate that a package is non-empty, safe and contains all artifacts."""
    if not zip_path.is_file() or zip_path.stat().st_size <= 0:
        raise SystemExit(f"Release package is missing or empty: {zip_path}")

    required = {
        pathlib.PurePath(item).name
        for item in (metadata.get("artifacts") or [metadata["assembly"]])
    }
    permitted = expected_archive_names(metadata)

    with zipfile.ZipFile(zip_path, "r") as archive:
        members = archive.infolist()
        if not members:
            raise SystemExit(f"Release package contains no files: {zip_path}")

        names: list[str] = []
        for member in members:
            pure_name = pathlib.PurePosixPath(member.filename)
            if member.is_dir():
                raise SystemExit(f"Release package contains a directory: {member.filename}")
            if pure_name.is_absolute() or ".." in pure_name.parts or len(pure_name.parts) != 1:
                raise SystemExit(f"Unsafe package path: {member.filename}")
            if member.file_size <= 0:
                raise SystemExit(f"Release artifact is empty: {member.filename}")
            names.append(pure_name.name)

        if len(names) != len(set(names)):
            raise SystemExit("Release package contains duplicate file names.")

        actual = set(names)
        missing = required - actual
        unexpected = actual - permitted
        if missing:
            raise SystemExit(
                "Release package is missing required artifacts: "
                + ", ".join(sorted(missing))
            )
        if unexpected:
            raise SystemExit(
                "Release package contains unexpected files: "
                + ", ".join(sorted(unexpected))
            )


def validate_manifest(
    manifest_path: pathlib.Path,
    expected_guid: str,
    expected_version: str,
    expected_zip: pathlib.Path,
) -> None:
    """Validate manifest structure and the checksum for the pending release."""
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(manifest, list):
        raise SystemExit("manifest.json must contain a top-level array.")

    seen_guids: set[str] = set()
    pending_version: dict | None = None
    for plugin in manifest:
        guid = plugin.get("guid")
        if not isinstance(guid, str) or not guid:
            raise SystemExit("Every manifest entry requires a non-empty guid.")
        if guid in seen_guids:
            raise SystemExit(f"Duplicate plugin guid in manifest: {guid}")
        seen_guids.add(guid)

        versions = plugin.get("versions") or []
        if not isinstance(versions, list):
            raise SystemExit(f"Manifest versions must be an array for {guid}.")

        seen_versions: set[str] = set()
        for version_entry in versions:
            version = version_entry.get("version")
            checksum = version_entry.get("checksum")
            source_url = version_entry.get("sourceUrl")
            if not isinstance(version, str) or not version:
                raise SystemExit(f"Manifest version is missing for {guid}.")
            if version in seen_versions:
                raise SystemExit(f"Duplicate manifest version {version} for {guid}.")
            seen_versions.add(version)
            if not isinstance(source_url, str) or not source_url.startswith("https://"):
                raise SystemExit(f"Invalid sourceUrl for {guid} {version}.")
            if not isinstance(checksum, str):
                raise SystemExit(f"Missing checksum for {guid} {version}.")

            normalized = checksum.upper()
            if not SHA256_PATTERN.fullmatch(normalized):
                legacy_key = (guid, version)
                if not (
                    legacy_key in LEGACY_CHECKSUM_ALLOWLIST
                    and LEGACY_MD5_PATTERN.fullmatch(normalized)
                ):
                    raise SystemExit(
                        f"Checksum for {guid} {version} is not a SHA-256 digest."
                    )

            if guid == expected_guid and version == expected_version:
                pending_version = version_entry

    if pending_version is None:
        raise SystemExit(
            f"Pending manifest version not found: {expected_guid} {expected_version}"
        )

    expected_checksum = sha256_upper(expected_zip)
    actual_checksum = str(pending_version.get("checksum", "")).upper()
    if actual_checksum != expected_checksum:
        raise SystemExit(
            "Manifest checksum does not match the generated release package."
        )
    if not str(pending_version.get("sourceUrl", "")).endswith(expected_zip.name):
        raise SystemExit("Manifest sourceUrl does not reference the generated package name.")


def verify_published_asset(metadata: dict, version: str, local_zip: pathlib.Path) -> None:
    """Download the published asset and compare it byte-for-byte by SHA-256."""
    tag_name = f"{metadata['slug']}-v{version}"
    with tempfile.TemporaryDirectory(prefix="jellyfin-release-") as directory:
        publisher.run(
            [
                "gh",
                "release",
                "download",
                tag_name,
                "--pattern",
                local_zip.name,
                "--dir",
                directory,
                "--clobber",
            ]
        )
        downloaded = pathlib.Path(directory) / local_zip.name
        if not downloaded.is_file():
            raise SystemExit(f"Published release asset was not downloaded: {local_zip.name}")
        if sha256_upper(downloaded) != sha256_upper(local_zip):
            raise SystemExit(
                f"Published release asset checksum differs from local package: {local_zip.name}"
            )
