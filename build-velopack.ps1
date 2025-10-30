# MultiWiz Velopack Build Script
# This script builds the project and creates a Velopack installer

param(
    [string]$Version = "3.0.0",
    [string]$ReleaseDir = ".\Deployment\VelopackReleases"
)

Write-Host "Building MultiWiz with Velopack..." -ForegroundColor Cyan

# Step 1: Clean and build the project
Write-Host "`n[1/3] Building project in Release mode..." -ForegroundColor Yellow
dotnet clean MultiWiz\MultiWiz.csproj --configuration Release
dotnet build MultiWiz\MultiWiz.csproj --configuration Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Step 2: Publish the application
Write-Host "`n[2/3] Publishing application..." -ForegroundColor Yellow
$publishDir = ".\MultiWiz\bin\Release\net8.0-windows10.0.18362.0\publish"
dotnet publish MultiWiz\MultiWiz.csproj --configuration Release --output $publishDir --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Create Velopack release
Write-Host "`n[3/3] Creating Velopack installer..." -ForegroundColor Yellow

# Ensure release directory exists
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

# Create the Velopack package
# NOTE: You need to install the Velopack CLI tool first:
# dotnet tool install -g vpk

vpk pack `
    --packId "MultiWiz" `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "MultiWiz.exe" `
    --outputDir $ReleaseDir `
    --packTitle "MultiWiz" `
    --packAuthors "jlwilley" `
    --icon ".\MultiWiz\Resources\magic-wand.ico"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Velopack packaging failed!" -ForegroundColor Red
    Write-Host "Make sure you have the Velopack CLI installed: dotnet tool install -g vpk" -ForegroundColor Yellow
    exit 1
}

Write-Host "`nBuild complete! Release files are in: $ReleaseDir" -ForegroundColor Green
Write-Host "`nTo upload to GitHub Releases:" -ForegroundColor Cyan
Write-Host "1. Create a new release on GitHub with tag v$Version" -ForegroundColor White
Write-Host "2. Upload all files from $ReleaseDir to the release" -ForegroundColor White
Write-Host "3. Users will automatically receive updates through the app" -ForegroundColor White
