# Agent Speed — Windows Traffic Helper Specification

Per-process network byte counts on Windows are only available to privileged code. This document specifies the small
service that provides them to the per-user app (METRICS 10.3), how it is installed, updated and removed, and its
security model. Everything else about traffic (attribution, rates, history, UI) lives in METRICS 10 and DESIGN 10;
the macOS app needs no helper.

## 1. Why a service, and which account

Measured on the Windows test machine (Windows 11 build 28000, Core Ultra 9 285H, 2026-10-22):

| Who starts the ETW session | Result |
| --- | --- |
| The interactive user's normal (UAC-filtered) token | `StartTrace` → `ERROR_ACCESS_DENIED` (5) |
| `NT AUTHORITY\LOCAL SERVICE` | session created; `EnableTraceEx2` on Microsoft-Windows-Kernel-Network and Microsoft-Windows-Kernel-Process succeed; events received |
| Administrator (elevated) | works |

The per-provider security descriptors on that build (`HKLM\SYSTEM\CurrentControlSet\Control\WMI\Security`):

| Provider | DACL |
| --- | --- |
| Microsoft-Windows-Kernel-Network `{7DD42A49-5329-4832-8DFD-43D979153A88}` | `D:(A;;0x120fff;;;SY)(A;;LO;;;LS)(A;;0x1207ff;;;BA)` — SYSTEM, LOCAL SERVICE (`0x80` = `TRACELOG_GUID_ENABLE`), Administrators |
| Microsoft-Windows-Kernel-Process `{22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716}` | same |
| Default for sessions (`0811c1af-…`) | Everyone register only; SYSTEM, LOCAL SERVICE, NETWORK SERVICE, Administrators full; Performance Log Users `0xee5` |
| NT Kernel Logger (`SystemTraceControlGuid`) | SYSTEM, Administrators and two service SIDs only |

Consequences:

- A **real-time session with the two manifest providers** (not the NT Kernel Logger) is enough, and **LOCAL SERVICE**
  is the least-privileged built-in account allowed to enable them. The service therefore runs as LocalService — not as
  LocalSystem and not as an administrator — although installing it needs administrator approval once.
- **Performance Log Users** membership (the "no resident service" alternative) does not work: the Kernel-Network
  descriptor does not include that group. Even where it would, it lets every process of the user start sessions for
  most providers on the machine (DNS, WinINet, …), a far broader grant than one service that only hands out byte counts.
- Other options rejected: `GetPerTcpConnectionEStats` (admin to enable, TCP only, misses closed connections);
  WFP callout driver (kernel driver signing); `ConnectionProfile.GetAttributedNetworkUsageAsync` (per application, not
  per process — Gemini CLI and npm installs are just `node.exe`, tool subprocesses cannot be traced to their agent —
  and aggregated by the Data Usage service, not live; not tested further); SRUM (hourly, admin only).

Sources: [StartTrace](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracew)
("Only users with administrative privileges, users in the Performance Log Users group, and services running as
LocalSystem, LocalService, NetworkService can control event tracing sessions … Only users with administrative
privileges and services running as LocalSystem can control an NT Kernel Logger session"),
[ETW security defaults](https://geoffchappell.com/notes/windows/etw/security.htm), and the registry values above.

## 2. Components and files

| Item | Value |
| --- | --- |
| Binary | `AgentSpeedTrafficHelper.exe`, C# compiled with NativeAOT (one file, no runtime, no side-by-side DLLs), x64 and arm64, built by the release workflow and **embedded as a resource in `AgentSpeed.exe`** with its SHA-256. It is not a separate download, so the update flow of UPDATES 5 (one exe) is unchanged. |
| Install location | `%ProgramFiles%\Agent Speed\Traffic Helper\AgentSpeedTrafficHelper.exe` (inherits the Program Files ACL: Administrators, SYSTEM and TrustedInstaller write; Users read and execute). Nothing else is stored there. |
| Service | name `AgentSpeedTraffic`, display name `Agent Speed Traffic Helper`, description "Counts network bytes per process for Agent Speed. Reads no content." (English; Windows shows service descriptions in one language), `SERVICE_WIN32_OWN_PROCESS`, **`SERVICE_DEMAND_START`**, error control normal, account `NT AUTHORITY\LocalService`, ImagePath quoted. |
| Service SID | `SERVICE_SID_TYPE_UNRESTRICTED` (`NT SERVICE\AgentSpeedTraffic`), used as the pipe owner. A *restricted* SID type is not used: ETW access checks would then also require the restricted SID, which the provider descriptors do not grant. |
| Required privileges | `SERVICE_CONFIG_REQUIRED_PRIVILEGES_INFO` = `SeChangeNotifyPrivilege` only; every other LocalService privilege (notably `SeImpersonatePrivilege` and `SeAssignPrimaryTokenPrivilege`) is removed from the process token. ETW access is granted by the descriptors, not by a privilege (to be confirmed by the first build: the scheduled-task test ran with LocalService's full privilege set). |
| Failure actions | restart after 5 s once; then none (reset after 1 day). |
| Service DACL | SYSTEM and Administrators: full. Each **allowed user** (below): `SERVICE_QUERY_STATUS`, `SERVICE_START`, `SERVICE_STOP`, `SERVICE_INTERROGATE`, `READ_CONTROL`. Authenticated Users: `SERVICE_QUERY_STATUS`. No one else, and no user gets `SERVICE_CHANGE_CONFIG`, `WRITE_DAC` or `WRITE_OWNER`. |
| Registry | `HKLM\SOFTWARE\AgentSpeed\TrafficHelper` (default HKLM ACL: admins write, users read): `Version` (REG_SZ, e.g. `1.0.0`), `Protocol` (REG_DWORD), `AllowedUsers` (REG_MULTI_SZ of user SIDs), `InstalledAt` (REG_SZ, ISO 8601). |
| ETW session | name `AgentSpeed-Traffic`, real-time, 64 KB buffers, 4 minimum / 32 maximum, flush timer 1 s, `EVENT_TRACE_REAL_TIME_MODE`, QPC clock. |
| Pipe | `\\.\pipe\AgentSpeed.Traffic` (section 4). |

The *allowed users* are the users who approved the install on this computer: the installer adds the SID of the user who
started it (passed by the app, confirmed against the elevated process's linked token where available) and removes it
on that user's uninstall. The service only exists while at least one allowed user remains.

## 3. What the service does

1. **Start** (only through the SCM, by an allowed user's app): `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`
   first (the binary is also linked with `/DEPENDENTLOADFLAG:0x800`, so even static imports — `kernel32`, `advapi32`,
   `sechost`, `tdh`, `ntdll` — resolve from System32 only). Command-line and SCM start arguments are ignored.
2. **Orphan cleanup**: if a session named `AgentSpeed-Traffic` exists (a previous instance crashed — real-time
   sessions outlive their controller, observed on the test machine), stop it, then start a new one.
3. **Providers**: Kernel-Network, level 4, keywords `0x30` (IPv4 | IPv6), with an event-ID filter
   (`EVENT_FILTER_TYPE_EVENT_ID`) for 10, 11, 26, 27 (TCP send/receive v4/v6) and 42, 43, 58, 59 (UDP send/receive);
   Kernel-Process, level 4, keyword `0x10` (process), event IDs 1 (start) and 2 (stop). If a provider rejects the
   event-ID filter, it is enabled without it and other IDs are dropped in the callback (they are few: connect,
   disconnect and retransmit events were 2 % of the test trace).
4. **Process table**: at start, one `NtQuerySystemInformation(SystemProcessInformation)` snapshot (PID, parent PID,
   session ID, create time — no handles opened); afterwards the Kernel-Process start/stop events keep it current, plus
   a fresh snapshot every 30 s to heal missed events. An entry is keyed by **(PID, create time)** (FILETIME).
5. **Counting**: for each network event, read `PID` and `size` and both addresses; drop the event when either address
   is loopback (`127.0.0.0/8`, `::1`, `::ffff:127.0.0.0/104`), when `size` is 0, or when PID ≤ 4 (Idle/System).
   Otherwise add `size` to the PID's current entry: receive events (11, 27, 43, 59) to `rx`, send events (10, 26, 42,
   58) to `tx`. Field offsets are resolved once per (event ID, version) with `TdhGetEventInformation` (verified layout
   on build 28000, version 0: `PID` u32 @0, `size` u32 @4, `daddr` @8, `saddr` @12 (IPv4) or @24 (IPv6), ports after);
   events whose layout lacks these fields, or whose `UserDataLength` is too short, are counted in `unparsedEvents` and
   ignored. Addresses and ports are read only to make the loopback decision and are never stored.
6. **Process stop**: the entry is marked exited and kept 15 s (so the app's next 1 Hz poll sees the final counts),
   then dropped. The table holds at most 16 384 entries; beyond that, exited entries are dropped first, then the least
   recently updated (counted in `evictedEntries`).
7. **Idle stop**: when no client has been connected for 120 s, the service stops its session and exits
   (`SERVICE_STOPPED`). It therefore runs only while an Agent Speed with traffic sampling enabled is running.
8. **Stop / shutdown**: `ControlTrace(STOP)`, close the pipe, exit within 2 s.

Counters are cumulative **since the service saw the process** (not since the process started). Each start of the
service gets a new `epoch` (its start FILETIME), so the app knows when to rebaseline (METRICS 10.4).

Measured consumer cost (a .NET Framework test consumer with the same parsing, on the test machine): 765 events/s
(normal desktop use) → **0.10 %** of one core; a loopback flood at ≈ 1.4 GB/s producing 40 900 events/s → **0.83 %**;
no events or buffers lost; 16 MB working set. A real download of 38.7 MB produced 12 768 receive events (≈ 3 KB per
event) and the per-PID sum matched curl's byte count within 0.16 % (TLS and HTTP overhead). At 1 Gbit/s that is
≈ 40 000 events/s, i.e. under 1 % of one core. Budget: ≤ 0.3 % at idle desktop rates, ≤ 1.5 % at 1 Gbit/s, ≤ 20 MB
working set.

## 4. IPC

**Transport**: one named pipe, `\\.\pipe\AgentSpeed.Traffic`, byte mode, created with `FILE_FLAG_FIRST_PIPE_INSTANCE`
(for the first instance), `PIPE_REJECT_REMOTE_CLIENTS`, at most 4 instances, overlapped I/O.

**Pipe security descriptor**: owner `NT SERVICE\AgentSpeedTraffic`; DACL `D:P` (protected) with: SYSTEM full; the
service SID full; each allowed user `FILE_GENERIC_READ | FILE_WRITE_DATA` (no create-instance right); an explicit deny
for `NETWORK` (S-1-5-2). Default medium integrity label (low-integrity and AppContainer processes cannot connect).

**Client side** (the app, unprivileged):

1. `CreateFile` with `SECURITY_SQOS_PRESENT | SECURITY_IDENTIFICATION`, so the server can never impersonate the user.
2. Verify the server before reading anything: `GetNamedPipeServerProcessId` must equal the `dwProcessId` that
   `QueryServiceStatusEx(AgentSpeedTraffic)` reports for a running service, and that process's image path
   (`QueryFullProcessImageName`) must equal the install location. Any mismatch → disconnect, status `helperDenied`
   (logged as possible pipe squatting).
3. Poll once per second (METRICS 10.4); never more than 4 requests per second.

**Server side**: the server identifies the client with `GetNamedPipeClientProcessId` and `GetNamedPipeClientSessionId`
(no impersonation). A snapshot contains **only processes in the client's session** (session from the process table),
so two users on one machine never see each other's per-process traffic.

**Messages** — little-endian, fixed layout, protocol version 1:

| Message | Layout |
| --- | --- |
| Request (exactly 16 bytes) | `u32 magic = 0x48545341` ("ASTH") · `u16 version` · `u16 type` (1 = hello, 2 = snapshot) · `u32 sequence` · `u32 reserved = 0` |
| Response header (32 bytes) | `u32 magic` · `u16 version` · `u16 type` (request type; `| 0x8000` on error) · `u32 status` (0 ok, 1 unsupportedVersion, 2 badRequest, 3 rateLimited, 4 overloaded) · `u32 sequence` (echo) · `u64 epoch` (service start FILETIME) · `u32 payloadLength` · `u32 reserved` |
| Hello payload (40 bytes) | `u16 minProtocol` · `u16 maxProtocol` · `u32 helperVersion` (`major << 16 | minor << 8 | patch`) · `u32 flags` (bit 0 session healthy) · `u32 reserved` · `u64 eventsLost` · `u64 unparsedEvents` · `u64 evictedEntries` |
| Snapshot payload | `u64 sampleTime` (FILETIME) · `u32 count` (≤ 16 384) · `u32 reserved` · `count ×` entry (32 bytes): `u32 pid` · `u32 flags` (bit 0 exited) · `u64 createTime` (FILETIME) · `u64 rx` · `u64 tx` |

Validation: a request that is not exactly 16 bytes, has the wrong magic, a non-zero reserved field or an unknown
type gets `badRequest` and the connection is closed; a version outside `[minProtocol, maxProtocol]` gets
`unsupportedVersion` with the hello payload (so the app can tell what to update); more than 4 requests in any second
get `rateLimited` (no payload); a client that sends nothing for 60 s is disconnected. Reads time out after 2 s; the
server never allocates based on client input. The protocol is read-only: there is no message that changes anything.

**Versions**: the helper has its own semantic version (independent of the app) and protocol version. The app embeds
`bundledHelperVersion`, `minimumHelperVersion` (raised only for security fixes) and the protocol range it speaks.

## 5. Install, update, remove

All privileged steps run in **one elevated process per action**: the embedded helper binary itself, started with
`ShellExecuteEx` (`runas`) — one UAC prompt. It is NativeAOT and loads only System32 DLLs; `AgentSpeed.exe` (a .NET
single-file app that extracts native libraries to a user-writable folder) is never run elevated.

**Install** (Settings → 網路流量 → `traffic.helper.install`, or `install.ps1 -TrafficHelper`):

1. The app shows `traffic.helper.uacNote`, writes the embedded helper to a new private folder
   `%LOCALAPPDATA%\AgentSpeed\helper-staging\<random>` (inherits the user-only ACL of the profile), verifies its
   SHA-256 against the embedded value, and starts it elevated: `AgentSpeedTrafficHelper.exe install --user <SID>
   --sha256 <hex>`.
2. The elevated helper: reads its own file (opened without write or delete sharing) into memory and checks the hash;
   creates `%ProgramFiles%\Agent Speed\Traffic Helper\` (or verifies its owner is Administrators/TrustedInstaller and
   that no non-admin principal has write access — otherwise it refuses and reports `helperError`); writes
   `AgentSpeedTrafficHelper.exe.new` from memory, flushes it, and renames it over the target (`MoveFileEx` with
   `REPLACE_EXISTING | WRITE_THROUGH`); creates or reconfigures the service (section 2), writes the registry key, adds
   the user to `AllowedUsers`, rebuilds the service DACL, and exits with a result code (0 ok, 1 refused, 2 failed).
3. The app deletes the staging folder, starts the service (`StartService`), connects, and shows
   `traffic.helper.installed`. A cancelled UAC prompt changes nothing and shows `traffic.helper.cancelled`.

**Update**: on launch (and after an app update) the app compares the installed `Version` with `bundledHelperVersion`.
Newer bundled → Settings and the dashboard's network section show `traffic.helper.outdated` with
`traffic.helper.update` (no prompt appears by itself). The elevated update is the install sequence; when the service is
running it is stopped first (`ControlService(STOP)`, wait ≤ 5 s) and restarted after the swap. If the user declines:

- installed version ≥ `minimumHelperVersion`: the app keeps using it (protocol permitting) and reminds once per app
  version;
- otherwise the app stops the service (it holds `SERVICE_STOP`), never starts it again, and per-process traffic is
  unavailable (`helperOutdated`) until the user updates or removes it. The old binary stays on disk (removing it needs
  administrator approval), but nothing runs it: the service is demand-start and only allowed users' apps start it.

The helper changes rarely; its version moves only when its code or protocol changes, so most app updates need no
prompt. `install.ps1` without `-TrafficHelper` never touches an installed helper; with it, it installs or updates
(one prompt).

**Remove** (Settings `traffic.helper.remove` with `traffic.helper.removeConfirm` / `traffic.helper.removeDetail`;
`install.ps1 -Uninstall`; the Apps & Features uninstall entry, whose `uninstall.ps1` elevates only when the helper is
installed and this user is in `AllowedUsers`): the elevated helper (copied to a temporary folder first, since a running
image cannot delete itself) removes this user from `AllowedUsers`; if the list is now empty: stops the service, stops a
leftover `AgentSpeed-Traffic` session, deletes the service, the registry key and the install folder (a file still in
use is deleted at the next reboot with `MOVEFILE_DELAY_UNTIL_REBOOT`). History already recorded is kept (DESIGN 10).
`install.ps1 -Purge` additionally deletes the user's data as today.

**Installer changes** (`install.ps1`): new switch `-TrafficHelper` (after the per-user install, run the elevated
install above via `Start-Process -Verb RunAs` on the embedded helper extracted by `AgentSpeed.exe --extract-helper
<dir>`; localized messages for waiting, done, cancelled); `-Uninstall` removes the helper as described; the generated
`uninstall.ps1` does the same. The installer itself is never elevated as a whole.

## 6. App behaviour

- Traffic sampling enabled and the helper installed: on launch and whenever per-process sampling is needed (METRICS
  10.10), `StartService` if stopped, connect, `hello`, then 1 Hz snapshots. Disabled: disconnect; the service stops
  itself after 120 s.
- States shown in Settings (DESIGN 10): `notInstalled`, `idle` (installed, stopped), `running`, `outdated`,
  `denied` (verification failed or access denied), `error` (with the Win32 error text). Connection failures retry
  after 1, 2, 4 … 60 s.
- Machine totals (METRICS 10.2) never depend on the helper.

## 7. Threat model

| Threat | Mitigation |
| --- | --- |
| **Local privilege escalation through the service binary** (a user replaces the privileged executable) | Lives in Program Files, writable only by administrators; the installer refuses a pre-existing folder that a non-admin can write; ImagePath quoted (no unquoted-path hijack); no configuration, plug-in or script is ever loaded from a user-writable location. |
| **DLL search-order / binary planting** | NativeAOT single file; `SetDefaultDllDirectories(System32)` and `/DEPENDENTLOADFLAG:0x800`; no `LoadLibrary` of relative names; no adjacent files. The elevated install step is the same NativeAOT binary, never the .NET single-file app. |
| **Abusing the service as a confused deputy** | The protocol has no write operation and no input other than the request type; the service reads no file, path or registry value named by a client; it never impersonates. |
| **Elevation through the service token** | LocalService, not SYSTEM; privileges stripped to `SeChangeNotifyPrivilege` (no `SeImpersonatePrivilege`, so "potato"-style token abuse has nothing to work with); no network access needed or used. |
| **Pipe squatting** (another process creates the pipe first to feed fake data or capture clients) | Clients verify the server PID against the SCM and its image path; clients connect with `SECURITY_IDENTIFICATION`, so a fake server cannot impersonate them. The service creates the pipe with `FILE_FLAG_FIRST_PIPE_INSTANCE` and exits if the name is taken (a denial of service only, reported as `helperDenied`). |
| **Information disclosure between users** | Pipe ACL limited to allowed users; snapshots limited to the client's session; only PIDs, create times and byte counts leave the service — never addresses, ports, host names, payloads or image names. |
| **Remote access** | `PIPE_REJECT_REMOTE_CLIENTS` and a deny ACE for NETWORK. |
| **Denial of service** (flooding requests or events) | ≤ 4 pipe instances, ≤ 4 requests per second per connection, fixed-size responses (≤ 16 384 entries), bounded process table, bounded ETW buffers (lost events are counted, never queued without limit); a misbehaving client affects only its own connection. Event floods cost ≈ 0.2 µs per event (section 3). |
| **Stale privileged code after updates** | Helper versioned separately; `minimumHelperVersion` stops the app from starting an outdated helper; demand-start, so a declined update leaves a stopped service that nothing starts. |
| **Tampered installer input** (malware already running as the user swaps the staged binary) | Out of scope: such code can show its own elevation prompt. The prompt is the trust boundary. Releases are not Authenticode-signed, so Windows shows the publisher as unknown; `traffic.helper.uacNote` says so before the prompt. Signing (open question) would let the prompt show a verified publisher and would allow silent, signature-checked helper updates. |
| **Orphaned ETW session** after a crash | Stopped at the next start (section 3); a session left running without a consumer only drops events into its bounded buffers. |
| **Uninstall leftovers** | Service, registry key, folder and ETW session are removed; files in use are scheduled for deletion at reboot. |

## 8. Tests (definition of done)

- Unit: request validation (every malformed variant → `badRequest`, connection closed), rate limiting, snapshot
  encoding, loopback filter (IPv4, IPv6, mapped), PID ≤ 4, exited-entry retention, table eviction.
- Integration on a test VM: install as a standard user via UAC; verify the service account, privileges (`sc qprivs`),
  SID type (`sc qsidtype`), service DACL (`sc sdshow`), pipe DACL, folder ACL; a non-allowed user and a NETWORK logon
  cannot connect; a squatting pipe is rejected by the client; download a known file and compare the reported bytes
  with the file size (± 1 %); kill the service and confirm the orphaned session is cleaned up at the next start;
  update with the app running; remove and confirm nothing is left (service, key, folder, session).
- Performance: the budgets of section 3 with the benchmark of METRICS 10.10.
