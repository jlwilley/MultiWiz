# Velopack Migration Guide

## What Changed

This project has been migrated from:
- **.NET 6.0** → **.NET 8.0** (Long-term support through November 2026)
- **Squirrel for Windows** → **Velopack** (Modern, actively maintained update framework)

## Prerequisites

### Install Velopack CLI
```bash
dotnet tool install -g vpk
```

### Verify Installation
```bash
vpk --version
```

## Building and Releasing

### Option 1: Using the PowerShell Script (Recommended)
```powershell
.\build-velopack.ps1 -Version "3.0.0"
```

### Option 2: Manual Build Process

#### Step 1: Build the Project
```bash
dotnet build MultiWiz\MultiWiz.csproj --configuration Release
```

#### Step 2: Publish the Application
```bash
dotnet publish MultiWiz\MultiWiz.csproj --configuration Release --self-contained false
```

#### Step 3: Create Velopack Package
```bash
vpk pack `
    --packId "MultiWiz" `
    --packVersion "3.0.0" `
    --packDir ".\MultiWiz\bin\Release\net8.0-windows10.0.18362.0\publish" `
    --mainExe "MultiWiz.exe" `
    --outputDir ".\Deployment\VelopackReleases" `
    --packTitle "MultiWiz" `
    --packAuthors "jlwilley" `
    --icon ".\MultiWiz\Resources\magic-wand.ico"
```

## Deployment to GitHub Releases

### Step 1: Create a GitHub Release
1. Go to https://github.com/jlwilley/MultiWiz/releases/new
2. Create a new tag (e.g., `v3.0.0`)
3. Set the release title (e.g., "MultiWiz 3.0.0")
4. Add release notes

### Step 2: Upload Velopack Files
Upload all files from `Deployment\VelopackReleases` to the GitHub release:
- `MultiWiz-Setup.exe` - The installer for new users
- `RELEASES` - The release manifest file
- `MultiWiz-{version}-full.nupkg` - The full update package
- Any delta packages (for incremental updates)

### Step 3: Publish the Release
Click "Publish release" to make it available

## Key Differences from Squirrel

| Feature | Squirrel | Velopack |
|---------|----------|----------|
| **Update Check** | `mgr.UpdateApp()` | `mgr.CheckForUpdatesAsync()` |
| **Apply Updates** | `UpdateManager.RestartApp()` | `mgr.ApplyUpdatesAndRestart()` |
| **Event Handling** | `SquirrelAwareApp.HandleEvents()` | `VelopackApp.Build().Run()` |
| **Shortcuts** | Manual via `IAppTools` | Automatic |
| **Build Tool** | `Squirrel.exe releasify` | `vpk pack` |

## Velopack Advantages

1. **Simpler Build Process**: No need for NuGet.exe or complex MSBuild targets
2. **Better Performance**: Faster updates with improved delta compression
3. **Active Development**: Regular updates and bug fixes
4. **Better Documentation**: Comprehensive docs at https://velopack.io
5. **Cross-Platform**: Can target Windows, macOS, and Linux
6. **Modern API**: Async/await throughout

## Testing the Update System

### Local Testing
```bash
# Build a test release
.\build-velopack.ps1 -Version "3.0.1"

# Run the Setup.exe to install
.\Deployment\VelopackReleases\MultiWiz-Setup.exe

# The app will check for updates on startup
```

### Testing Updates
1. Install version 3.0.0
2. Build and release version 3.0.1
3. Run the app - it should detect and offer the update

## Important Notes

### GitHub Token Security
⚠️ **SECURITY WARNING**: The GitHub token is still hardcoded in `MainWindow.xaml.cs:55`

**Recommended fixes:**
1. Revoke the current token on GitHub
2. Store the token in an environment variable or config file
3. Never commit tokens to source control

```csharp
// Better approach:
String Token = Environment.GetEnvironmentVariable("MULTIWIZ_GITHUB_TOKEN") ?? "";
```

### Version Numbering
- The version is now defined in `MultiWiz.csproj` (line 45)
- Bump this version before each release
- Use semantic versioning (MAJOR.MINOR.PATCH)

### Existing Users
Users on Squirrel-based versions (2.7.1 and earlier) will need to:
1. Uninstall the old version
2. Download and run the new MultiWiz-Setup.exe (v3.0.0+)
3. Future updates will work automatically

## Troubleshooting

### "vpk: command not found"
Install the Velopack CLI:
```bash
dotnet tool install -g vpk
```

### Build Errors
Ensure you have .NET 8 SDK installed:
```bash
dotnet --list-sdks
```
Download from: https://dotnet.microsoft.com/download/dotnet/8.0

### Updates Not Working
1. Verify the RELEASES file is uploaded to GitHub
2. Check that the release tag matches the version
3. Ensure the app has internet connectivity
4. Check Debug output for error messages

## Resources

- **Velopack Documentation**: https://velopack.io
- **Velopack GitHub**: https://github.com/velopack/velopack
- **.NET 8 Documentation**: https://learn.microsoft.com/dotnet/core/whats-new/dotnet-8

## Rollback Instructions

If you need to rollback to Squirrel:
```bash
git revert HEAD
git push
```

Then rebuild with the old system. However, Velopack is recommended for long-term support.
