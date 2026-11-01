# MusicToolkit

Independent toolkit for audio duplicate analysis, filename previews/renaming and an
optional Spotify-style music page. Its identity, version and package contents are
defined in [`plugin.json`](plugin.json).

Administrative operations include media file changes; review previews first and test
against a dedicated library before using them on production media. The Paradox runtime
plugins provide optional page registration and web transformation. Their sources are
pinned as submodules; see [`external/README.md`](../../external/README.md) for installation.

## Build and test

```bash
dotnet build plugins/MusicToolkit/MusicToolkit.csproj -c Release
dotnet test tests/MusicToolkit.Tests/MusicToolkit.Tests.csproj -c Release
```

The package must include both `Jellyfin.Plugin.MusicToolkit.dll` and `TagLibSharp.dll`.
The historical `1.0.0.1` archive omitted the latter and is not advertised in the
installation catalog. A corrected forward release is required; do not overwrite the
existing published asset or silently reuse its version.

MusicSuite and MusicToolkit both provide music UI integrations. Validate the intended
combination and redirection settings on a real server before enabling both. Longer-term,
MusicSuite should own the music browsing UI and MusicToolkit should focus on library
maintenance so that their responsibilities and navigation do not overlap.
