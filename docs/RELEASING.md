# Releasing MultiWiz

Releases are built by GitHub Actions ([`.github/workflows/release.yml`](../.github/workflows/release.yml))
whenever a tag that starts with `v` is pushed. Nothing is released from branch pushes. CI
([`ci.yml`](../.github/workflows/ci.yml)) builds, tests and publishes every push and pull request, so
check that the commit you are tagging is green first.

```powershell
# Beta: GitHub pre-release on the "beta" channel
git tag -a v4.1.0-beta.1 -m "MultiWiz 4.1.0-beta.1"
git push origin v4.1.0-beta.1

# Stable: normal GitHub release on the default "win" channel (every stable user, including MultiWiz 3.x).
# Blocked until the repository variable STABLE_RELEASES_ENABLED is true; see "Why stable stays on win".
git tag -a v4.1.0 -m "MultiWiz 4.1.0"
git push origin v4.1.0
```

Push one release tag at a time and let its run finish before pushing the next. Each run builds its
delta package from the release before it.

## Versions and channels

| Tag | Version | Velopack channel | GitHub release | Installer asset |
|---|---|---|---|---|
| `v4.1.0` | `4.1.0` | `win` (Velopack's default) | normal, becomes "Latest" | `MultiWiz-win-Setup.exe` |
| `v4.1.0-beta.1` | `4.1.0-beta.1` | `beta` | pre-release | `MultiWiz-beta-Setup.exe` |

- A tag must be `v` followed by a three-part SemVer 2 version: plain ASCII numbers without leading
  zeros, optionally followed by a pre-release label. The workflow rejects anything else (`v4.1`,
  `v4.1.0.1`, `v04.1.0`, `v4.1.0-beta.01`, `v4.1.0+build.5`) before it builds.
- Anything after a `-` makes the release a beta: `-beta.1`, `-rc.1` and `-preview.2` all go to the
  `beta` channel.
- Versions must keep increasing. Use dot-separated numbers in pre-release labels (`beta.2`, `beta.10`),
  because SemVer compares `beta.10` above `beta.2` but compares `beta10` below `beta2`. The order is
  `4.1.0-beta.2` < `4.1.0-beta.10` < `4.1.0-rc.1` < `4.1.0`.
- Never reuse a version number. If a release is broken, ship the next patch version.

Each release also contains the Velopack feed (`releases.win.json` or `releases.beta.json`), full and
delta `.nupkg` update packages, and a portable zip. For the `win` channel only, Velopack also adds a
legacy `RELEASES` file.

### Who gets which update

- **Stable** (the default in Settings → General → Update channel) sees only stable releases. A fresh
  install from `MultiWiz-beta-Setup.exe` selects **Beta** on its first start instead.
- **Beta** checks the beta feed and the stable feed and offers whichever version is higher. Beta
  testers therefore move on to `4.1.0` once it ships and never need a separate stable install.
- A tester who switches from Beta back to Stable and clicks **Check now** is offered the latest stable
  release of the same major version, even when it is lower than the beta they are running. The
  background checks never move a build to a lower version, and Stable never offers an older major
  version: a v4 install is never offered MultiWiz 3.
- **Until 4.0.0 ships as a stable release, the latest stable release is MultiWiz 3.3.5.** A v4 beta
  switched to Stable therefore reports that it is up to date and receives no further betas. Testers
  should stay on Beta until 4.0.0 is out; say so in the notes of every beta release.

MultiWiz checks for updates on startup and every 6 hours. It downloads the update in the background
and applies it when the user chooses to restart. Development builds that were not installed (from
`dotnet run` or an unzipped CI artifact) never update.

### Why stable stays on `win`, and what that means for v4

MultiWiz 3.x uses Velopack 0.0.942. It reads `releases.win.json` from the newest non-pre-release GitHub
releases of `jlwilley/MultiWiz`, downloads any higher version in the background, and asks the user to
restart. MultiWiz 4 keeps the same package id (`MultiWiz`), the same executable (`MultiWiz.exe`) and
the same default channel (`win`) so that v3 installs update straight into v4. The consequences:

- **Don't push a stable `v4.x.y` tag until v4 is ready to replace v3 for everyone.** Every v3 user who
  opens MultiWiz 3 after that release will be prompted to restart into v4. Until then, ship only
  `-beta` tags. MultiWiz 3 never looks at pre-releases, and betas use a different feed anyway.
- The release workflow enforces this. It fails every stable tag before building unless the repository
  variable `STABLE_RELEASES_ENABLED` is `true` (**Settings → Secrets and variables → Actions →
  Variables**). Create it only when you are ready to ship 4.0.0 to every v3 user, and leave it set
  afterwards. A stable tag pushed by mistake while the variable is unset publishes nothing: delete the
  tag (`git push --delete origin v4.0.0`) and push the one you meant.
- There is no rollback. Deleting the GitHub release stops further updates, but anyone who already
  updated stays on v4. Fix forward with a new patch release.
- The first stable v4 release carries no delta package. The workflow does not build deltas across
  major versions, since a 3.x → 4.0 delta of a full rewrite would be as large as the full package. v3
  clients download the full package instead.
- On first start, v4 offers to import the v3 accounts, passwords and settings. The v3 files are never
  changed. See the README.
- Velopack's GitHub client, the app and `vpk download github` all read only the **10 newest** GitHub
  releases. If more than 10 releases pile up after the latest stable one, stable and v3 clients stop
  seeing any update and CI stops building deltas. Delete old beta releases on the Releases page to keep
  the newest stable release within the 10 newest. The tags can stay.

## What the release workflow does

1. Derives the version from the tag (`v4.1.0-beta.1` → `4.1.0-beta.1`) and picks the channel (`beta` if
   the version contains `-`, otherwise stable). A stable tag stops here unless `STABLE_RELEASES_ENABLED`
   is `true`.
2. Restores, builds (`-p:Version=<version>`) and runs the tests.
3. Publishes `src/MultiWiz.App` self-contained for `win-x64`.
4. Restores `vpk` 1.2.158, pinned in [`.config/dotnet-tools.json`](../.config/dotnet-tools.json).
   Keep it equal to the `Velopack` package version in `Directory.Packages.props`.
5. `vpk download github` fetches the previous full package of the same channel to use as the delta
   base (with `--channel beta --pre` for betas).
6. When all six signing variables are set, it signs in to Azure with OIDC and writes the Artifact
   Signing `metadata.json` (see below). Otherwise it continues unsigned and puts a warning in the job
   summary.
7. `vpk pack` runs with `--packId MultiWiz --mainExe MultiWiz.exe --packTitle MultiWiz --packAuthors jlwilley`
   and the app icon. Stable omits `--channel`, so it stays on `win`; betas use `--channel beta`. Signing
   adds `--azureTrustedSignFile`.
8. `vpk upload github --publish --tag <pushed tag> --releaseName "MultiWiz <version>"` creates the
   GitHub release and uploads the assets and the feed. Betas add `--channel beta --pre`.

**Release notes (optional).** If `docs/release-notes/<version>.md` exists when you tag (for example
`docs/release-notes/4.1.0-beta.1.md`), it is passed to `vpk pack --releaseNotes` and embedded in the
update package. You can edit the text on the GitHub release page afterwards.

### If a release run fails

- **Failed before "Publish GitHub release".** Nothing was published. Fix the problem, then either
  re-run the job (same commit) or move the tag to the fixed commit:
  `git tag -d v4.1.0 && git push --delete origin v4.1.0`, then tag and push again.
- **Failed in or after "Publish GitHub release".** A draft or partial release may already exist, and
  `vpk upload` refuses to upload into an existing release. Delete that release on the Releases page
  (keep the tag), then re-run the job.
- Once a release has been public for any length of time, don't re-release the same version. Bump the
  patch number instead.

## Code signing with Azure Artifact Signing

Unsigned installers trigger Windows SmartScreen's "Unknown publisher" warning. Azure Artifact Signing
(formerly Trusted Signing) signs the app with a Microsoft-issued certificate in your verified name.
The workflow needs no stored secret: it logs in to Azure with GitHub's OIDC token.

When the six variables below are set, `vpk pack --azureTrustedSignFile` signs `MultiWiz.exe`, the app's
own and third-party DLLs, `Update.exe`, the setup executable and the portable stub. It uses the
`signtool` and Artifact Signing client that ship inside vpk, so the runner needs no Windows SDK. Files
that are already signed, such as the Microsoft-signed .NET runtime, are skipped. When any of the six is
missing, the release is published unsigned and the job summary says which ones are missing.

### Set it up (US individual developer)

The portal labels below are the ones current when this guide was written. Microsoft renamed the
service from "Trusted Signing" to "Artifact Signing", so older pages and roles may still use the old
name.

1. **Azure subscription.** Sign in at <https://portal.azure.com> and create a Pay-As-You-Go
   subscription if you don't have one. Then open **Subscriptions → *your subscription* → Settings →
   Resource providers** and register **Microsoft.CodeSigning**.
2. **Artifact Signing account.** Search the portal for **Artifact Signing** and choose **Create**.
   Pick the subscription, a new resource group (for example `rg-multiwiz-signing`), an account name
   (for example `multiwiz-signing`), a region near you (for example East US) and the **Basic** SKU.
   Basic includes a monthly signature quota far above what MultiWiz uses; check the current price on
   the Azure pricing page. When the account exists, open its **Overview** and note:
   - **Account URI**, for example `https://eus.codesigning.azure.net/` → `AZURE_SIGNING_ENDPOINT`. It
     must match the account's region, or signing fails with 403.
   - the account name → `AZURE_SIGNING_ACCOUNT`
3. **Allow yourself to request identity validation.** In the account, open **Access control (IAM) →
   Add role assignment**. Give your own user the **Artifact Signing Identity Verifier** role (older
   name: *Trusted Signing Identity Verifier*). You need this role even as the subscription owner. If
   the portal lists the role under a different name, pick the *Identity Verifier* role it shows.
4. **Identity validation.** In the account, go to **Objects → Identity validations → New identity →
   Public → Individual**.
   - Enter your legal name and address exactly as they appear on your government-issued photo ID.
   - Follow the emailed link to verify your identity. It uses Microsoft Entra Verified ID in the
     Microsoft Authenticator app with your photo ID and a selfie.
   - Individual validation is available to developers in the US and Canada.
   - It usually completes within hours to a few business days. Wait for the status **Completed**.
   - Your legal name becomes the "Verified publisher" that Windows shows.
5. **Certificate profile.** Go to **Objects → Certificate profiles → Create → Public Trust**. Name it
   (for example `multiwiz`) and select the completed identity validation. The profile name →
   `AZURE_SIGNING_PROFILE`.
6. **Entra app registration for GitHub Actions.**
   1. Open **Microsoft Entra ID → App registrations → New registration**. Name it
      `multiwiz-github-release`, choose single tenant and leave the redirect URI empty.
   2. From **Overview**, copy the **Application (client) ID** → `AZURE_CLIENT_ID` and the
      **Directory (tenant) ID** → `AZURE_TENANT_ID`. The subscription's ID (from step 1) →
      `AZURE_SUBSCRIPTION_ID`.
   3. Open **Certificates & secrets → Federated credentials → Add credential** and choose the scenario
      **GitHub Actions deploying Azure resources**. Enter:
      - Organization: `jlwilley`
      - Repository: `MultiWiz`
      - Entity type: **Environment**
      - Environment name: `release`
      - Name: `multiwiz-release-tags`

      This creates the subject `repo:jlwilley/MultiWiz:environment:release` with issuer
      `https://token.actions.githubusercontent.com` and audience `api://AzureADTokenExchange`. Do not
      create a client secret.

   Why an environment and not the **Tag** entity type: Entra matches the subject exactly, and a Tag
   credential names one specific tag (`v4.1.0`), so every release would need a new credential. The
   release job runs in the `release` environment, and step 8 limits that environment to `v*` tags. The
   result is the same: only tag-triggered release runs can get an Azure token.
7. **Grant signing rights.** In the Artifact Signing account (or on the certificate profile), open
   **Access control (IAM) → Add role assignment**. Choose **Artifact Signing Certificate Profile
   Signer**, set *Assign access to* to "User, group, or service principal", select
   `multiwiz-github-release`, and click **Review + assign**. Role assignments can take a few minutes
   to take effect.
8. **GitHub environment.** Go to **Repository → Settings → Environments** and open or create
   `release`. The first release run also creates it automatically.
   - Under **Deployment branches and tags**, choose **Selected branches and tags**, then add a rule
     with ref type **Tag** and pattern `v*`.
   - Optionally add yourself as a **required reviewer**, so every release waits for your approval
     before it builds.
9. **Repository variables.** Go to **Settings → Secrets and variables → Actions → Variables → New
   repository variable** and add all six. They are identifiers, not secrets. You can also define them as
   variables on the `release` environment.

   | Variable | Example |
   |---|---|
   | `AZURE_SIGNING_ENDPOINT` | `https://eus.codesigning.azure.net/` |
   | `AZURE_SIGNING_ACCOUNT` | `multiwiz-signing` |
   | `AZURE_SIGNING_PROFILE` | `multiwiz` |
   | `AZURE_CLIENT_ID` | app registration's Application (client) ID |
   | `AZURE_TENANT_ID` | Directory (tenant) ID |
   | `AZURE_SUBSCRIPTION_ID` | subscription ID |

10. **Verify.** Push a beta tag and check the run:
    - "Azure login (OIDC)" succeeds.
    - The "Package with Velopack" log shows signtool signing the files.
    - The job summary says `Signed: yes`.
    - On Windows, `Get-AuthenticodeSignature .\MultiWiz-beta-Setup.exe` returns `Valid` with your name
      in the signer certificate.

### Troubleshooting

- **"No matching federated identity record found".** The token's subject doesn't match the
  credential. Check the environment name (`release`) and the owner and repository spelling.
- **"No subscriptions found".** The app registration has no role anywhere in the subscription. Assign
  the signer role (step 7) and check `AZURE_SUBSCRIPTION_ID`.
- **403 or `SignerSign() failed`.** Either the endpoint's region doesn't match the account, or the role
  assignment hasn't propagated yet.
- **Costs.** Each release signs only the unsigned PE files: MultiWiz's own, Avalonia and other
  third-party libraries, and Velopack's executables. That comes to tens of signatures per release,
  well inside the Basic quota.

## SmartScreen reputation

- **Unsigned builds.** Windows shows "Windows protected your PC" for a downloaded installer until that
  exact file has built up reputation, and every new version starts over. Users click **More info → Run
  anyway**. The README explains this.
- **Signed builds.** The prompt names you as the publisher, and reputation builds up with your signing
  identity over time. No certificate gives instant reputation any more, so early signed releases can
  still show the prompt until enough people have installed them without problems. Sign consistently
  and don't switch identities.
- **Updates bypass SmartScreen.** Velopack downloads updates itself, so they never carry the browser's
  mark-of-the-web. SmartScreen only affects the first download of the installer.
- If Microsoft Defender flags a release by mistake, submit it at
  <https://www.microsoft.com/wdsi/filesubmission>.

## Testing updates

Always test installs in **Windows Sandbox** or a throwaway VM. Installing a test build on your own PC
replaces your real MultiWiz install, because every build uses the same package id.

- **Try a CI build.** Open the CI run on GitHub, download the `MultiWiz-win-x64` artifact, unzip it and
  run `MultiWiz.exe`. It uses your real `%AppData%\MultiWiz\v4` data. It is not installed, so it never
  updates.
- **Installer and update, beta to beta.**
  1. Install `MultiWiz-beta-Setup.exe` from the newest beta release.
  2. Check that Settings → General → Update channel shows Beta (a fresh beta install selects it).
  3. Push the next beta tag.
  4. In the app, open Settings → General → **Check now**. Expect the "Update ready — restart to apply"
     banner.
  5. Restart. About should show the new version.
- **Channel switching (only once 4.0.0 is stable).** On an installed beta, switch to Stable and click
  **Check now**. You should be offered the latest stable 4.x release, even when it is lower than the
  beta. Restart to apply it, switch back to Beta and click **Check now**: you should be offered the
  latest beta again. Before 4.0.0 is stable, Stable should report that you are up to date instead
  (see [Who gets which update](#who-gets-which-update)), because MultiWiz 3.3.5 is never offered to v4.
- **Test the installer locally without GitHub.** This is useful for checking shortcuts, the icon and
  signing prompts:

  ```powershell
  dotnet tool restore
  dotnet publish src/MultiWiz.App/MultiWiz.App.csproj -c Release -r win-x64 --self-contained -o publish -p:Version=4.0.0
  dotnet vpk pack --packId MultiWiz --packVersion 4.0.0 --packDir publish --mainExe MultiWiz.exe --packTitle MultiWiz --packAuthors jlwilley --icon src/MultiWiz.App/Assets/multiwiz.ico --outputDir releases
  ```

  Then run `releases\MultiWiz-win-Setup.exe` inside Windows Sandbox.
- **Rehearse the v3 → v4 upgrade before the first stable v4 tag.** Both v3 and v4 hard-code
  `https://github.com/jlwilley/MultiWiz` as their update source, so the real upgrade can only be
  rehearsed against a scratch repository:
  1. Create an empty public repository, for example `jlwilley/MultiWiz-updatetest`.
  2. Check out tag `v3.3.5`. Change the `GithubSource` URL in `MultiWiz/MainWindow.xaml.cs` to the
     scratch repository and push that tree to the scratch repository's `master`. Its old release
     workflow publishes a v3 release there. Before pushing, pin vpk in that workflow to the version
     that built the real v3.3.5 release, so the scratch install gets the same `Update.exe` and
     packaging as real v3 installs: change `dotnet tool install -g vpk` to
     `dotnet tool install -g vpk --version 0.0.1298`. If that 2025 workflow no longer runs, bump its
     action versions too.
  3. In the scratch repository, add the repository variable `STABLE_RELEASES_ENABLED` = `true`. In a v4
     working copy, change `AppInfo.RepositoryUrl` (`src/MultiWiz.App/AppInfo.cs`) to the scratch
     repository. Push it to another branch of the scratch repository, then push the tag `v4.0.0` there.
  4. In Windows Sandbox, install the scratch repository's v3 `MultiWiz-win-Setup.exe`, add a dummy
     account and restart MultiWiz 3. It should download 4.0.0 and prompt you to restart. After the
     restart, v4 should offer to import the dummy account.
  5. Delete the scratch repository afterwards.
