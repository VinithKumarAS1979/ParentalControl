# ParentalControl

A .NET 10 solution for basic parental-control website monitoring and blocking on Windows.

> **Note on design:** This runs as a standard, disclosed background Windows Service (no visible
> window while running — the same as any service, antivirus agent, or MDM tool). It intentionally
> does **not** implement any anti-detection, process-hiding, or stealth techniques. Parental-control
> software should be installed transparently by whoever administers the machine.

## Projects

- **ParentalControl.Common** — shared library: browser history reader (Chrome/Edge/Firefox SQLite
  history), a local DNS proxy + network adapter configurator (covers every browser/app on the
  machine), hosts-file blocklist enforcer, daily log writer, scan-state tracker.
- **ParentalControl.Service** — Worker Service (`BackgroundService`) that runs two things
  concurrently:
  1. **DNS proxy** (`DnsProxyServer`) — binds `127.0.0.1:53`. On startup, `NetworkDnsConfigurator`
     points every active network adapter's DNS server at 127.0.0.1 (via WMI), so **every app on
     the machine** — not just specific browsers, and including incognito/private windows — has
     its DNS lookups pass through the proxy. Each lookup is logged; blocked domains get an
     NXDOMAIN reply, everything else is forwarded to an upstream resolver (1.1.1.1) and relayed
     back. Original adapter DNS settings are restored when the service stops.
  2. **Browser history scan** (every minute) — scans Chrome/Edge/Firefox history databases for
     new visits (adds page titles, which DNS-level logging can't provide) and appends them to
     `C:\temp\log-yyyy-MM-dd.txt`; also updates the Windows `hosts` file so full-domain block
     rules resolve to `127.0.0.1` as a second, redundant enforcement layer.
- **ParentalControl.Viewer** — Windows Forms app to browse the visited-sites log: pick a date,
  optionally filter by Windows account and/or free text (URL/title), and view matches in a grid.

### DNS proxy limitations

- Browsers with **DNS-over-HTTPS ("Secure DNS")** enabled bypass the OS resolver entirely and
  won't be seen by the local proxy — only hosts-file / history-scan blocking still applies to them.
- Requires administrator privileges to bind port 53 and to change adapter DNS settings via WMI.
- Only sees domain names, not full URLs/paths (that detail still comes from the history scan).

## How blocking works

Blocklist entries live in:
```
C:\ProgramData\ParentalControl\blocklist.txt
```
One rule per line, `#` for comments. A rule can be:
- A **full domain**, e.g. `example.com` — matches `example.com`, `www.example.com`, and any
  subdomain. Enforced two ways: the DNS proxy returns NXDOMAIN for it, and it's also written to
  the `hosts` file (redirected to `127.0.0.1`) as a redundant second layer.
- A **partial fragment**, e.g. `casino` — matches any visited domain containing that text
  (substring match). Enforced by the DNS proxy (NXDOMAIN for any queried domain containing the
  fragment). It is **not** written to the `hosts` file, since that file only supports exact
  domain entries — the DNS proxy is what makes fragment blocking work.

The service only edits a clearly marked section of the hosts file:
```
# === ParentalControl BEGIN ===
...
# === ParentalControl END ===
```
so it never disturbs other entries, and can be safely removed by deleting that block.

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
2026-08-24 14:32:11 UTC	(all users)	DNS	example.com	
```
DNS-proxy entries can't be attributed to a specific Windows account (DNS queries carry no
user/session information), so they're logged under `(all users)`.

## Prerequisites

- Windows 10/11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- An elevated (Run as Administrator) PowerShell/terminal for anything that touches the `hosts`
  file, port 53, or adapter DNS settings — i.e. the service, in any mode.

## 1. Clone and build the solution

```powershell
git clone <this-repo-url>
cd ParentalControl
dotnet build
```
This restores NuGet packages and builds all three projects (`ParentalControl.Common`,
`ParentalControl.Service`, `ParentalControl.Viewer`).

## 2. Configure the blocklist (optional, before first run)

The service creates `C:\ProgramData\ParentalControl\blocklist.txt` automatically the first time
it runs, with a comment header and no rules. You can create/edit it yourself first if you want
rules in place immediately:

```powershell
New-Item -ItemType Directory -Force -Path C:\ProgramData\ParentalControl | Out-Null
notepad C:\ProgramData\ParentalControl\blocklist.txt
```
Add one rule per line — a full domain (`example.com`) or a partial fragment (`casino`). See
[How blocking works](#how-blocking-works) for details. Changes are picked up on the next scan
cycle (every minute) even while the service is already running.

## 3. Run ParentalControl.Service

The service must run elevated because it edits the `hosts` file, binds port 53 for the DNS
proxy, and changes network adapter DNS settings via WMI. Choose one of the two modes below.

### Option A — Console mode (quick manual testing, no installation)

Open an **elevated** PowerShell and run:
```powershell
cd ParentalControl
dotnet run --project src/ParentalControl.Service
```
It runs in the foreground and logs to the console. Press `Ctrl+C` to stop it — this also
restores the original DNS adapter settings before exiting. Useful for verifying behavior before
installing it as a real service, but it stops when the terminal closes and only has permission
to read the current user's browser profiles (plus any others, if run as an admin account with
access to them).

### Option B — Install as a Windows Service (persists across logoff/reboot)

In an **elevated** PowerShell:
```powershell
dotnet publish src/ParentalControl.Service -c Release -o publish/service
sc.exe create ParentalControlService binPath= "C:\full\path\to\publish\service\ParentalControl.Service.exe" start= auto
sc.exe start ParentalControlService
```
Replace the `binPath=` value with the actual full path to the published `.exe` (note the
required space after `binPath=` and `start=` — that's `sc.exe` syntax, not a typo). Installed
this way it runs as `LocalSystem`, which is why it can read every Windows user's browser
profiles and modify the `hosts` file / DNS settings.

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
