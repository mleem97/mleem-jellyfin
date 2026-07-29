#!/usr/bin/env python3
"""Create temporary plugin packages from Release outputs and validate them."""

from __future__ import annotations

import json
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
    metadata_paths = sorted((ROOT / "plugins").glob("*/plugin.json"))
    if not metadata_paths:
        raise SystemExit("No plugin metadata files were found.")

    with tempfile.TemporaryDirectory(prefix="jellyfin-package-smoke-") as directory:
        output_root = pathlib.Path(directory)
        for metadata_path in metadata_paths:
            smoke_test_plugin(metadata_path, output_root)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
