#!/usr/bin/env python3
"""Scaffold one independently versioned Jellyfin plugin and its test project."""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]


def prepare(root: pathlib.Path, slug: str, display_name: str) -> dict[pathlib.Path, str]:
    if not re.fullmatch(r"[A-Z][A-Za-z0-9]{1,63}", slug):
        raise ValueError("Slug must be a PascalCase C# identifier with 2–64 ASCII letters/digits")
    if slug.upper() in {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}:
        raise ValueError("Slug is a reserved filesystem name")
    if not display_name.strip() or any(ord(char) < 32 for char in display_name):
        raise ValueError("Display name must be a nonempty single line")
    plugin_dir = root / "plugins" / slug
    test_dir = root / "tests" / f"{slug}.Tests"
    for parent, name in [(root / "plugins", slug), (root / "tests", f"{slug}.Tests")]:
        if parent.exists() and any(child.name.casefold() == name.casefold() for child in parent.iterdir()):
            raise ValueError(f"Refusing to overwrite existing path: {parent / name}")
    settings = ET.parse(root / "Directory.Build.props")
    framework = settings.findtext(".//TargetFramework")
    packages = ET.parse(root / "Directory.Packages.props")
    version = next(item.attrib["Version"] for item in packages.findall(".//PackageVersion") if item.attrib["Include"] == "Jellyfin.Controller")
    if not framework or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("Cannot determine framework/server ABI from central build metadata")
    identity = str(uuid.uuid4())
    namespace = f"Jellyfin.Plugin.{slug}"
    metadata = {
        "slug": slug, "name": display_name, "guid": identity,
        "version": "0.1.0.0", "targetAbi": version + ".0", "framework": framework,
        "project": f"plugins/{slug}/{slug}.csproj", "packagePrefix": slug,
        "assembly": namespace + ".dll", "owner": "mleem97", "category": "General",
        "overview": display_name, "description": f"{display_name} for Jellyfin.",
        "artifacts": [namespace + ".dll"], "runtimeDependencies": [],
    }
    project = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>{namespace}</RootNamespace>
    <AssemblyName>{namespace}</AssemblyName>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>
    <Version>0.1.0.0</Version>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Jellyfin.Controller" />
    <PackageReference Include="Jellyfin.Model" />
  </ItemGroup>
</Project>
'''
    plugin = f'''using System;
using {namespace}.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace {namespace};

/// <summary>Entry point for {slug}.</summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>
{{
    /// <summary>Initializes the plugin.</summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">Jellyfin configuration serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {{
    }}

    /// <inheritdoc />
    public override string Name => {json.dumps(display_name)};

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("{identity}");
}}
'''
    configuration = f'''using MediaBrowser.Model.Plugins;

namespace {namespace}.Configuration;

/// <summary>Configuration for {slug}.</summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{{
    /// <summary>Gets or sets a value indicating whether the feature is enabled.</summary>
    public bool Enabled {{ get; set; }}
}}
'''
    tests = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
    <PackageReference Include="coverlet.collector" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../plugins/{slug}/{slug}.csproj" />
  </ItemGroup>
</Project>
'''
    readme = f'''# {display_name}

Independent Jellyfin plugin `{slug}`. Initial development version: `0.1.0.0`.

## Compatibility and scope

- Jellyfin `{version}` / `{framework}`; no additional runtime plugin dependencies yet.
- This is an unpublished scaffold. Implement the feature before creating a release.
- Configuration defaults to disabled. Add authenticated/admin-only APIs as required.
- Add a `Web/` resource directory only when a configuration or user page is needed.

## Development

```bash
dotnet restore plugins/{slug}/{slug}.csproj
dotnet test tests/{slug}.Tests/{slug}.Tests.csproj -c Release
python .github/scripts/validate_repository.py
```

Commit the generated `packages.lock.json` files. Add behavior and authorization tests
under `tests/{slug}.Tests` before enabling production functionality. Register any
runtime plugins by ID in `plugin.json`; manage their sources under `external/`.

## Release

Use the shared manual release workflow after validation on a real Jellyfin server.
Tag namespace: `{slug}-v<four-part-version>`. The workflow updates only this plugin's
catalog entry. Do not add a catalog version before the corresponding asset exists.
'''
    return {
        plugin_dir / f"{slug}.csproj": project,
        plugin_dir / "Plugin.cs": plugin,
        plugin_dir / "Configuration/PluginConfiguration.cs": configuration,
        plugin_dir / "plugin.json": json.dumps(metadata, indent=2, ensure_ascii=False) + "\n",
        plugin_dir / "README.md": readme,
        test_dir / f"{slug}.Tests.csproj": tests,
        test_dir / "README.md": f"# {display_name} tests\n\nAdd feature, authorization, failure and lifecycle tests here as behavior is implemented.\nThe new scaffold has no production behavior and intentionally contains no placeholder tests.\n",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("slug", help="PascalCase folder and assembly suffix, e.g. PlaybackInsights")
    parser.add_argument("--name", help="Human-readable plugin name")
    parser.add_argument("--dry-run", action="store_true", help="Show planned files without modifying the repository")
    parser.add_argument("--dotnet", default="dotnet", help=".NET CLI executable")
    args = parser.parse_args()
    try:
        files = prepare(ROOT, args.slug, args.name or args.slug)
        for path in files:
            print(path.relative_to(ROOT))
        if args.dry_run:
            return 0
        executable = shutil.which(args.dotnet)
        if executable is None:
            raise ValueError("Install the SDK from global.json, or specify --dotnet /path/to/dotnet")
        solution = ROOT / "Jellyfin.Plugins.sln"
        build = ROOT / "build.yaml"
        validator = ROOT / ".github/scripts/validate_repository.py"
        if not validator.is_file():
            raise ValueError("Repository metadata validator is missing")
        backups = {path: path.read_bytes() for path in [solution, build]}
        try:
            for path, content in files.items():
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            projects = [str(path.relative_to(ROOT)) for path in files if path.suffix == ".csproj"]
            subprocess.run([executable, "sln", str(solution), "add", *projects], cwd=ROOT, check=True)
            subprocess.run([sys.executable, str(validator), "--sync-build"], cwd=ROOT, check=True)
        except (OSError, subprocess.CalledProcessError):
            for path, content in backups.items():
                path.write_bytes(content)
            shutil.rmtree(ROOT / "plugins" / args.slug, ignore_errors=True)
            shutil.rmtree(ROOT / "tests" / f"{args.slug}.Tests", ignore_errors=True)
            raise
        print("Scaffold created. Implement behavior, restore to generate lock files, then build and test before publishing.")
        return 0
    except (ValueError, OSError, StopIteration, ET.ParseError, subprocess.CalledProcessError) as exc:
        print(f"Cannot create plugin: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
