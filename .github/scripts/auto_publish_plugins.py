#!/usr/bin/env python3
"""Auto-version and publish changed Jellyfin plugins.

The script is designed for GitHub Actions. It detects changed plugin folders,
bumps each plugin version, creates a changelog from commits, updates metadata,
builds ZIP packages, tags releases and creates GitHub releases.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import zipfile

ROOT = pathlib.Path.cwd()
PLUGINS_DIR = ROOT / "plugins"
MANIFEST_PATH = ROOT / "manifest.json"
ZERO_SHA = "0000000000000000000000000000000000000000"


def run(args: list[str], *, capture: bool = False) -> str:
    print("+ " + " ".join(args))
    result = subprocess.run(args, check=True, text=True, capture_output=capture)
    return result.stdout.strip() if capture else ""


def load_json(path: pathlib.Path):
    return json.loads(path.read_text(encoding="utf-8"))


def save_json(path: pathlib.Path, value) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def event_payload() -> dict:
    event_path = os.environ.get("GITHUB_EVENT_PATH")
    if not event_path:
        return {}
    path = pathlib.Path(event_path)
    if not path.exists():
        return {}
    return load_json(path)


def changed_files() -> list[str]:
    payload = event_payload()
    before = payload.get("before")
    after = payload.get("after") or os.environ.get("GITHUB_SHA") or "HEAD"
    if before and before != ZERO_SHA:
        try:
            return run(["git", "diff", "--name-only", before, after], capture=True).splitlines()
        except subprocess.CalledProcessError:
            pass
    try:
        return run(["git", "diff", "--name-only", "HEAD~1", "HEAD"], capture=True).splitlines()
    except subprocess.CalledProcessError:
        return []


def discover_plugins() -> list[str]:
    return sorted(path.parent.name for path in PLUGINS_DIR.glob("*/plugin.json"))


def selected_plugins() -> list[str]:
    requested = os.environ.get("PLUGIN") or os.environ.get("INPUT_PLUGIN") or "auto"
    if requested and requested.lower() != "auto":
        return [requested]

    slugs: set[str] = set()
    for file_name in changed_files():
        parts = pathlib.PurePosixPath(file_name).parts
        if len(parts) >= 2 and parts[0] == "plugins":
            if (PLUGINS_DIR / parts[1] / "plugin.json").exists():
                slugs.add(parts[1])
    return sorted(slugs)


def latest_tag(slug: str) -> str | None:
    tags = run(["git", "tag", "--list", f"{slug}-v*", "--sort=-creatordate"], capture=True).splitlines()
    return tags[0] if tags else None


def changelog_for(slug: str, tag: str | None) -> str:
    rev_range = f"{tag}..HEAD" if tag else "HEAD"
    output = run([
        "git",
        "log",
        rev_range,
        "--pretty=format:- %s (%h)",
        "--",
        f"plugins/{slug}",
        "manifest.json",
        "build.yaml",
    ], capture=True)
    return output.strip() or "- Maintenance updates."


def bump_level(changelog: str) -> str:
    configured = (os.environ.get("VERSION_BUMP") or os.environ.get("INPUT_BUMP") or "auto").lower()
    if configured in {"major", "minor", "patch", "revision"}:
        return configured
    if "BREAKING CHANGE" in changelog or re.search(r"^- [a-zA-Z]+!:", changelog, re.MULTILINE):
        return "major"
    if re.search(r"^- feat(\(.+\))?:", changelog, re.MULTILINE):
        return "minor"
    if re.search(r"^- (fix|perf|refactor)(\(.+\))?:", changelog, re.MULTILINE):
        return "patch"
    return "revision"


def bump_version(version: str, level: str) -> str:
    parts = [int(part) for part in version.split(".")]
    while len(parts) < 4:
        parts.append(0)
    major, minor, patch, revision = parts[:4]
    if level == "major":
        return f"{major + 1}.0.0.0"
    if level == "minor":
        return f"{major}.{minor + 1}.0.0"
    if level == "patch":
        return f"{major}.{minor}.{patch + 1}.0"
    return f"{major}.{minor}.{patch}.{revision + 1}"


def update_csproj(project_path: pathlib.Path, version: str) -> None:
    text = project_path.read_text(encoding="utf-8")
    text = re.sub(r"<AssemblyVersion>[^<]+</AssemblyVersion>", f"<AssemblyVersion>{version}</AssemblyVersion>", text)
    text = re.sub(r"<FileVersion>[^<]+</FileVersion>", f"<FileVersion>{version}</FileVersion>", text)
    if "<Version>" in text:
        text = re.sub(r"<Version>[^<]+</Version>", f"<Version>{version}</Version>", text)
    else:
        text = text.replace(f"<FileVersion>{version}</FileVersion>", f"<FileVersion>{version}</FileVersion>\n    <Version>{version}</Version>")
    project_path.write_text(text, encoding="utf-8")


def build_plugin(project: pathlib.Path) -> None:
    run(["dotnet", "restore", str(project)])
    run(["dotnet", "build", str(project), "-c", "Release", "--no-restore"])


def package_plugin(metadata: dict, version: str) -> pathlib.Path:
    release_assets = ROOT / "release-assets"
    release_assets.mkdir(exist_ok=True)
    project = ROOT / metadata["project"]
    output_dir = project.parent / "bin" / "Release" / metadata["framework"]
    package_name = f"{metadata['packagePrefix']}_{version}.zip"
    zip_path = release_assets / package_name
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for artifact in metadata.get("artifacts", [metadata["assembly"]]):
            artifact_path = output_dir / artifact
            if artifact_path.exists():
                archive.write(artifact_path, artifact_path.name)
        pdb_path = output_dir / metadata["assembly"].replace(".dll", ".pdb")
        if pdb_path.exists():
            archive.write(pdb_path, pdb_path.name)
    return zip_path


def md5_upper(path: pathlib.Path) -> str:
    return hashlib.md5(path.read_bytes()).hexdigest().upper()


def update_manifest(metadata: dict, version: str, changelog: str, zip_path: pathlib.Path) -> None:
    manifest = load_json(MANIFEST_PATH)
    entry = next((item for item in manifest if item.get("guid") == metadata["guid"]), None)
    if entry is None:
        entry = {"guid": metadata["guid"], "versions": []}
        manifest.append(entry)

    tag_name = f"{metadata['slug']}-v{version}"
    repo = os.environ.get("GITHUB_REPOSITORY", "mleem97/mleem-jellyfin")
    entry["name"] = metadata["name"]
    entry["description"] = metadata["description"]
    entry["overview"] = metadata["overview"]
    entry["owner"] = metadata["owner"]
    entry["category"] = metadata["category"]
    new_version = {
        "version": version,
        "changelog": changelog,
        "targetAbi": metadata["targetAbi"],
        "sourceUrl": f"https://github.com/{repo}/releases/download/{tag_name}/{zip_path.name}",
        "checksum": md5_upper(zip_path),
        "timestamp": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    }
    old_versions = entry.get("versions") or []
    entry["versions"] = [new_version] + [item for item in old_versions if item.get("version") != version]
    save_json(MANIFEST_PATH, manifest)


def release_with_gh(metadata: dict, version: str, changelog: str, zip_path: pathlib.Path) -> None:
    tag_name = f"{metadata['slug']}-v{version}"
    run(["git", "tag", tag_name])
    notes_path = ROOT / "release-assets" / f"{metadata['slug']}-CHANGELOG.md"
    notes_path.write_text(changelog + "\n", encoding="utf-8")
    run([
        "gh",
        "release",
        "create",
        tag_name,
        str(zip_path),
        str(MANIFEST_PATH),
        "--title",
        f"{metadata['name']} {version}",
        "--notes-file",
        str(notes_path),
    ])


def commit_and_push(slugs: list[str]) -> None:
    status = run(["git", "status", "--porcelain"], capture=True)
    if status.strip():
        run(["git", "add", "plugins", "manifest.json"])
        run(["git", "commit", "-m", f"chore(release): publish {', '.join(slugs)}"])
    run(["git", "push", "origin", "main", "--follow-tags"])


def main() -> int:
    slugs = selected_plugins()
    if not slugs:
        print("No changed plugin projects detected.")
        return 0

    known = discover_plugins()
    for slug in slugs:
        if slug not in known:
            raise SystemExit(f"Unknown plugin slug: {slug}")

    release_data: list[tuple[dict, str, str, pathlib.Path]] = []
    for slug in slugs:
        metadata_path = PLUGINS_DIR / slug / "plugin.json"
        metadata = load_json(metadata_path)
        changelog = changelog_for(slug, latest_tag(slug))
        level = bump_level(changelog)
        new_version = bump_version(metadata["version"], level)
        metadata["version"] = new_version
        save_json(metadata_path, metadata)
        project = ROOT / metadata["project"]
        update_csproj(project, new_version)
        build_plugin(project)
        zip_path = package_plugin(metadata, new_version)
        update_manifest(metadata, new_version, changelog, zip_path)
        release_data.append((metadata, new_version, changelog, zip_path))

    run(["git", "config", "user.name", "github-actions[bot]"])
    run(["git", "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com"])
    commit_and_push(slugs)

    for metadata, version, changelog, zip_path in release_data:
        release_with_gh(metadata, version, changelog, zip_path)
    run(["git", "push", "origin", "main", "--follow-tags"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
