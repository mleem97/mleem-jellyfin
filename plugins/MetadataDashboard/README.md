# MetadataDashboard

Independent metadata inspection and management UI for Jellyfin music. The plugin
contains metadata audit services, MusicBrainz lookups and Cover Art Archive support.
Its identity, output package and server baseline are defined in
[`plugin.json`](plugin.json).

Plugin Pages is an optional runtime dependency for its navigation entry. See
[`external/README.md`](../../external/README.md) for the source pin and separate runtime
installation. Inspect provider matches and the permissions of the active account
before applying metadata. External provider availability is independent of this plugin.

## Build and validate

```bash
dotnet build plugins/MetadataDashboard/MetadataDashboard.csproj -c Release
dotnet test tests/Jellyfin.Plugins.Tests/Jellyfin.Plugins.Tests.csproj -c Release --filter MetadataDashboard
```

The development baseline is Jellyfin 12.2 and .NET 10. Published catalog compatibility
is determined by the actual release artifact, not by the current development branch.
Verify manual UI flows on a real Jellyfin installation before a new release.
