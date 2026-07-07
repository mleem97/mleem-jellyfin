# COV Integration Policy

This document defines how the MusicHoarderz/COV integration must be implemented in this repository.

## Rules

- Do not use the internal COV API.
- Do not scrape the COV website.
- Do not fully automate artwork retrieval from COV.
- Use COV only as a semi-automatic, user-driven artwork picker.
- Open the official COV website in a compatible browser or web view.
- Let the user interact with the official website and pick the artwork.
- Display visible attribution text in the integration UI.
- The visible attribution text must include the full address: https://covers.musichoarders.xyz/

## Allowed simple flow

The plugin may open the official COV website with predefined query parameters:

```text
https://covers.musichoarders.xyz/?theme=dark&country=DE&artist=ARTIST&album=ALBUM
```

Allowed query parameters:

```text
theme=light|dark
resolution=value
sources=source1,source2
country=code
artist=value
album=value
identifier=value
```

A search may be initiated by the website when at least one of `artist`, `album` or `identifier` is present. The plugin must not bypass the website or automate the final choice.

## Allowed advanced flow

The plugin may use the documented remote protocol to receive the user's picked cover from the official COV website.

Allowed remote parameters:

```text
remote.port
remote.agent
remote.text
```

Required integration text example:

```text
This Jellyfin plugin uses MusicHoarderz/COV: https://covers.musichoarders.xyz/
```

Supported communication modes:

- `remote.port=browser` using `postMessage` between opener and child page.
- `remote.port=ws:PORT` using a local WebSocket endpoint.

The plugin may handle a picked cover after the user explicitly picks it in the official COV website.

## Optional COV Integration Tool

The COV Integration Tool may be supported as an optional admin-configured helper. The plugin may not require it by default.

Allowed uses:

- read metadata from audio files;
- pass query parameters;
- open the official COV website;
- save a user-picked cover to disk;
- call back into Jellyfin plugin code after a user-picked cover is available.

## Impact on MusicHoarderzProvider

The provider must be designed as a COV launcher and picker integration, not as an automated COV API client.

Implementation consequences:

- `MusicHoarderzCoverProvider` should open the official website or prepare a remote-protocol session.
- Scheduled tasks must not fetch or apply COV artwork automatically.
- Scheduled tasks may only identify candidates, missing covers or low-quality covers and queue user review actions.
- Automatic artwork apply may only be used for sources whose terms and API behavior allow it, such as admin-configured Spotify or local files, and only when explicitly enabled.
- COV-picked images may be applied only after explicit user selection.

## Acceptance requirements

- No internal COV endpoint is called.
- No HTML scraping is implemented.
- COV attribution is visible to the user.
- The full website address is visible: https://covers.musichoarders.xyz/
- A COV result cannot be applied unless the user picked it on the official COV website.
- Admin settings expose COV as a semi-automatic picker, not as an automatic provider.
