#!/usr/bin/env python3
"""Create temporary plugin packages from Release outputs and validate them."""

from __future__ import annotations

import json
import argparse
import pathlib
import tempfile
import zipfile

import release_validation

ROOT = pathlib.Path.cwd()


def smoke_test_plugin(metadata_path: pathlib.Path, output_root: pathlib.Path) -> None:
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    project_path = ROOT / metadata["project"]
    build_output = project_path.parent / "bin" / "Release" / metadata["framework"]
    package_path = output_root / f"{metadata['slug']}.zip"
    artifacts = metadata.get("artifacts") or [metadata["assembly"]]

    with zipfile.ZipFile(package_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for artifact in artifacts:
            source = build_output / artifact
            if not source.is_file():
                raise SystemExit(f"Missing Release artifact for smoke test: {source}")
            archive.write(source, pathlib.PurePath(artifact).name)

        pdb = build_output / metadata["assembly"].replace(".dll", ".pdb")
        if pdb.is_file():
            archive.write(pdb, pdb.name)

    release_validation.validate_package(metadata, package_path)
    print(f"validated package for {metadata['slug']}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plugin", help="Validate one plugin slug instead of all plugins")
    parser.add_argument("--output", type=pathlib.Path, help="Keep validated packages in this directory")
    args = parser.parse_args()
    metadata_paths = sorted((ROOT / "plugins").glob("*/plugin.json"))
    if args.plugin:
        metadata_paths = [path for path in metadata_paths if path.parent.name == args.plugin]
    if not metadata_paths:
        raise SystemExit("No matching plugin metadata files were found.")

    if args.output:
        args.output.mkdir(parents=True, exist_ok=True)
        for metadata_path in metadata_paths:
            smoke_test_plugin(metadata_path, args.output)
    else:
        with tempfile.TemporaryDirectory(prefix="jellyfin-package-smoke-") as directory:
            for metadata_path in metadata_paths:
                smoke_test_plugin(metadata_path, pathlib.Path(directory))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
