#!/usr/bin/env python3
"""Validation helpers for Jellyfin plugin release packages and manifests."""

from __future__ import annotations

import hashlib
import json
import pathlib
import re
import tempfile
import uuid
import zipfile
from urllib.parse import urlsplit

import auto_publish_plugins as publisher

MD5_PATTERN = re.compile(r"^[A-F0-9]{32}$")
VERSION_PATTERN = re.compile(r"^\d+\.\d+\.\d+\.\d+$")


def sha256(path: pathlib.Path) -> str:
    """Return the SHA-256 digest used for published-artifact verification."""
    return hashlib.sha256(path.read_bytes()).hexdigest()


def md5_upper(path: pathlib.Path) -> str:
    """Return the uppercase MD5 digest for a file (required by Jellyfin server)."""
    return hashlib.md5(path.read_bytes()).hexdigest().upper()


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
            if (
                pure_name.is_absolute()
                or ".." in pure_name.parts
                or len(pure_name.parts) != 1
                or "\\" in member.filename
                or ":" in member.filename
                or ((member.external_attr >> 16) & 0o170000) == 0o120000
            ):
                raise SystemExit(f"Unsafe package path: {member.filename}")
            if member.file_size <= 0:
                raise SystemExit(f"Release artifact is empty: {member.filename}")
            names.append(pure_name.name)

        if len(names) != len({name.casefold() for name in names}):
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
        if archive.testzip() is not None:
            raise SystemExit("Release package contains a corrupt artifact.")


def validate_manifest_structure(manifest_path: pathlib.Path) -> list[dict]:
    """Validate catalog entries, allowing distinct builds of one version per ABI."""
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if not isinstance(manifest, list):
        raise SystemExit("manifest.json must contain a top-level array.")

    seen_guids: set[str] = set()
    for plugin in manifest:
        guid = plugin.get("guid")
        if not isinstance(guid, str) or not guid:
            raise SystemExit("Every manifest entry requires a non-empty guid.")
        try:
            normalized_guid = str(uuid.UUID(guid))
        except ValueError as error:
            raise SystemExit(f"Invalid plugin guid: {guid}") from error
        if normalized_guid in seen_guids:
            raise SystemExit(f"Duplicate plugin guid in manifest: {guid}")
        seen_guids.add(normalized_guid)

        versions = plugin.get("versions") or []
        if not isinstance(versions, list):
            raise SystemExit(f"Manifest versions must be an array for {guid}.")

        seen_versions: set[tuple[str, tuple[int, ...]]] = set()
        for version_entry in versions:
            version = version_entry.get("version")
            checksum = version_entry.get("checksum")
            source_url = version_entry.get("sourceUrl")
            target_abi = version_entry.get("targetAbi")
            if not isinstance(version, str) or not VERSION_PATTERN.fullmatch(version):
                raise SystemExit(f"Manifest version is missing for {guid}.")
            if not isinstance(target_abi, str) or not re.fullmatch(r"\d+(?:\.\d+){1,3}", target_abi):
                raise SystemExit(f"Invalid targetAbi for {guid} {version}.")
            abi_parts = tuple(int(part) for part in target_abi.split("."))
            version_key = (version, abi_parts + (0,) * (4 - len(abi_parts)))
            if version_key in seen_versions:
                raise SystemExit(f"Duplicate manifest version/ABI {version_key} for {guid}.")
            seen_versions.add(version_key)
            if not isinstance(source_url, str):
                raise SystemExit(f"Invalid sourceUrl for {guid} {version}.")
            parsed_url = urlsplit(source_url)
            if parsed_url.scheme != "https" or not parsed_url.hostname or parsed_url.username or parsed_url.password:
                raise SystemExit(f"Invalid sourceUrl for {guid} {version}.")
            if not isinstance(checksum, str):
                raise SystemExit(f"Missing checksum for {guid} {version}.")

            normalized = checksum.upper()
            if not MD5_PATTERN.fullmatch(normalized):
                raise SystemExit(f"Checksum for {guid} {version} is not an MD5 digest.")

    return manifest


def validate_manifest(
    manifest_path: pathlib.Path,
    expected_guid: str,
    expected_version: str,
    expected_zip: pathlib.Path,
    expected_target_abi: str | None = None,
) -> None:
    """Validate the catalog and exactly identify the pending package by ABI."""
    manifest = validate_manifest_structure(manifest_path)
    matching = [
        version
        for plugin in manifest
        if plugin["guid"].casefold() == expected_guid.casefold()
        for version in plugin.get("versions", [])
        if version["version"] == expected_version
        and (expected_target_abi is None or version["targetAbi"] == expected_target_abi)
    ]

    if len(matching) != 1:
        raise SystemExit(
            f"Pending manifest version must identify one build: {expected_guid} {expected_version} {expected_target_abi}"
        )
    pending_version = matching[0]

    expected_checksum = md5_upper(expected_zip)
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
        if sha256(downloaded) != sha256(local_zip):
            raise SystemExit(
                f"Published release asset SHA-256 differs from local package: {local_zip.name}"
            )
        validate_package(metadata, downloaded)
