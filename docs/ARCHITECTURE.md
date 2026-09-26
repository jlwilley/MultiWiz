# MultiWiz v4 architecture

MultiWiz v4 is a from-scratch rewrite of the Wizard101 / Pirate101 multi-account launcher. It is a
Windows-only desktop app built on **.NET 10**, **Avalonia 12**, **CsWin32**, and **Velopack**.

The ground rules:

- **Never modify the game.** No writing game memory, no injecting code, no hooking, no packet
  tampering, no gameplay automation. Everything MultiWiz does to a game client is OS-level: starting
  the process, posting keystrokes to its window during login, moving and resizing its window,
  adjusting its audio-session volume and CPU priority, and (in the future) **reading** its memory and
  pixels for overlays.
- **Read-only live data.** The planned live overlays (stats, duel info, damage estimates) read game
  memory through `IProcessMemory`, which by contract opens processes with read/query rights only.
- **Every module is replaceable.** The UI talks to Core services, Core talks to the OS through
  interfaces in `MultiWiz.Core.Platform`, and only `MultiWiz.Platform.Windows` touches Win32.

## Project files

`global.json`, `Directory.Build.props`, `Directory.Packages.props` (all package versions, pinned
from primary sources on 2026-09-25), `MultiWiz.slnx`, and every `.csproj` already exist. Add packages
only through `Directory.Packages.props`, and say so in your report.

## Solution layout

```
MultiWiz.slnx
global.json                      .NET SDK pin
Directory.Build.props            shared MSBuild properties (version, nullable, etc.)
Directory.Packages.props         central package versions (ManagePackageVersionsCentrally)
src/
  MultiWiz.Core/                 net10.0; no Windows or UI dependencies; all app logic
  MultiWiz.Platform.Windows/     net10.0-windows10.0.19041.0; CsWin32 + NAudio.Wasapi implementations
  MultiWiz.App/                  net10.0-windows10.0.19041.0; Avalonia UI, composition root, updates
tests/
  MultiWiz.Core.Tests/           net10.0; xUnit; Core logic with fakes
.github/workflows/
  ci.yml                         build + test on every push/PR (windows-latest)
  release.yml                    tag-triggered Velopack release with channels + Azure Artifact Signing
docs/
  ARCHITECTURE.md                this file
  RELEASING.md                   how to cut stable/beta releases and set up signing
  ROADMAP.md                     phased feature plan
```

Dependencies point one way: `App -> Platform.Windows -> Core`, `App -> Core`, `Core.Tests -> Core`.

## Contracts (already written; do not change their public shape without a strong reason)

These files in `src/MultiWiz.Core` define the agreement between modules:

| Area | Files |
|---|---|
| Primitives | `Primitives/PixelRect.cs` (`PixelRect`, `PixelSize`) |
| Games | `Games/GameKind.cs`, `Games/Realm.cs`, `Games/BuiltInRealms.cs`, `Games/IRealmCatalog.cs`, `Games/InstallSource.cs`, `Games/GameInstall.cs`, `Games/GameExecutables.cs`, `Games/IInstallCatalog.cs` |
| Accounts | `Accounts/Account.cs`, `Accounts/IAccountStore.cs` |
| Teams | `Teams/Team.cs`, `Teams/WindowLayout.cs` (`LayoutCell`, `WindowLayout`), `Teams/BuiltInLayouts.cs`, `Teams/ITeamStore.cs`, `Teams/ITeamLauncher.cs` (`IWindowArranger`, `ITeamLauncher`) |
| Settings | `Settings/AppSettings.cs` (all settings records + `ThemePreference`, `UpdateChannel`), `Settings/ISettingsStore.cs` |
| Hotkeys | `Hotkeys/HotkeyAction.cs`, `Hotkeys/HotkeyModifiers.cs`, `Hotkeys/HotkeyBinding.cs`, `Hotkeys/VirtualKeys.cs`, `Hotkeys/DefaultHotkeys.cs`, `Hotkeys/IHotkeyCoordinator.cs` |
| Sessions | `Sessions/ClientSession.cs` (`ClientSession`, `ClientSessionState`), `Sessions/ISessionManager.cs`, `Sessions/ISessionEvents.cs` |
| Legacy | `Legacy/ILegacyImporter.cs` (`ILegacyImporter`, `LegacyImportPreview`, `LegacyAccount`, `LegacySettings`) |
| Switching | `Switching/IClientSwitcher.cs` |
| Platform interfaces | `Platform/*.cs`: `IDisplayService` + `MonitorInfo`, `IWindowService`, `IWindowEvents` (+ `ForegroundChangedEventArgs`, `WindowTrackingUpdate`), `IInputSender`, `IProcessLauncher` (+ `ProcessLaunchRequest`, `ILaunchedProcess`), `IHotkeyService`, `IAudioService` (+ `VolumeTarget`), `IProcessThrottler`, `IInstallLocator`, `IThumbnailService` (+ `IWindowThumbnail`), `IOverlayWindowStyler`, `ISteamSupport` (+ `SteamReadiness`) |
| Security | `Security/ICredentialVault.cs`, `Security/ISecretProtector.cs` |
| Storage | `Storage/AppPaths.cs` |
| Live data | `Live/IProcessMemory.cs` (`IProcessMemory`, `IProcessMemoryFactory`, `ModuleInfo`) |

If an implementer truly needs an extra member on a contract, add it in a backward-compatible way and
list the change in their report.

## MultiWiz.Core (implementation to add)

Namespaces follow folders (`MultiWiz.Core.<Folder>`). Everything is thread-safe unless noted. Use
`ILogger<T>` (Microsoft.Extensions.Logging.Abstractions) for logging and `TimeProvider` for every delay
and timestamp so tests can use `FakeTimeProvider`.

### Games
- `BuiltInRealms` is written (w101-us, w101-eu, w101-test, p101-us).
- `RealmCatalog : IRealmCatalog`: built-ins plus `AppSettings.CustomRealms`; `GetAll()`,
  `Find(id)`, `ForGame(game)`, `DefaultFor(game)` (w101-us / p101-us).
- `LaunchArguments.Build(Realm realm, GameInstall install)` → string. Order: `-ST` if
  `install.SteamAppId` is set, then `-L {host} {port}`, then `install.ExtraArguments` if any.
- `SteamLibraryParser` (static, pure): `ParseLibraryFolders(string vdfText)` → library root paths
  from `libraryfolders.vdf` (handles both the modern `"path"` form and the legacy `"1" "D:\\Steam"` form,
  unescapes `\\`); `ParseAppManifest(string acfText)` → `(string AppId, string InstallDir)?` from
  `appmanifest_<id>.acf`. A tiny tolerant VDF tokenizer is fine.
- `InstallCatalog : IInstallCatalog`: combines `IInstallLocator.Discover()` (cached; `Refresh()`
  re-runs it) with `AppSettings.CustomInstalls`. `GetAll()`, `Find(id)`, `ForGame(game)`,
  `Resolve(Account)` → the account's `InstallId` if it exists, else `PreferredInstallIds[game]`,
  else the first discovered install for that game (Standalone before Steam before Custom), else null.

### Storage
- `JsonFileStore` (static): `T? Load<T>(string path, JsonTypeInfo<T> typeInfo)` (returns null if the
  file is missing; on corrupt JSON, renames the file to `<name>.corrupt-<yyyyMMddHHmmss>` and returns
  null, logging a warning via an optional `ILogger`) and `Save<T>(string path, T value, JsonTypeInfo<T>)`
  (creates the directory, writes `<path>.tmp`, flushes, then `File.Move(tmp, path, overwrite: true)`).
- `CoreJsonContext : JsonSerializerContext` (System.Text.Json source generation) with camelCase names,
  indented output, string enums, and `[JsonSerializable]` for `AppSettings`, `AccountsDocument`,
  `TeamsDocument`.
- `AccountsDocument { int SchemaVersion = 1; List<Account> Accounts }`,
  `TeamsDocument { int SchemaVersion = 1; List<Team> Teams }`.
- `JsonAccountStore : IAccountStore`, `JsonTeamStore : ITeamStore`, `JsonSettingsStore : ISettingsStore`
  over `AppPaths`. Load lazily on first use, save on every mutation, raise `Changed` after saving
  (outside the lock). `JsonSettingsStore` clamps obviously invalid values on load (volumes 0..100,
  opacity 0.2..1, delays >= 0).

### Teams
- `BuiltInLayouts` is written (none, side-by-side, grid-2x2, grid-3x2, main-plus-3, one-per-monitor).
- `LayoutCalculator.Arrange(WindowLayout layout, IReadOnlyList<MonitorInfo> monitors, int windowCount)`
  → `IReadOnlyList<PixelRect>` in slot order; empty for `none` or no monitors; cells repeat when there
  are more windows than cells; `MonitorIndex` wraps modulo the monitor count; rectangles are computed
  from the monitor **work area** and rounded so neighbouring cells share edges exactly.
- `WindowArranger : IWindowArranger`: `Arrange(IReadOnlyList<ClientSession> orderedSessions,
  WindowLayout layout, bool resize)` using `IDisplayService` + `IWindowService.SetBounds`. Skips
  sessions without a window. Returns how many windows were placed.
- `TeamLauncher : ITeamLauncher`: `LaunchAsync(Guid teamId, CancellationToken)`:
  sets the switcher's active team, calls `ISessionManager.LaunchManyAsync` for the team's accounts that
  are not already alive, then arranges all of the team's windows with its layout (if not `none`),
  records `LastTeamId` in settings. Returns the sessions. `ArrangeNow(teamId)` arranges the running
  windows immediately.

### Sessions
- `SessionManager : ISessionManager, ISessionEvents`. Per launch:
  1. Resolve account (`IAccountStore`), realm (`IRealmCatalog`), install (`IInstallCatalog.Resolve`).
     Missing install or executable → Failed session with a clear message
     ("Wizard101 was not found. Set the game folder in Settings → Games.").
  2. Steam installs (`SteamAppId` set): call `ISteamSupport.EnsureReady(install)` (see Platform; it
     checks Steam is running + signed in, starts Steam and waits up to 3 minutes if needed, and writes
     `Bin\steam_appid.txt`). Failure → Failed session with its message.
  3. `IProcessLauncher.Start(new ProcessLaunchRequest(install.ExecutablePath, install.BinPath,
     LaunchArguments.Build(realm, install)))` → state `WaitingForWindow`.
  4. Poll `IWindowService.FindMainWindow(pid)` every 250 ms until found or
     `LoginSettings.WindowTimeoutSeconds` elapses (→ Failed "The game window never appeared").
     Also fail fast if the process exits.
  5. If `AutoLogin`: state `WaitingForReady`, wait `ReadyDelaySeconds`, then acquire the **login gate**
     (a `SemaphoreSlim(1)` shared by all sessions so only one client is typed into at a time), state
     `LoggingIn`, read the password from `ICredentialVault` (missing → Failed "No saved password"),
     then `IInputSender`: username, Tab (`VirtualKeys.Tab`), password, Enter (`VirtualKeys.Enter`) with
     `KeystrokeDelayMs` between characters and 100 ms between fields. Release the gate.
  6. State `Running`. If `RefocusAfterLogin`, raise `ISessionEvents.LoginCompleted` (the UI brings
     itself forward).
  7. Watch `ILaunchedProcess.WaitForExitAsync`; on exit → state `Exited`, remove from `Sessions`,
     call `IAudioService.Release(pid)` and `IProcessThrottler.Release(pid)`.
  `LaunchManyAsync`: start launches `StaggerSeconds` apart (don't wait for each login before starting
  the next; the login gate already serializes typing), await all. `Stop` kills the process and lets the
  exit watcher clean up. Publish every state change through `SessionChanged` (outside locks).
- Sessions are keyed by account id; launching an account that is already alive returns its session.
- External clients: every 3 s (a `TimeProvider` timer; the manager is `IDisposable`) while
  `GeneralSettings.DetectExternalClients` is on, `IWindowService.FindGameWindows()` lists the game-class
  windows; a process no session tracks, at least 10 s old (so a client MultiWiz is starting is never
  raced), whose `IProcessLauncher.TryAttach` reports a client process name and start time, becomes a
  Running session with `IsExternal = true`, a `Label` ("Wizard101 client 1", lowest free number per
  game), `Game`, and a synthetic `AccountId` derived from pid + start time. It is watched for exit like
  any other session but never written to `running-clients.json`; Retype login refuses it. Turning the
  setting off drops external sessions without closing them. `ISessionLinking.LinkExternal(externalId,
  accountId)` re-keys one to an idle account of the same game (publishing Exited for the old id, then
  the account's snapshot) and records it for re-adoption.

### Switching
- `ClientSwitcher : IClientSwitcher`. Maintains order as described on the interface. Listens to
  `ISessionManager.SessionChanged`, `ITeamStore.Changed`, `IAccountStore.Changed` to rebuild order, and
  to `IWindowEvents.ForegroundChanged` to update `Current` when the foreground window belongs to a
  session (ignore foreground changes to non-game windows: the last game stays `Current`).
  `FocusSlot/Next/Previous/Focus` call `IWindowService.Focus(hwnd)` **synchronously on the calling
  thread** (hotkey callbacks arrive on the platform message thread that holds foreground rights), set
  `Current`, then schedule focus effects. Next/Previous wrap around.
- `FocusEffects` (internal, owned by `ClientSwitcher`): debounced (~120 ms, via `TimeProvider`) on a
  background task: `AudioPolicy.Compute` → `IAudioService.Apply`; and for every alive session
  `IProcessThrottler.Apply(pid, background: pid != focusedPid, settings.Performance...)` when either
  performance option is on (and `Release` for all when both are turned off).
- `AudioPolicy.Compute(IReadOnlyList<int> processIds, int? focusedProcessId, AudioSettings settings)`
  → `IReadOnlyList<VolumeTarget>`; empty when `Enabled` is false; focused → `FocusedVolumePercent`,
  others → `UnfocusedVolumePercent`; when nothing is focused, everyone gets `FocusedVolumePercent`.
  When the audio setting is turned off, call `IAudioService.RestoreAll()` once.

### Hotkeys
- `HotkeyCoordinator : IHotkeyCoordinator`: `Start()` registers every action whose effective binding
  (`DefaultHotkeys.Resolve`) parses and is valid, via `IHotkeyService.TryRegister`; re-registers
  everything on `ISettingsStore.Changed` when `Hotkeys` changed; `Stop()` unregisters.
  Focus actions go straight to `IClientSwitcher` (`FocusSlot1..8` → `FocusSlot(0..7)`,
  `NextClient`, `PreviousClient`). Every other action raises `UiActionRequested`. Fills
  `FailedActions` and raises `RegistrationsChanged` after each (re)registration. Registers nothing when
  `HotkeySettings.Enabled` is false.

### Legacy import (v3 → v4)
- `LegacyImporter : ILegacyImporter`: `ReadPreview()` reads `AppPaths.LegacyConfigFile` (v3
  `config.txt`: one account per line `name,user,pass[,Server]`; user/pass are Base64 DPAPI
  (CurrentUser) blobs, decrypted with `ISecretProtector`; if Base64-decoding or decryption fails the
  field is used as plain text, like v3 did) and `AppPaths.LegacySettingsFile` (v3 `settings.txt`:
  `key=value` lines: `IsDarkModeEnabled`, `Wait`, `_muteWhenNotInFocus`, `_unmuteVolume`,
  `_switcherOpacity`). Server mapping: `Wizard101_US`→`w101-us`, `Wizard101_EU`→`w101-eu`,
  `Pirate101_US`→`p101-us` (+ `Game = Pirate101`), missing/unknown → `w101-us`.
  Returns null when there is no v3 config. `Apply(preview)` adds each account (new Guid, keeps order,
  skips exact duplicates of an existing username+realm) to `IAccountStore`, saves passwords to
  `ICredentialVault`, maps settings (dark mode → theme, `Wait` → `ReadyDelaySeconds` clamped 2..30,
  mute flag → `Audio.Enabled`, unmute volume → `FocusedVolumePercent`, opacity → `Switcher.Opacity`),
  sets `LegacyImportHandled = true`, and returns the number of accounts added. `Decline()` only sets
  `LegacyImportHandled`. Never modifies or deletes the v3 files.

### Live data (foundation only)
- `BytePattern`: `Parse("48 8B 05 ?? ?? ?? ??")` (hex bytes, `?`/`??` wildcards), `Length`,
  `Matches(ReadOnlySpan<byte>)`. `PatternScanner.IndexOf(ReadOnlySpan<byte> haystack, BytePattern)`
  → first index or -1 (use the first non-wildcard byte with `IndexOf` to skip ahead).
- `ProcessMemoryExtensions`: `TryRead<T>(this IProcessMemory, nint address, out T value) where T : unmanaged`,
  `TryReadPointer(this IProcessMemory, nint address, out nint value)` (64-bit),
  `TryReadString(this IProcessMemory, nint address, int maxBytes, out string value)` (UTF-8, stops at NUL),
  `TryFollowPointerChain(this IProcessMemory, nint baseAddress, ReadOnlySpan<int> offsets, out nint address)`.
- Nothing game-specific yet; offsets and type data will come from the owner's own database later.

### Patching (game file downloads)
- `IGameDownloader` / `GameDownloader` download Wizard101's files from KingsIsle's patch server,
  verified by KingsIsle's size and CRC (`KiCrc32`: reflected 0xEDB88320, initial 0, no final XOR; not
  the standard CRC-32). `PatchServerClient` does the TCP handshake with `patch.us.wizard101.com:12500`
  (`PatchProtocol`: 0xF00D framing, SessionOffer/SessionAccept, "latest file list v2" = service 8,
  order 2) behind `IPatchConnectionFactory`; `FileListParser` reads the DML tables of
  LatestFileList.bin; `PatchPaths` rejects any path that escapes the install.
- `PlanAsync(install, FullGame | Update)` checks files (size, then CRC; a size+mtime+CRC cache lives in
  `state\patch-cache-<installId>.json`); `DownloadAsync` fetches 4 files at a time over one shared
  `HttpClient` (User-Agent "KingsIsle Patcher"), writes each to a temp file next to its target, verifies
  it, then moves it into place; 3 attempts per file; locked files are reported, not retried; after a
  clean run it updates `PatchInfo\` and `LocalPackagesList.txt` like the official patcher.
  `CheckForUpdateAsync` compares only the Base package (the "out of date" banner).
- Standalone Wizard101 only: Steam installs and Pirate101 are reported as unsupported by `GetSupport`.
  The App (`GameFilesViewModel`, `GameFilesService`) refuses to download while any client or the
  official launcher runs.

### DI
- `CoreServiceCollectionExtensions.AddMultiWizCore(this IServiceCollection services, AppPaths paths)`
  (namespace `MultiWiz.Core`)
  registers every Core service above as singletons (interfaces + concrete where the UI needs the
  concrete type), `TimeProvider.System`, and `paths`. Platform services are registered separately.

## MultiWiz.Platform.Windows

`net10.0-windows10.0.19041.0`, `AllowUnsafeBlocks`, `Microsoft.Windows.CsWin32` (Win32 P/Invoke is
generated from `NativeMethods.txt`), `NAudio.Wasapi` for audio sessions,
`System.Security.Cryptography.ProtectedData` for DPAPI. Implements every Core platform interface:

- `Win32MessageThread` (internal, singleton): a dedicated background thread with a message-only window
  (`HWND_MESSAGE`) and a `GetMessage` loop. Provides `Invoke(Action)` / `InvokeAsync(Func<T>)`
  marshaling (post a custom message + a queue), hosts `WM_HOTKEY` handling and WinEvent hooks
  (`WINEVENT_OUTOFCONTEXT` callbacks are delivered to the hooking thread's message loop). Keeps every
  delegate passed to native code alive in fields. Shut down cleanly on `Dispose`.
- `HotkeyService : IHotkeyService` — `RegisterHotKey` on the message window with `MOD_NOREPEAT`
  added; unique ids; callbacks run on the message thread; returns null when registration fails.
- `WindowEventsService : IWindowEvents` — one global `EVENT_SYSTEM_FOREGROUND` hook →
  `ForegroundChanged` (with `GetWindowThreadProcessId`). `TrackWindow`: per-process
  `EVENT_OBJECT_LOCATIONCHANGE` (filter `hwnd == target && idObject == OBJID_WINDOW`),
  `EVENT_SYSTEM_MINIMIZESTART/END`, `EVENT_OBJECT_DESTROY`, plus foreground changes; coalesce updates
  to ≤ 60/s; compute `ClientBounds` via `GetClientRect` + `ClientToScreen`; `IsClosed` when
  `!IsWindow`. Immediately invoke once with the current state.
- `WindowService : IWindowService` — `FindMainWindow` via `EnumWindows` (same pid, visible, no owner;
  prefer class name "Wizard Graphical Client" / "Pirate Graphical Client" if present, else largest
  area). `Focus`: restore if `IsIconic`; `SetForegroundWindow`; if that fails, simulate an ALT tap with
  `SendInput` and retry; last resort `AttachThreadInput` to the current foreground thread, then
  `BringWindowToTop` + `SetForegroundWindow`, detach. `GetBounds` via
  `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` (fallback `GetWindowRect`). `SetBounds`:
  restore if minimized/maximized, compute the invisible border delta (`GetWindowRect` minus extended
  frame bounds) and `SetWindowPos` with `SWP_NOZORDER | SWP_NOACTIVATE` (+ `SWP_NOSIZE` when not
  resizing). `SetBorderless`: toggle `WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX |
  WS_MAXIMIZEBOX` on `GWL_STYLE`, remember the original style per hwnd, `SWP_FRAMECHANGED`.
  Use `GetWindowLongPtr/SetWindowLongPtr` (64-bit).
- `InputSender : IInputSender` — `PostMessage(WM_CHAR)` per UTF-16 code unit; keys via
  `WM_KEYDOWN`/`WM_KEYUP` with `lParam` built from `MapVirtualKey(vk, MAPVK_VK_TO_VSC)`
  (repeat=1, scan code << 16, key-up sets bits 30 and 31), 30 ms between down and up.
- `ProcessLauncher : IProcessLauncher` — `System.Diagnostics.Process` with `UseShellExecute=false`,
  explicit working directory; `LaunchedProcess : ILaunchedProcess` wraps it (`Kill(entireProcessTree:
  false)`, swallow "already exited").
- `AudioService : IAudioService` — a dedicated MTA worker thread owning all NAudio COM objects.
  `Apply` replaces a pending "latest targets" slot and signals the worker (so rapid switches collapse).
  Cache `AudioSessionControl`s per pid (re-enumerate all active render devices when a pid is missing, at
  most every 750 ms per pid; drop cached sessions that throw). Remember the original
  `SimpleAudioVolume.Volume` the first time a pid is touched; `Release`/`RestoreAll` put it back.
  Dispose sessions and devices properly.
- `ProcessThrottler : IProcessThrottler` — `OpenProcess(PROCESS_SET_INFORMATION |
  PROCESS_QUERY_LIMITED_INFORMATION)`; EcoQoS via `SetProcessInformation(ProcessPowerThrottling,
  PROCESS_POWER_THROTTLING_STATE{ Version = 1, ControlMask = EXECUTION_SPEED, StateMask = on ?
  EXECUTION_SPEED : 0 })`; priority via `SetPriorityClass(BELOW_NORMAL_PRIORITY_CLASS /
  NORMAL_PRIORITY_CLASS)`. Track what was applied; `Release` reverts (ControlMask = 0 releases control
  back to the system).
- `DisplayService : IDisplayService` — `EnumDisplayMonitors` + `GetMonitorInfo(MONITORINFOEXW)` +
  `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` → `Scale = dpi / 96.0`; order: primary first, then by X, then Y.
- `InstallLocator : IInstallLocator` — Standalone: `GameExecutables.DefaultStandaloneRoot`, plus
  uninstall registry keys (HKLM 32/64-bit views and HKCU,
  `Software\Microsoft\Windows\CurrentVersion\Uninstall\*`) whose `DisplayName` starts with
  "Wizard101"/"Pirate101" and have an `InstallLocation`. Steam: `HKCU\Software\Valve\Steam\SteamPath`
  (fallback HKLM `InstallPath` in the 32-bit view) → `steamapps\libraryfolders.vdf` →
  each library's `steamapps\appmanifest_*.acf` whose installdir is "Wizard101"/"Pirate101"
  (use `SteamLibraryParser`) → root `steamapps\common\<installdir>`, `SteamAppId` = manifest id.
  Only return installs whose client exe exists. Deterministic ids: `standalone-wizard101`,
  `standalone-pirate101` (default path) or `standalone-<game>-<8 hex of SHA-256 of lowercase root>`,
  `steam-<game>-<appid>`. Deduplicate by root path (case-insensitive).
- `SteamSupport : ISteamSupport` — reads `HKCU\Software\Valve\Steam\ActiveProcess`
  `pid` (0 = not running) and `ActiveUser` (0 = not signed in); starts `SteamExe` if not running; polls
  every second until signed in or timeout; ensures `Bin\steam_appid.txt` contains the app id.
- `CredentialVault : ICredentialVault` — Windows Credential Manager (`CredWrite/CredRead/CredDelete/
  CredFree`), `CRED_TYPE_GENERIC`, `CRED_PERSIST_LOCAL_MACHINE`, target
  `MultiWiz:account:{accountId:N}`, `UserName` = username, blob = UTF-16LE password bytes (reject
  passwords longer than 1,280 chars). Zero temporary buffers where practical.
- `DpapiSecretProtector : ISecretProtector` — `ProtectedData.Protect/Unprotect` with
  `DataProtectionScope.CurrentUser`, no entropy (matches v3).
- `ThumbnailService : IThumbnailService` — `DwmRegisterThumbnail`, `DwmQueryThumbnailSourceSize`,
  `DwmUpdateThumbnailProperties` (`DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY |
  DWM_TNP_SOURCECLIENTAREAONLY`), letterboxing inside the requested rect, `DwmUnregisterThumbnail` on
  dispose.
- `OverlayWindowStyler : IOverlayWindowStyler` — `WS_EX_LAYERED | WS_EX_TRANSPARENT` (click-through
  only) `| WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`; after adding `WS_EX_LAYERED`, call
  `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` so composition-rendered content stays visible;
  `SetWindowPos(HWND_TOPMOST, …, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED)`.
- `ProcessMemoryFactory : IProcessMemoryFactory` / `ProcessMemory : IProcessMemory` —
  `OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION)` **only**; `ReadProcessMemory`
  (fail unless all bytes were read); modules via `EnumProcessModulesEx(LIST_MODULES_ALL)` +
  `GetModuleBaseName` + `GetModuleInformation` (these need `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`
  on older systems; if `QUERY_LIMITED` fails for module enumeration, reopen a second handle with
  `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` — still read-only).
- `WindowsPlatformServiceCollectionExtensions.AddWindowsPlatform(this IServiceCollection)` (namespace
  `MultiWiz.Platform.Windows`) registers all of the above as singletons against their Core interfaces.

## MultiWiz.App (Avalonia 12)

`WinExe`, `AssemblyName` **MultiWiz** (so the executable stays `MultiWiz.exe`, matching v3 and the
Velopack package id), app icon `Assets/multiwiz.ico` (the v3 magic-wand icon), app manifest with
`asInvoker` and no `dpiAwareness` (Avalonia sets Per-Monitor V2 at runtime; a manifest value would
override it).

- `Program.Main` (`[STAThread]`): `VelopackApp.Build().Run()` first; single instance via a named
  `Mutex` (`Local\MultiWiz.SingleInstance`); a second launch signals the first through a named
  `EventWaitHandle` (`Local\MultiWiz.Activate`) to show its main window, then exits. Then
  `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` with `ShutdownMode.OnExplicitShutdown`
  (tray keeps the app alive).
- Composition root (`AppHost`): `AppPaths.Default.EnsureCreated()`, Serilog rolling file log in
  `AppPaths.LogsDirectory` (7 days) wired into Microsoft.Extensions.Logging, `AddMultiWizCore`,
  `AddWindowsPlatform`, view models, `UpdateService`, `OverlayManager`, `DialogService`.
  Unhandled exception handlers log and show a friendly error dialog.
- Theming: Fluent theme; dark/light/system from settings; Catppuccin Mocha (dark) and Latte (light)
  palette as resource brushes (base, mantle, crust, surface0/1/2, overlay, text, subtext, mauve accent,
  green/red/yellow status colors). Rounded, spacious, modern.
- **Main window** (sidebar navigation, remembers size/position):
  - **Accounts** — list (drag handle / up-down reorder, accent color dot, name, game+realm, status chip
    driven by `ClientSession.State`), per-row Launch/Stop, Focus, Edit, Delete (confirm; also deletes
    the vault entry and removes the account from teams); multi-select → "Launch selected"; "Add account"
    dialog (display name, username, password with reveal, game, realm filtered by game, install
    "Automatic" or a specific one, accent color, notes); empty state with "Import from MultiWiz 3" when a
    v3 config exists.
    When clients started outside MultiWiz are running, a "Clients started outside MultiWiz: N" card lists
    them (label, process id, Focus) with a "Link to account…" picker of idle accounts of the same game.
  - **Teams** — list of teams; editor with name, ordered account slots (add/remove/reorder), layout
    picker (built-ins with a small preview drawing), "resize windows" toggle; "Launch team",
    "Arrange now", "Set as active" (switcher order).
  - **Settings** — General (theme, tray behavior, close games on exit, detect games started outside
    MultiWiz, updates + channel + "Check now"),
    Login (auto-login, ready delay, timeout, keystroke delay, stagger, refocus), Audio (enabled, focused /
    background volume sliders), Switcher (opacity, show on team launch, don't steal focus), Overlays
    (name badges), Performance (efficiency mode, lower priority), Hotkeys (one row per action with a
    capture box: click then press a combination; Esc cancels; Backspace clears; shows a warning icon for
    `FailedActions`; "Reset to defaults"), Games (discovered installs list, "Add game folder…" picker that
    validates the Bin exe, preferred install per game, custom realms editor), About (version, links, log
    folder button, legal disclaimer).
  - Status bar: running client count, update banner ("Update ready — restart to apply").
- **Switcher** (compact, always on top, draggable, remembers position, opacity from settings,
  `SetNoActivate` when `DoNotStealFocus`): one row per `IClientSwitcher.OrderedSessions` entry with slot
  number, accent color, account name and hotkey hint; highlights `Current`; click focuses. Toggled by the
  `ToggleSwitcher` hotkey, a main-window button, and the tray menu.
- **Command Center**: a resizable window with a grid of live DWM thumbnails (`IThumbnailService`) of
  every running client, labelled with account name + slot; click a tile to focus it. Tile rectangles are
  converted from Avalonia DIPs to physical pixels with `RenderScaling`. Updates on resize and session
  changes. Toggled by hotkey/tray/button.
- **Overlays**: `OverlayManager` creates one `NameBadgeOverlay` window per running session with a window
  when `ShowNameBadges` is on (toggle hotkey too). The badge is a small transparent, borderless,
  topmost, non-activating, click-through Avalonia window (styles applied via
  `IOverlayWindowStyler.ApplyOverlayStyle(hwnd, clickThrough: true)` once the native handle exists),
  positioned at the top-left of the game's client area using `IWindowEvents.TrackWindow`, hidden while
  the game is minimized, closed when the session ends. This is the pipeline future overlays (live stats,
  damage planner) will reuse, so keep an `OverlayWindowBase` class that handles transparency, styling,
  tracking and positioning in physical pixels. Avalonia rewrites `GWL_EXSTYLE` whenever some window
  properties change, so the base class must also register `Win32Properties.AddWindowStylesCallback`
  to re-add the overlay extended styles (and apply them once immediately via the styler).
- **Tray icon**: Show MultiWiz, Switcher, Command Center, Launch team ▸ (teams), Stop all clients, Quit.
- **Updates** (`UpdateService`): Velopack `UpdateManager` with `GithubSource("https://github.com/jlwilley/MultiWiz", null, prerelease)`.
  Stable channel uses the default channel (`win`) so v3 installs can update into v4. Beta channel
  checks both the `beta` feed (prerelease releases) and the stable feed and offers the higher version.
  Checks on startup (if enabled) and every 6 hours; downloads in the background; the user chooses when to
  restart. Everything is a no-op when `UpdateManager.IsInstalled` is false (dev builds).
- First run: if `LegacyImporter.ReadPreview()` finds v3 data and `LegacyImportHandled` is false, offer to
  import (shows the account names that will be imported).
- On exit: `IAudioService.RestoreAll()`, release throttling, stop hotkeys, optionally stop games
  (`CloseGamesOnExit`).
- MVVM with CommunityToolkit.Mvvm, compiled bindings (`x:DataType`) everywhere, no logic in
  code-behind beyond view plumbing. Core events arrive on background threads → marshal with
  `Dispatcher.UIThread.Post`.

## Threading model

- Core services are thread-safe and raise events on the thread that caused them.
- Hotkey callbacks and WinEvent callbacks run on `Win32MessageThread`. They must stay quick; anything
  slow (audio, throttling, disk) goes to a background task.
- Audio work runs on its own MTA thread inside `AudioService`.
- UI code only touches Avalonia objects on the UI thread.

## Build, test, and release

- `dotnet build MultiWiz.slnx -c Release` and `dotnet test --solution MultiWiz.slnx -c Release`
  (Microsoft.Testing.Platform mode, selected in `global.json`) on windows-latest (`ci.yml`, every push
  and PR). CI also runs a `dotnet publish` of the app for `win-x64`.
- Releases are created only by pushing a tag: `v4.1.0` → stable (default `win` channel, normal
  release); `v4.1.0-beta.1` → beta (`beta` channel, GitHub pre-release). See `docs/RELEASING.md`.
- Code signing uses Azure Artifact Signing when the repository has the signing variables/secrets
  configured; otherwise releases are published unsigned with a warning in the job summary.
