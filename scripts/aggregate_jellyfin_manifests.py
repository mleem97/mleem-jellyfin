#!/usr/bin/env python3
"""Build a proposed Jellyfin manifest from an owned baseline and allowlisted feeds.

This script NEVER modifies the live manifest or publishes releases.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import re
import urllib.error
import urllib.parse
import urllib.request
from collections import OrderedDict

MAX_BYTES = 4 * 1024 * 1024
MAX_ENTRIES = 1000
MAX_VERSIONS = 100
GUID_RE = re.compile(r"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$")
VERSION_RE = re.compile(r"^\d+(\.\d+){2,3}$")
CHECKSUM_RE = re.compile(r"^[0-9a-fA-F]{32}$")


class ManifestError(ValueError):
    pass


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ManifestError("Redirects require separate review")


def checked_https(url: str) -> str:
    if not isinstance(url, str):
        raise ManifestError("URL must be a string")
    parsed = urllib.parse.urlsplit(url)
    if (parsed.scheme != "https" or not parsed.hostname or parsed.username or
            parsed.password or parsed.port not in (None, 443) or parsed.fragment):
        raise ManifestError("Only direct HTTPS URLs without credentials or fragments are allowed")
    if parsed.hostname in {"localhost"} or parsed.hostname.endswith(".local"):
        raise ManifestError("Local hosts are prohibited")
    # Only explicitly trusted upstream publication hosts. Add new hosts after review.
    if parsed.hostname not in {"raw.githubusercontent.com", "www.iamparadox.dev"}:
        raise ManifestError(f"Host not allowed: {parsed.hostname}")
    return url


def fetch_json(url: str):
    checked_https(url)
    request = urllib.request.Request(url, headers={"User-Agent": "mleem-jellyfin-manifest-aggregator/1"})
    opener = urllib.request.build_opener(NoRedirect())
    with opener.open(request, timeout=15) as response:
        if response.status != 200:
            raise ManifestError(f"Upstream returned HTTP {response.status}")
        data = response.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        raise ManifestError("Upstream manifest is too large")
    return json.loads(data.decode("utf-8"))


def validate_manifest(data, source: str):
    if not isinstance(data, list) or len(data) > MAX_ENTRIES:
        raise ManifestError(f"{source}: expected bounded plugin array")
    seen = set()
    for plugin in data:
        if not isinstance(plugin, dict):
            raise ManifestError(f"{source}: plugin must be object")
        guid = plugin.get("guid", "")
        if not isinstance(guid, str) or not GUID_RE.fullmatch(guid):
            raise ManifestError(f"{source}: invalid plugin GUID")
        key = guid.lower()
        if key in seen:
            raise ManifestError(f"{source}: duplicate GUID {guid}")
        seen.add(key)
        if not isinstance(plugin.get("name"), str) or not plugin["name"].strip():
            raise ManifestError(f"{source}: plugin name missing")
        versions = plugin.get("versions")
        if not isinstance(versions, list) or len(versions) > MAX_VERSIONS:
            raise ManifestError(f"{source}: invalid versions for {guid}")
        version_keys = set()
        for version in versions:
            if not isinstance(version, dict):
                raise ManifestError(f"{source}: invalid version object for {guid}")
            name, abi = version.get("version"), version.get("targetAbi")
            if not isinstance(name, str) or not VERSION_RE.fullmatch(name):
                raise ManifestError(f"{source}: invalid version for {guid}")
            if not isinstance(abi, str) or not VERSION_RE.fullmatch(abi):
                raise ManifestError(f"{source}: invalid targetAbi for {guid}")
            identity = (name, abi)
            if identity in version_keys:
                raise ManifestError(f"{source}: duplicate version/ABI for {guid}")
            version_keys.add(identity)
            checked_https(version.get("sourceUrl", ""))
            checksum = version.get("checksum", "")
            if not isinstance(checksum, str) or not CHECKSUM_RE.fullmatch(checksum):
                raise ManifestError(f"{source}: invalid checksum for {guid} {name}")
    return data


def merge_manifests(base, sources):
    """Owned/base entries take precedence; no rewriting of third-party package data."""
    result = OrderedDict((p["guid"].lower(), p) for p in validate_manifest(base, "baseline"))
    origins = {key: "baseline" for key in result}
    conflicts = []
    for source, entries in sources:
        for plugin in validate_manifest(entries, source):
            key = plugin["guid"].lower()
            if key in result:
                if result[key] != plugin:
                    conflicts.append({"guid": key, "kept": origins[key], "skipped": source})
                continue
            result[key] = plugin
            origins[key] = source
    return list(result.values()), {"origins": origins, "conflicts": conflicts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", default="manifest.json")
    parser.add_argument("--sources", default="configs/repositories.json")
    parser.add_argument("--output", default="build/aggregated-manifest.json")
    parser.add_argument("--report", default="build/manifest-aggregation-report.json")
    parser.add_argument("--offline", action="store_true", help="Use only baseline; validates aggregation without network")
    args = parser.parse_args()
    base = json.loads(pathlib.Path(args.baseline).read_text(encoding="utf-8"))
    feeds = json.loads(pathlib.Path(args.sources).read_text(encoding="utf-8"))
    if not isinstance(feeds, list):
        raise ManifestError("Sources config must be an array")
    collected, seen_urls = [], set()
    if not args.offline:
        for feed in feeds:
            if not isinstance(feed, dict) or not isinstance(feed.get("url"), str):
                raise ManifestError("Invalid feed specification")
            url = checked_https(feed["url"])
            if url in seen_urls:
                continue
            seen_urls.add(url)
            # This URL already publishes the owned baseline, not a third-party source.
            if url.endswith("/mleem97/mleem-jellyfin/main/manifest.json"):
                continue
            collected.append((url, fetch_json(url)))
    merged, report = merge_manifests(base, collected)
    report["feeds_considered"] = [name for name, _ in collected]
    report["entry_count"] = len(merged)
    # Write only after all sources are validated; output is staging, not publication.
    for name, data in [(args.output, merged), (args.report, report)]:
        target = pathlib.Path(name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Validated {len(merged)} plugins; {len(report['conflicts'])} GUID conflicts; staged only")


if __name__ == "__main__":
    main()
