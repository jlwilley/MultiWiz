<div align="center">

# MultiWiz

### Multi-Account Manager for Wizard101 & Pirate101

A modern Windows desktop application for managing multiple KingsIsle game accounts with automatic login, quick window switching, and intelligent audio control.

![Version](https://img.shields.io/badge/version-3.3.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-MIT-green)

</div>

---

## Features

### Account Management
- **Multi-Game Support** - Manage accounts for Wizard101 (US/EU servers) and Pirate101
- **Secure Storage** - Passwords encrypted using Windows DPAPI (Data Protection API)
- **Automatic Login** - Launch and auto-login to multiple accounts simultaneously
- **Batch Operations** - Start/stop multiple accounts at once with multi-select

### Window Management
- **Quick Switcher** - Overlay panel for instant switching between game windows
- **Keyboard Shortcuts** - Navigate accounts with `Ctrl+W` (up) and `Ctrl+S` (down)
- **Auto-Focus** - Automatically brings selected game window to foreground

### Audio Control
- **Smart Mute** - Automatically mutes unfocused game clients
- **Volume Control** - Set custom volume levels for active windows
- **Per-Process Audio** - Individual volume control for each game instance

### User Interface
- **Material Design 3** - Modern, clean interface with Catppuccin theming
- **Dark Mode** - Built-in dark/light theme toggle
- **Customizable** - Adjustable login delay and switcher opacity
- **Auto-Updates** - Automatic update notifications via Velopack

---

## Installation

### Download

1. Navigate to [Releases](https://github.com/jlwilley/MultiWiz/releases)
2. Download the latest `MultiWiz.zip`
3. Extract the archive to your preferred location
4. Run `MultiWiz.exe`

> **Optional**: Right-click `MultiWiz.exe` → Create Shortcut → Move to Desktop

### Requirements

- **OS**: Windows 10/11 (x64)
- **Framework**: .NET 8.0 Runtime (included in installer)
- **Games**: Wizard101 and/or Pirate101 installed in default locations

---

## Quick Start Guide

### Adding Accounts

1. Click the **Add Account** button
2. Fill in the account details:
   - **Account Name**: Display name (can be anything)
   - **Username**: Your KingsIsle account username
   - **Password**: Your KingsIsle account password
   - **Server**: Select game and region (Wizard101 US/EU, Pirate101 US)
3. Click **Save**

### Launching Games

- **Single Account**: Click the play icon next to an account
- **Multiple Accounts**: Select multiple accounts (Ctrl+Click) → Click **Launch Selected**
- **Auto-Login**: Games will automatically log in after the configured delay (default: 6 seconds)

> **Tip**: Avoid clicking on game windows during the auto-login process to prevent input interference

### Using the Switcher

1. Launch your accounts
2. Click **Open Switcher** button
3. The switcher panel appears on the right side of your screen
4. Click an account or use keyboard shortcuts:
   - `Ctrl+W` - Move selection up
   - `Ctrl+S` - Move selection down

The selected game will automatically:
- Come to the foreground
- Unmute its audio
- Mute all other game windows (if enabled)

### Settings

Access settings to customize:
- **Theme**: Toggle dark/light mode
- **Login Delay**: Adjust wait time before auto-login (seconds)
- **Auto-Mute**: Enable/disable automatic muting of unfocused windows
- **Volume**: Set unmute volume level (0-100)
- **Switcher Opacity**: Adjust transparency of the switcher overlay

---

## Configuration

### File Locations

All configuration files are stored in:
```
%AppData%\MultiWiz\
├── config.txt      # Account data (encrypted)
└── settings.txt    # User preferences
```

### Security

- **Password Encryption**: Uses Windows DPAPI for user-specific encryption
- **Local Storage**: All data stored locally on your machine
- **No Network Calls**: Credentials never transmitted over network (except to game servers)

> **Warning**: Do not use MultiWiz on shared computers. While passwords are encrypted, they can be decrypted by the same Windows user account.

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+W` | Switch to previous account (Switcher mode) |
| `Ctrl+S` | Switch to next account (Switcher mode) |

---

## Game Compatibility

| Game | Server | Status |
|------|--------|--------|
| Wizard101 | US | ✅ Supported |
| Wizard101 | EU | ✅ Supported |
| Pirate101 | US | ✅ Supported |

### Important Notes

- **Game Updates**: After official game updates, launch the game at least once through the official KingsIsle launcher before using MultiWiz
- **Default Paths**: Games must be installed in default ProgramData locations:
  - Wizard101: `C:\ProgramData\KingsIsle Entertainment\Wizard101\`
  - Pirate101: `C:\ProgramData\KingsIsle Entertainment\Pirate101\`

---

## Troubleshooting

### Auto-login not working
- Increase the login delay in settings (try 8-10 seconds for slower computers)
- Avoid clicking or interacting with game windows during login
- Ensure game is fully loaded before the login sequence begins

### Game won't launch
- Verify game is installed in the default location
- Run the game once through the official launcher after any updates
- Check that you have the correct server selected for the account

### Audio issues
- Ensure "Auto-Mute When Not In Focus" is enabled in settings
- Verify Windows audio permissions for MultiWiz
- Check individual game volumes in Windows Volume Mixer

---

## Contributing

Found a bug or have a feature request? Please [open an issue](https://github.com/jlwilley/MultiWiz/issues) with:
- Detailed description of the problem
- Steps to reproduce
- Expected vs. actual behavior
- System information (Windows version, game versions)

---

## Disclaimer

**MultiWiz is an unofficial, community-developed tool and is not affiliated with or endorsed by KingsIsle Entertainment.**

- This application is provided as-is, without warranty of any kind
- Use at your own risk
- The developer does not condone violation of game terms of service
- If KingsIsle requests this application be discontinued, it will be immediately taken down

### Terms of Service

While the developer believes this tool does not violate KingsIsle's Terms of Service, users should:
- Review KingsIsle's current Terms of Service
- Use this tool responsibly
- Understand that account actions are their own responsibility

---

## Technical Details

- **Framework**: .NET 8.0 WPF
- **UI Library**: MaterialDesignThemes 5.3.0
- **Audio**: NAudio 2.2.1
- **Hotkeys**: NHotkey.Wpf 2.1.1
- **Updates**: Velopack 0.0.942
- **Theme**: Catppuccin

---

<div align="center">

**Made with ⚡ by the community, for the community**

[Report Bug](https://github.com/jlwilley/MultiWiz/issues) • [Request Feature](https://github.com/jlwilley/MultiWiz/issues)

</div>
