# LUFFY

A free Windows loudness meter by Andreas Busk, with its static website.

## Versions

- Latest public download: [LUFFY v3.17 beta](https://github.com/userewkrewljkel/luffy/releases/tag/3.17) with a Windows x64 EXE and SHA-256 checksum.
- Previous beta: [v3.14](https://github.com/userewkrewljkel/luffy/releases/tag/3.14), still available for rollback.
- **Matching v3.17 source:** use the [3.17 release tag](https://github.com/userewkrewljkel/luffy/tree/3.17) from the 3.17 feature branch. The `main/app/` directory currently contains the older v3.14 app source and is not the source of the v3.17 EXE.
- The v3.17 beta passed automated Windows compilation and release-upload checks. Full manual audio, screenshot, playback and device testing is still outstanding. Test before production use.

## Website

The English website uses plain HTML, CSS and JavaScript. Open `index.html` locally, keeping the adjacent files together.

| File | Purpose |
| --- | --- |
| `index.html` | Page content |
| `styles.css` | Responsive design |
| `config.js` | Public version and download/donation destinations |
| `app.js` | Version labels and button availability |
| `luffy-paw.svg` | Logo and favicon |

The download URL in `config.js` points to the published v3.17 beta release asset. Donations remain disabled because no payment address has been configured. Empty or non-HTTPS destinations disable their buttons. No analytics, cookies, external fonts or third-party scripts are used.

Before changing the public version, test the Windows build, upload it to GitHub Releases, then update `version` and `downloadUrl` together. A donation URL must belong to the intended recipient; leave it empty until available.

## Build the Windows app

To rebuild v3.17, check out the `3.17` tag (not `main`). Install the .NET 8 SDK on Windows, then run from the release tag's repository root:

```powershell
dotnet build app/LufsTaskbarMeter.csproj -c Release
dotnet publish app/LufsTaskbarMeter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The executable is written to `app/bin/Release/net8.0-windows/win-x64/publish/`. The existing `app/BUILD-WINDOWS.ps1` also launches the app after publishing; the commands above only build it. See the app README for device, capture and playback tests. A successful build does not verify audio accuracy or interactive Windows behavior.

## Checks and changes

Requires Node.js 22 or later; there are no npm dependencies:

```powershell
node --test
node scripts/check-site.mjs
```

GitHub Actions runs these checks and a Windows .NET build on pull requests and pushes to `main`. Website checks validate local resources, anchors, script syntax, configured URLs, version labels and the release asset. Button tests cover working, missing and unsafe destinations.

Work on a branch, open a pull request and wait for `Website checks` and `Windows build` before merging. GitHub Pages currently publishes `main` automatically; the checks alone do not enforce a publishing gate. Configure the protection described in [repository setup](docs/repository-setup.md) to require them before merging.

## Release checks

Test source selection, pause/resume, reset, warnings on each monitor, capture and playback on Windows. Compare loudness readings against known reference signals. The v3.17 beta is released for testing, not verified production use. Test on another Windows PC and check its SHA-256 hash before relying on it. Keep the previous release available for rollback.

Contact: contact@andreasbusk.com
