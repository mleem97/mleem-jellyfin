# Gracenote Metadata Provider

Album metadata via Gracenote MusicID for the mleem Jellyfin plugin library.

- Remote metadata for `MusicAlbum` (title, year, genres, review, `GN_ID` provider id)
- Manual "Identify" search results
- Automatic Gracenote user registration when only a client id is configured
- Admin status endpoint: `GET /Plugins/GracenoteProvider/Status`
- Admin validation endpoint: `POST /Plugins/GracenoteProvider/TestGracenote?clientId=...`
