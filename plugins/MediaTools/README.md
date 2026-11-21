# MediaTools

Independent plugin for previewing and managing media renaming, container conversion,
split-file merging and stream deduplication. Plugin identity, package files and server
baseline are defined in [`plugin.json`](plugin.json).

## Access and operation

Management endpoints require an authenticated Jellyfin administrator. Inspect previews
and job results before applying filesystem-changing operations. FFmpeg-dependent
operations need a usable server FFmpeg executable; these operations are separate from
ordinary playback and should be scheduled with the server's storage and load in mind.

Plugin Pages is an optional runtime dependency for the navigation entry. Its pinned
source and installation instructions are in [`external/README.md`](../../external/README.md).
The plugin's own page remains a fallback when that integration is absent.

## Build and validate

```bash
dotnet build plugins/MediaTools/MediaTools.csproj -c Release
dotnet test tests/Jellyfin.Plugins.Tests/Jellyfin.Plugins.Tests.csproj -c Release --filter MediaTools
```

The package includes this plugin DLL and `TagLibSharp.dll`, as declared in metadata.
Run the shared package smoke test after building the entire solution. Real-server
filesystem/FFmpeg tests belong in release qualification; unit tests alone do not
qualify destructive operations against a production media library.
