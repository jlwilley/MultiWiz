# MultiWiz roadmap

This is the planned direction, not a list of promises. Every phase follows the same rules as today:
MultiWiz never writes game memory, injects code, hooks the client, touches network traffic or plays
the game for you. Live data is read-only. See "Fair play" in the [README](../README.md).

## v4.0: the rewrite

This is a from-scratch rewrite on .NET 10, Avalonia 12, CsWin32 and Velopack. It replaces MultiWiz 3
in place.

- **Accounts:** any number of accounts for Wizard101 and Pirate101, each with a realm, game install,
  accent color and notes.
- **Secure vault:** passwords are kept in Windows Credential Manager and never in MultiWiz's files.
- **Teams and layouts:** ordered account slots per team, launched with one click. Windows are arranged
  side by side, in 2×2 or 3×2 grids, as main + 3, or one per monitor, using DPI-aware work areas.
- **Smart login:** each client starts straight on its realm's login server. MultiWiz waits for the
  window and types the credentials one client at a time behind a shared login gate. Launches are
  staggered and failures are reported clearly.
- **Steam support:** Steam libraries are discovered, MultiWiz makes sure Steam is running and signed
  in, and `steam_appid.txt` is handled.
- **Realms:** Wizard101 US, EU and Test Realm, Pirate101 US, plus custom host/port realms.
- **Switcher:** a compact, always-on-top list of running clients that doesn't steal focus.
- **Command Center:** live DWM thumbnails of every client; click one to focus it.
- **External clients:** clients started with the official launcher, or running before MultiWiz, are
  detected and managed like launched ones (slots, audio, badges), and can be linked to an account.
- **Global hotkeys:** fully rebindable, with registration failures flagged.
- **Audio:** the focused client plays at full volume while background clients are muted or ducked.
  Original volumes are restored on exit.
- **Efficiency mode:** Windows efficiency mode (EcoQoS) and/or lower priority for background clients.
- **Name badges:** click-through overlays that show the slot and account name on each game window.
  They run on the same overlay pipeline that later overlays will use.
- **Tray:** show, switcher, Command Center, launch a team, stop all clients.
- **Updates:** Velopack updates from GitHub Releases, with stable and beta channels. Updates are
  signed when Azure Artifact Signing is configured.
- **v3 import:** accounts, passwords and settings come across on first start, and the v3 files are
  left untouched.
- **Download full game:** Settings → Games → Game files downloads every Wizard101 file from
  KingsIsle's patch server (verified by KingsIsle's size and CRC), so zones don't download while you
  play. Also warns when the core client files are out of date and updates them. Standalone Wizard101
  (North America) only: Steam installs are already complete, and Pirate101's patch server is
  unverified.

## v4.x: quality of life

- **Crash and disconnect detection:** notice when a client crashes, hangs or drops to the login
  screen, then offer to relaunch it and log it back in, with a per-account opt-in.
- **Stale client and patch detection:** detect a pending game patch or outdated client files before a
  multi-launch, and ask the user to run the official launcher once instead of starting clients that
  will fail. (The check itself exists: `IGameDownloader.CheckForUpdateAsync` powers the "out of date"
  banner in Settings → Games; wiring it into launches is still to do.)
- **Pirate101 and EU game file downloads:** once their patch servers are verified.
- **Discord Rich Presence:** opt-in, showing the game and client count and nothing account-specific.
- **Per-account timers:** gardening, pet training and hatching, and crafting cooldowns, with tray
  notifications when each is ready.
- **Screenshots:** a hotkey that captures the focused client or every client and saves the images per
  account.

## Live overlays (Blish HUD-style)

Opt-in overlays drawn in MultiWiz's own click-through windows on top of each client. The data comes
from a **read-only** memory-reading layer.

- **Memory-reading layer:** built on `IProcessMemory` (process handles opened with read/query rights
  only), byte-pattern scanning and pointer chains. Type and offset data will come from the owner's own
  wizdatabase and the community wiki (to be provided). The data is keyed to the game build, so a patch
  switches the overlays off instead of showing wrong numbers.
- **Live stats overlay:** health, mana, pips and other per-client values, next to each game window.
- **Damage planner and estimator:** estimates a hit with the community damage formula (base damage,
  blades, traps, boosts, resists, pierce, critical) from the live combat state or from inputs you
  enter.
- **Drop logger:** records drops per account and session to a local file you can review or export.

## Later

- **Plugin and module system:** lets the community build overlays against a stable, read-only API
  surface, with the same fair-play limits as the built-in overlays.
- **OCR fallbacks:** screen-reading through `Windows.Graphics.Capture` where memory data is
  unavailable, such as right after a game patch.

## Never

Bots, auto-questing, auto-farming, input broadcasting to several clients, macros, memory writing, code
injection, hooking and packet editing. MultiWiz only ever types into a client at the login screen.
