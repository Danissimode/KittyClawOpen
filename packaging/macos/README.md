# KittyClawOpen macOS App Launcher

This directory contains the macOS app bundle for KittyClawOpen.

## Structure

```
KittyClawOpen.app/
  Contents/
    Info.plist
    MacOS/
      KittyClawOpen   ← shell script launcher
```

## Installation

Run the install script:

```bash
./install-app.sh
```

This copies `KittyClawOpen.app` to `/Applications` (or `~/Applications` if permissions are insufficient).

## Uninstallation

Run the uninstall script:

```bash
./uninstall-app.sh
```

To also remove user data and logs:

```bash
./uninstall-app.sh --purge
```

## App Behavior

When launched, the app:

1. Checks if `localhost:5230` is already listening. If so, it opens the browser at `http://localhost:8080` (or `5230` as fallback) and exits.
2. Otherwise, it resolves the repo directory (`$KITTYCLAWOPEN_HOME` or `~/Documents/GitHub/KittyClawOpen`).
3. Starts the backend with `dotnet run --project KittyClaw.Web/KittyClaw.Web.csproj` in the background.
4. Waits 3 seconds for the server to boot.
5. Opens the UI in the default browser.

## Requirements

- macOS 12.0 or later
- .NET SDK installed (for `dotnet run`)
- The KittyClawOpen repository cloned to `~/Documents/GitHub/KittyClawOpen`
