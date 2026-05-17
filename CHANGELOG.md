# Changelog

## Unreleased

## v1.5.1.0 - 2026-05-17

### Fixed
- Sidebar and settings menus not being loaded in read-only filesystem installs.

### Breaking Changes
- Plugin requires the File Transformation plugin to function in read-only filesystem installs.

## v1.5.0.0 - 2026-05-12

Changes since `f381716` (`update(workflow): Versioning issue`).

### Added

- Added automatic Jellyfin web menu injection through a new hosted `InjectionService`, removing the need to manually configure a separate JavaScript injector for the Letterboxd menu links.
- Added an embedded `letterboxd-menu.js` script that adds Letterboxd settings and stats links to the Jellyfin sidebar and user preferences menu for regular users.
- Added dedicated embedded plugin routes for the user settings page, stats page, and menu script:
  - `GET /Jellyfin.Plugin.LetterboxdSync/UserSettingsPage`
  - `GET /Jellyfin.Plugin.LetterboxdSync/StatsPage`
  - `GET /Jellyfin.Plugin.LetterboxdSync/MenuScript`
- Added a user-facing manual sync endpoint, `POST /Jellyfin.Plugin.LetterboxdSync/RunSync`, that syncs the current user's played movies directly from the stats page.
- Added per-user sync history storage by `UserId`, with legacy shared history lookup preserved for older records.
- Added per-user filtering for sync stats, recent history, retries, and reviews so users only see relevant activity for their Jellyfin or Letterboxd account.
- Added account connection testing to the admin configuration account rows.
- Added registration and test coverage for the new `InjectionService` hosted service.

### Changed

- Redesigned the standalone stats page with a full header, summary cards, recent activity table, direct manual sync button, review action, retry action for failed items, and authenticated navigation links.
- Redesigned the standalone user settings page with a full header, account form, connection test button, save/disconnect actions, authenticated navigation, and clearer status messages.
- Updated the admin configuration page with links to the new user settings and stats pages.
- Updated review modal styling and placement so it renders as an overlay outside the main content containers.
- Updated review, retry, playback, scheduled sync, and manual sync history records to include the Jellyfin `UserId`.
- Reused shared sync helper logic for Letterboxd rating mapping and favorite/like syncing during retry and manual sync flows.
- Updated plugin page registration so the main admin configuration page appears in Jellyfin's main menu, while the user pages and menu script are registered as embedded resources.
- Refreshed the Letterboxd UI color palette across admin, stats, and user settings pages.

### Fixed

- Fixed stats and history views leaking shared global history by filtering results to the current user/account where possible.
- Fixed review and retry flows to resolve the current account consistently and require an enabled Letterboxd account.
- Fixed manual retry favorite syncing to use the shared favorite sync helper instead of duplicating like-sync behavior.
- Fixed failed retry history lookup so the displayed title is resolved from the current user's history.
- Bump project version to 1.5.0.0

### Maintenance

- Embedded `Web/letterboxd-menu.js` in the plugin project file.
- Ignored generated `LetterboxdSync/Web/mock_*.html` files.

