# ParentalControl

A .NET 10 solution for browser history monitoring and browser-visible website blocking on Windows.

> **Note on design:** This runs as a standard, disclosed background Windows Service (no visible
> window while running — the same as any service, antivirus agent, or MDM tool). It intentionally
> does **not** implement any anti-detection, process-hiding, or stealth techniques. Parental-control
> software should be installed transparently by whoever administers the machine.

## Projects

- **ParentalControl.Common** — shared library: browser history reader (Chrome/Edge/Firefox SQLite
  history), blocklist manager, daily log writer, scan-state tracker, local block-page certificate
  store.
- **ParentalControl.Service** — Worker Service (`BackgroundService`) that runs two things
  concurrently:
  1. **Browser history scan** (every minute) — scans Chrome/Edge/Firefox history databases for
    new visits (adds page titles) and appends them to `C:\temp\log-yyyy-MM-dd.txt`.
  2. **Website blocking** — applies blocklist domains to the Windows `hosts` file and serves a
    local block page over HTTP/HTTPS so blocked sites show a browser message instead of loading.
- **browser-extension/ParentalControlBrowserExtension** — Manifest V3 extension for
  Chromium-based browsers (Chrome, Edge, Opera, Comet) that polls the local rule API,
  converts partial-fragment rules into browser redirects, and shows a native blocked page for
  sites matched by the block list.
- **browser-extension/ParentalControlFirefoxExtension** — Firefox WebExtension that polls the
  same local rule API and blocks matching sites with a browser-visible page.
- **ParentalControl.Viewer** — Windows Forms app to browse the visited-sites log: pick a date,
  optionally filter by Windows account and/or free text (URL/title), and view matches in a grid.

## How blocking works

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
so HTTPS requests to blocked domains can be served with a real page instead of a certificate
error.

The browser extension reads the active rules from `http://127.0.0.1:8787/api/blocklist` and keeps
its dynamic rules synchronized automatically every few minutes.

The service only edits a clearly marked section of the hosts file:
```
# === ParentalControl BEGIN ===
...
# === ParentalControl END ===
```
so it never disturbs other entries, and can be safely removed by deleting that block.

## How logging works

The service records browser history from each user profile under `C:\Users` and does not change
the machine's DNS settings.

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
```

## Prerequisites

- Windows 10/11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- An elevated (Run as Administrator) PowerShell/terminal if you want the service to read browser
  profiles from other Windows users, install the local CA certificate, or run it as `LocalSystem`.

## 1. Clone and build the solution

```powershell
git clone <this-repo-url>
cd ParentalControl
dotnet build
```
This restores NuGet packages and builds all three projects (`ParentalControl.Common`,
`ParentalControl.Service`, `ParentalControl.Viewer`).

## 2. Configure the block-list and log locations (optional)

The service creates `C:\ProgramData\ParentalControl\block-list.txt` automatically the first time
it runs, but you can prepare it up front if you want rules in place immediately:

```powershell
New-Item -ItemType Directory -Force -Path C:\ProgramData\ParentalControl | Out-Null
notepad C:\ProgramData\ParentalControl\block-list.txt
```
Add one rule per line — a full domain (`example.com`) or a partial fragment (`casino`).

The service writes browser history logs to `C:\temp`. Create the folder in advance if you want
to verify permissions before starting the service:

```powershell
New-Item -ItemType Directory -Force -Path C:\temp | Out-Null
```

## 3. Run ParentalControl.Service

The service must run elevated because it installs the local CA certificate, updates the managed
`hosts` block section, and serves the local block page. Choose one of the two modes below.

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
profiles and manage the blocking certificate.

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
account and/or free text (URL/title), and click **Load** to refresh the grid.

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
