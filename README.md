# LUFFY

A free Windows loudness meter by Andreas Busk, with its static website.

## Versions

- Public download: [v3.14 beta](https://github.com/userewkrewljkel/luffy/releases/tag/3.14).
- Development source: **v3.14 beta**, in [`app/`](app/). Imported from the local `LUFFY-v3.14-source` project; generated binaries and build caches are excluded. This import does not prove that a published EXE was built from this source.
- The v3.14 beta executable is built from the tagged source by GitHub Actions. Automated build checks pass; the full manual audio and playback checklist in [`app/README.md`](app/README.md) remains outstanding. Keep the previous release available.

## Website

The English website uses plain HTML, CSS and JavaScript. Open `index.html` locally, keeping the adjacent files together.

| File | Purpose |
| --- | --- |
| `index.html` | Page content |
| `styles.css` | Responsive design |
| `config.js` | Public version and download/donation destinations |
| `app.js` | Version labels and button availability |
| `luffy-paw.svg` | Logo and favicon |

The download URL in `config.js` points to the v3.14 release asset. Donations remain disabled because no payment address has been configured. Empty or non-HTTPS destinations disable their buttons. No analytics, cookies, external fonts or third-party scripts are used.

Before changing the public version, test the Windows build, upload it to GitHub Releases, then update `version` and `downloadUrl` together. A donation URL must belong to the intended recipient; leave it empty until available.

## Build the Windows app

Install the .NET 8 SDK on Windows, then run from the repository root:

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

Test source selection, pause/resume, reset, warnings on each monitor, capture and playback on Windows. Compare loudness readings against known reference signals. Test the EXE on another Windows PC before publishing and include its SHA-256 hash. Keep the previous release available for rollback.

Contact: contact@andreasbusk.com
