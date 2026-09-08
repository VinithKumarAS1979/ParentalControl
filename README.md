# ParentalControl

A .NET 10 solution for browser history monitoring, browser-visible website blocking, and
application blocking on Windows.

> **Note on design:** This runs as a standard, disclosed background Windows Service (no visible
> window while running — the same as any service, antivirus agent, or MDM tool). It intentionally
> does **not** implement any anti-detection, process-hiding, or stealth techniques. Parental-control
> software should be installed transparently by whoever administers the machine.

## Projects

- **ParentalControl.Common** — shared library: browser history reader (Chrome/Edge/Firefox SQLite
  history), website blocklist manager (hosts-file enforcement), application blocklist manager
  (process termination), daily log writer, scan-state tracker, local block-page certificate store,
  and the shared `ParentalControlOptions`/`PathsConfig` configuration model.
- **ParentalControl.Service** — Worker Service (`BackgroundService`) that runs four things
  concurrently:
  1. **Browser history scan** (every `BrowserRefreshIntervalMinutes`, default 1 minute) — scans
     Chrome/Edge/Firefox history databases for new visits (adds page titles) and appends them to
     `C:\temp\log-yyyy-MM-dd.txt`.
  2. **Website blocking** (every `BlocklistRefreshIntervalMinutes`, default 1 minute) — applies
     blocklist domains to the Windows `hosts` file and serves a local block page over HTTP/HTTPS
     (`BlockPageServer`) so blocked sites show a browser message instead of loading.
  3. **Blocklist API** (`BlocklistApiServer`) — serves the active block rules as JSON at
     `http://127.0.0.1:8787/api/blocklist` so the browser extensions can enforce fragment rules
     client-side.
  4. **Application blocking** (every `AppBlockRefreshIntervalSeconds`, default 3 seconds) —
     enumerates running processes and terminates any that match a rule in the app-blocklist, so
     blocked programs (games, launchers, etc.) get killed shortly after they start; each
     termination is appended to the same daily log so it's visible in the Viewer.
- **browser-extension/ParentalControlBrowserExtension** — Manifest V3 extension for
  Chromium-based browsers (Chrome, Edge, Opera, Comet) that polls the local rule API,
  converts partial-fragment rules into browser redirects, and shows a native blocked page for
  sites matched by the block list.
- **browser-extension/ParentalControlFirefoxExtension** — Firefox WebExtension that polls the
  same local rule API and blocks matching sites with a browser-visible page.
- **ParentalControl.Viewer** — Windows Forms app to browse the visited-sites log: pick a date,
  optionally filter by Windows account and/or free text (URL/title), and view matches in a grid.
  Blocked-application terminations show up in the same grid (`Browser` column = `AppBlock`).

## How website blocking works

Blocklist entries live in:
```
C:\ProgramData\ParentalControl\block-list.txt
```
One rule per line, `#` for comments. A rule can be:
- A **full domain**, e.g. `example.com` — the service writes it to the managed `hosts` file
  section so the domain resolves to `127.0.0.1`, where the local block page server answers with a
  browser-visible "Website blocked" page. The browser extension also enforces the same rule in
  the browser itself, which helps when a browser is using DNS-over-HTTPS.
- A **partial fragment**, e.g. `casino` — matches the service-side rule check, but cannot be
  turned into a reliable browser redirect with `hosts` alone. These are enforced by the browser
  extension, which polls the local rule API and redirects matching navigations to a blocked page.

The block page uses a local CA certificate that the service installs into the machine trust store
(requires administrator privileges — if it can't, HTTPS block pages fall back to a browser
certificate warning, but blocking still functions) so HTTPS requests to blocked domains can be
served with a real page instead of a certificate error.

The browser extension reads the active rules from `http://127.0.0.1:8787/api/blocklist` and keeps
its dynamic rules synchronized automatically every few minutes.

The service only edits a clearly marked section of the hosts file:
```
# === ParentalControl BEGIN ===
...
# === ParentalControl END ===
```
so it never disturbs other entries, and can be safely removed by deleting that block.

A legacy `blocklist.txt` file (from earlier builds) is still read if present, alongside the
canonical `block-list.txt`, for backward compatibility.

## How application blocking works

App-blocklist entries live in:
```
C:\ProgramData\ParentalControl\app-blocklist.txt
```
One rule per line, `#` for comments. A rule can be:
- An **executable name**, e.g. `steam.exe` — matches any running process with that name,
  regardless of where it's installed.
- A **path fragment**, e.g. `Riot Games\VALORANT` — matches any running process whose full
  executable path contains that text (substring match), useful for targeting a specific install
  without blocking every process with the same generic name.

Every few seconds (`AppBlockRefreshIntervalSeconds`) the service enumerates all running
processes; anything matching a rule is force-terminated (including its child processes). Each
termination is logged to the same daily visited-sites log (`C:\temp\log-yyyy-MM-dd.txt`), with
`AppBlock` in the `Browser` column and the process's full path in the `URL` column, so blocked
launch attempts show up in the Viewer alongside browsing history. As with the website blocklist,
changes to the file are picked up automatically — no restart needed.

## How visit logging works

Chrome/Edge/Firefox all store history locally in a SQLite database per profile. The service
enumerates every real Windows user profile under `C:\Users` (skipping `Public`, `Default`,
`All Users`, etc.) and scans each one's Chrome/Edge/Firefox profiles - not just the account the
service runs as - so visits are captured no matter which Windows account made them. This requires
the service to run as `LocalSystem`/an administrator; otherwise it can only read the profiles of
the account it's running as, and logs a warning for the accounts it can't access.

Since the browser holds a lock on its history file while running, the service copies it to a temp
file before reading. Each Windows user + browser + profile combination tracks its own last-seen
visit timestamp in `C:\ProgramData\ParentalControl\scan-state.json` so restarts don't duplicate
log entries.

Log format (tab-separated: time, Windows user, browser, URL, title), one file per day:
```
C:\temp\log-2026-08-24.txt
2026-08-24 14:32:10 UTC	jsmith	Chrome	https://example.com/page	Example Page Title
2026-08-24 14:35:02 UTC	(unknown)	AppBlock	C:\Games\Steam\steam.exe	Blocked - terminated
```

## Configuration

All configurable values live under the `ParentalControl` section of `appsettings.json` (service)
and are also read by the Viewer for shared path resolution. Leave a value empty/absent to use its
default:

| Setting | Default | Purpose |
|---|---|---|
| `LogFolder` | `C:\temp` | Daily visited-sites/app-block log folder |
| `DnsTopologyFolder` | `LogFolder` | Folder for DNS topology snapshots |
| `AppDataFolder` | `C:\ProgramData\ParentalControl` | Config/state root folder |
| `BlockListFile` | `<AppDataFolder>\block-list.txt` | Canonical website blocklist |
| `LegacyBlocklistFile` | `<AppDataFolder>\blocklist.txt` | Older blocklist filename, still read for compatibility |
| `AppBlocklistFile` | `<AppDataFolder>\app-blocklist.txt` | Application blocklist |
| `StateFile` | `<AppDataFolder>\scan-state.json` | Last-scanned browser history timestamps |
| `BlocklistApiPort` | `8787` | Port for the local JSON rule API used by browser extensions |
| `BlockPageHttpPort` | `80` | Port the local HTTP block page listens on |
| `BlockPageHttpsPort` | `443` | Port the local HTTPS block page listens on |
| `BrowserRefreshIntervalMinutes` | `1` | Browser history scan interval |
| `BlocklistRefreshIntervalMinutes` | `1` | Website blocklist/hosts-file refresh interval |
| `DnsTopologyRefreshIntervalMinutes` | `1` | DNS topology snapshot interval |
| `AppBlockRefreshIntervalSeconds` | `3` | Application-process scan interval |

## Prerequisites

- Windows 10/11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- An elevated (Run as Administrator) PowerShell/terminal so the service can read browser profiles
  from other Windows users, install the local CA certificate, update the `hosts` file, bind ports
  80/443, and terminate blocked application processes.

## 1. Clone and build the solution

```powershell
git clone <this-repo-url>
cd ParentalControl
dotnet build
```
This restores NuGet packages and builds all three projects (`ParentalControl.Common`,
`ParentalControl.Service`, `ParentalControl.Viewer`).

## 2. Configure the blocklists and log locations (optional)

The service creates `C:\ProgramData\ParentalControl\block-list.txt` and
`C:\ProgramData\ParentalControl\app-blocklist.txt` automatically the first time it runs, but you
can prepare them up front if you want rules in place immediately:

```powershell
New-Item -ItemType Directory -Force -Path C:\ProgramData\ParentalControl | Out-Null
notepad C:\ProgramData\ParentalControl\block-list.txt
notepad C:\ProgramData\ParentalControl\app-blocklist.txt
```
Add one rule per line to each — for websites, a full domain (`example.com`) or a partial fragment
(`casino`); for applications, an executable name (`steam.exe`) or a path fragment
(`Riot Games\VALORANT`).

The service writes browser history and app-block logs to `C:\temp`. Create the folder in advance
if you want to verify permissions before starting the service:

```powershell
New-Item -ItemType Directory -Force -Path C:\temp | Out-Null
```

## 3. Run ParentalControl.Service

The service must run elevated because it installs the local CA certificate, updates the managed
`hosts` block section, serves the local block page on ports 80/443, and terminates blocked
application processes. Choose one of the two modes below.

### Option A — Console mode (quick manual testing, no installation)

Open an elevated PowerShell and run:
```powershell
cd ParentalControl
dotnet run --project src/ParentalControl.Service
```
It runs in the foreground and logs to the console. Press `Ctrl+C` to stop it. Useful for
verifying behavior before installing it as a real service.

### Option B — Install as a Windows Service (persists across logoff/reboot)

In an **elevated** PowerShell:
```powershell
dotnet publish src/ParentalControl.Service -c Release -o publish/service
sc.exe create ParentalControlService binPath= "C:\full\path\to\publish\service\ParentalControl.Service.exe" start= auto
sc.exe start ParentalControlService
```
Replace the `binPath=` value with the actual full path to the published `.exe` (note the
required space after `binPath=` and `start=` — that's `sc.exe` syntax, not a typo). Installed
this way it runs as `LocalSystem`, which is the easiest way to read every Windows user's browser
profiles, manage the blocking certificate, and terminate blocked processes owned by any user.

To check status, stop, or remove it later:
```powershell
sc.exe query ParentalControlService
sc.exe stop ParentalControlService
sc.exe delete ParentalControlService
```

## 4. Run ParentalControl.Viewer

No elevation or admin rights needed — it only reads the log files written by the service.

```powershell
cd ParentalControl
dotnet run --project src/ParentalControl.Viewer
```
This opens a window defaulting to today's (UTC) log. Pick a date, optionally filter by Windows
account and/or free text (URL/title), and click **Load** to refresh the grid. Blocked-application
terminations appear alongside visited sites, with `AppBlock` in the Browser column.

If you'd rather run a standalone build instead of `dotnet run`:
```powershell
dotnet publish src/ParentalControl.Viewer -c Release -o publish/viewer
.\publish\viewer\ParentalControl.Viewer.exe
```

## 5. Install the browser extensions

### Chromium-based browsers

The Chromium extension lives in `browser-extension/ParentalControlBrowserExtension` and does not
need a build step. Install it as an unpacked extension in Chrome, Edge, Opera, or Comet:

1. Open `chrome://extensions` in Chrome, Opera, or Comet, or `edge://extensions` in Edge.
2. Turn on Developer mode.
3. Click Load unpacked.
4. Select `browser-extension/ParentalControlBrowserExtension`.

Once installed, the extension will fetch the active rules from the local service and apply the
fragment-based browser blocking rules automatically. The service keeps the rules up to date and
the extension refreshes them on startup and every few minutes.

### Firefox

The Firefox extension lives in `browser-extension/ParentalControlFirefoxExtension`.

1. Open `about:debugging#/runtime/this-firefox`.
2. Click Load Temporary Add-on.
3. Select the `manifest.json` file inside `browser-extension/ParentalControlFirefoxExtension`.

Firefox will load the add-on temporarily for the current session. If you want a signed, permanent
install later, I can add a packaged distribution workflow next.

### IE

Internet Explorer does not support modern browser extensions, so there is no IE add-on for this
project. The practical fallback is the service-side hosts/block-page behavior plus migrating users
to Edge IE mode or another supported browser.
