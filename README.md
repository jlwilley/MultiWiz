<div align="center">

# MultiWiz

**Multi-account launcher and multiboxing companion for Wizard101 and Pirate101**

[![Latest release](https://img.shields.io/github/v/release/jlwilley/MultiWiz?sort=semver&label=release)](https://github.com/jlwilley/MultiWiz/releases/latest)
[![Latest pre-release](https://img.shields.io/github/v/release/jlwilley/MultiWiz?include_prereleases&sort=semver&label=pre-release)](https://github.com/jlwilley/MultiWiz/releases)
[![CI](https://github.com/jlwilley/MultiWiz/actions/workflows/ci.yml/badge.svg)](https://github.com/jlwilley/MultiWiz/actions/workflows/ci.yml)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D4)
[![License](https://img.shields.io/github/license/jlwilley/MultiWiz)](LICENSE)

</div>

MultiWiz saves your KingsIsle accounts and launches as many clients as you like, each already
pointed at the right realm and logged in. It arranges the windows into a layout, lets you jump between
clients with global hotkeys, and keeps background clients quiet and light on your CPU.

MultiWiz 4 is a from-scratch rewrite on .NET 10 and Avalonia. Until 4.0 is released on the stable
channel, the newest stable download is MultiWiz 3.x, and v4 builds appear as pre-releases.

## Features

- **Accounts:** any number of Wizard101 and Pirate101 accounts, each with its own realm, game install,
  accent color and notes. You can reorder them, launch several at once, and see each client's live
  status.
- **Secure vault:** passwords are stored in Windows Credential Manager, never in MultiWiz's own files.
- **Smart login:** each client starts directly on its realm's login server. MultiWiz waits for the
  window, then types your username and password. Clients are logged in one at a time, so launching
  eight at once never mixes up keystrokes.
- **Realms:** Wizard101 US, Wizard101 EU, the Wizard101 Test Realm and Pirate101 US, plus your own
  custom realms (host and port).
- **Steam support:** Steam copies of the games are detected automatically. MultiWiz makes sure Steam
  is running and signed in before it launches.
- **Install discovery:** finds the default KingsIsle folders, installs listed in Windows, and every
  Steam library. You can also add a game folder by hand.
- **Teams and layouts:** group accounts into teams with a fixed slot order and launch a whole team with
  one click. Windows can be arranged side by side, in a 2×2 or 3×2 grid, as main + 3, or one per
  monitor, with multi-monitor and DPI awareness.
- **Switcher:** a compact, always-on-top list of your running clients. Click one to focus it; the
  switcher doesn't take focus away from the game.
- **Command Center:** a window of live thumbnails of every client. Click a tile to jump to that client.
- **Global hotkeys:** focus a slot, cycle clients, or toggle the switcher, Command Center and name
  badges. Every hotkey can be rebound, and combinations another app already owns are flagged.
- **Audio:** the focused client plays at full volume while background clients are muted or turned
  down. Original volumes are restored when MultiWiz exits.
- **Efficiency mode:** optionally puts background clients into Windows efficiency mode and/or lowers
  their priority.
- **Name badges:** a small click-through label on each game window showing its slot number and
  account name.
- **Tray icon:** show MultiWiz, the switcher or the Command Center, launch a team, stop all clients.
- **Automatic updates:** stable or beta channel, downloaded in the background and applied when you
  choose to restart.
- **MultiWiz 3 import:** brings over your v3 accounts, passwords and settings on first start.

## Install

1. Open the [latest release](https://github.com/jlwilley/MultiWiz/releases/latest) and download
   **`MultiWiz-win-Setup.exe`**. Beta builds are pre-releases on the
   [Releases page](https://github.com/jlwilley/MultiWiz/releases) and ship `MultiWiz-beta-Setup.exe`.
2. Run it. MultiWiz installs for your Windows user, with no administrator rights needed, and adds
   Desktop and Start menu shortcuts.

**Installing a beta:** an install from `MultiWiz-beta-Setup.exe` starts on the Beta update channel
(Settings → General → Update channel). Stay on Beta until 4.0 is released on the stable channel: a v4
beta switched to Stable reports that it is up to date and is never moved back to MultiWiz 3.

**SmartScreen:** Windows may show "Windows protected your PC" for a new download. Click
**More info → Run anyway**. New releases have no download reputation yet, and unsigned builds show
an unknown publisher. Updates after the first install are downloaded by MultiWiz itself and don't
trigger this warning.

**Requirements:** Windows 10 (1809 or later) or Windows 11, x64. The .NET runtime is bundled, so
there is nothing else to install.

To uninstall, use **Settings → Apps → Installed apps → MultiWiz**. If you also want your saved
passwords removed, delete your accounts in MultiWiz first.

## Upgrading from MultiWiz 3

- **Automatic:** once 4.0 is published on the stable channel, MultiWiz 3 downloads it in the
  background and asks you to restart. After the restart you are on v4. You can also install
  `MultiWiz-win-Setup.exe` over v3.
- **Import:** on first start, MultiWiz 4 finds your v3 data (`%AppData%\MultiWiz\config.txt` and
  `settings.txt`), shows the accounts it found, and offers to import them. The import covers:
  - accounts and realms
  - passwords, which move into Windows Credential Manager
  - theme, login delay, background mute and volume, and switcher opacity

  The v3 files are only read, never changed or deleted.
- **Hotkeys changed:** v3's global `Ctrl+W` / `Ctrl+S` are gone, because they broke closing tabs and
  saving files in every other app. See [Hotkeys](#hotkeys) for the new defaults.

## Quick start

1. **Settings → Games:** check that your Wizard101 / Pirate101 installs were found, or use
   **Add game folder…**.
2. **Accounts → Add account:** enter a display name, your KingsIsle username and password, the game
   and the realm.
3. **Launch** the account, or select several and click **Launch selected**. Don't click into a game
   window while MultiWiz is typing the login. If your PC loads slowly, raise the ready delay in
   **Settings → Login**.
4. **Teams → New team:** add accounts in slot order, pick a layout, then **Launch team**. Launching a
   team makes it the active team, and its slot order drives the hotkeys and the switcher.
5. Jump between clients with `Alt+1` … `Alt+8` or cycle with `` Alt+` ``. Toggle the switcher with
   `Ctrl+Alt+S`.

## Hotkeys

All hotkeys are global and can be changed in **Settings → Hotkeys**. Click a box and press a
combination. Esc cancels, and Backspace clears the hotkey.

| Action | Default |
|---|---|
| Focus the client in slot 1 … 8 | `Alt+1` … `Alt+8` |
| Next client | `` Alt+` `` |
| Previous client | `` Alt+Shift+` `` |
| Show / hide the switcher | `Ctrl+Alt+S` |
| Show / hide the Command Center | `Ctrl+Alt+C` |
| Show MultiWiz | `Ctrl+Alt+M` |
| Show / hide name badges | `Ctrl+Alt+B` |

Slots follow the active team's order first, then any other running clients in account-list order.

## Where your data lives

| What | Where |
|---|---|
| Settings, accounts (without passwords), teams | `%AppData%\MultiWiz\v4\` (`settings.json`, `accounts.json`, `teams.json`) |
| Passwords | Windows Credential Manager → Windows Credentials → entries named `MultiWiz:account:<id>` |
| Logs (kept 7 days) | `%LocalAppData%\MultiWiz\logs` |
| The app itself | `%LocalAppData%\MultiWiz` |
| MultiWiz 3 data (read once for import) | `%AppData%\MultiWiz\config.txt`, `settings.txt` |

Credential Manager protects your passwords with your Windows sign-in. Any program running as your
Windows user can read them, so don't save accounts on a shared Windows account. MultiWiz has no
telemetry. The only network requests it makes itself are update checks and downloads from GitHub
Releases, which you can turn off in **Settings → General**.

## Fair play

MultiWiz is a launcher and window manager. It works through the operating system and never modifies
the game.

**MultiWiz does:**
- start game clients with their realm's login server, and type your username and password once, at
  the login screen
- move, resize, focus and arrange game windows
- set each client's volume and adjust CPU priority or efficiency mode for background clients
- draw its own overlay windows (switcher, name badges) on top of the game
- for Steam installs, write Steam's standard `steam_appid.txt` in the game's `Bin` folder, so a Steam
  copy can be started directly

**MultiWiz never:**
- writes to game memory, injects code or DLLs, or hooks game functions
- reads or changes network traffic
- plays the game for you: no bots, macros, auto-clicking, or input broadcast to several clients
- sends keystrokes to a client after login

Future live overlays (see the [roadmap](docs/ROADMAP.md)) will **read** game memory to display
information. They will be read-only by design: MultiWiz opens the game process with read and query
rights only. You are responsible for following KingsIsle's Terms of Use. Review them before you use
any third-party tool.

## Building from source

Prerequisites:
- Windows 10 or 11
- the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` pins the 10.0
  feature band)
- optionally Visual Studio 2026, JetBrains Rider or VS Code with C# Dev Kit

```powershell
git clone https://github.com/jlwilley/MultiWiz.git
cd MultiWiz
dotnet build MultiWiz.slnx -c Release
dotnet test --solution MultiWiz.slnx -c Release
dotnet run --project src/MultiWiz.App
```

- Tests use xUnit v3 on Microsoft.Testing.Platform, which `global.json` selects. Pass the solution
  with `--solution` or a project with `--project`, not as a positional path.
- A development build is not installed, so it never updates itself. It does use your real
  `%AppData%\MultiWiz\v4` data.
- The self-contained build that CI produces for every push can be reproduced with:

  ```powershell
  dotnet publish src/MultiWiz.App/MultiWiz.App.csproj -c Release -r win-x64 --self-contained -o publish
  ```

### Project layout

```
MultiWiz.slnx
src/
  MultiWiz.Core/               app logic: accounts, teams, sessions, switching, hotkeys, storage, v3 import
  MultiWiz.Platform.Windows/   Win32 (CsWin32), audio sessions (NAudio), Credential Manager, DPAPI
  MultiWiz.App/                Avalonia UI, tray, overlays, updates (Velopack); builds MultiWiz.exe
tests/
  MultiWiz.Core.Tests/         xUnit v3 tests for Core
docs/
  ARCHITECTURE.md              design and module contracts
  RELEASING.md                 cutting stable/beta releases, channels, code signing
  ROADMAP.md                   what's next
.github/workflows/
  ci.yml                       build, test and publish on every push and pull request
  release.yml                  tag-triggered Velopack release
```

## Releasing

Releases are created by pushing a tag: `v4.1.0` for stable, `v4.1.0-beta.1` for beta. Channels, the
v3 upgrade path and Azure Artifact Signing setup are covered in [docs/RELEASING.md](docs/RELEASING.md).

## Contributing

Found a bug or have an idea? [Open an issue](https://github.com/jlwilley/MultiWiz/issues). For bugs,
include your Windows version, the steps to reproduce, and the latest log from
`%LocalAppData%\MultiWiz\logs`.

## Disclaimer

MultiWiz is an unofficial, community-made tool. It is not affiliated with, endorsed by or sponsored
by KingsIsle Entertainment. Wizard101 and Pirate101 are trademarks of KingsIsle Entertainment. The
software is provided as-is, without warranty of any kind, and you use it at your own risk. If
KingsIsle asks for MultiWiz to be taken down, it will be.

## License

[MIT](LICENSE) © 2023-2026 jlwilley
