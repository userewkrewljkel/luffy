# LUFFY — Getting started (Windows x64)

This guide covers the publicly distributed LUFFY 3.17 beta.

## Download and run

1. Download `LUFFY-v3.17.exe` from the [official release page](https://github.com/userewkrewljkel/luffy-site/releases/tag/3.17).
2. Optionally compare its SHA-256 checksum against `SHA256SUMS.txt` available with the release.
3. Run the Windows x64 EXE. It is self-contained; no separate .NET installation is required.
4. The beta is not code-signed. Windows may display an unknown-publisher warning. Use only the official download.

## Choose an audio device

Open **Settings → Audio** (click the gear or double-click LUFFY), and select an available Windows audio input or output device. Play audio and check that the meter responds.

## Ableton Live, FMOD and ASIO

LUFFY monitors a Windows audio source. It does **not** capture ASIO directly, and it cannot identify the individual DAW tracks or FMOD events responsible for a peak.

If your DAW sends audio through ASIO, you may need your audio interface's loopback feature to expose the output as a compatible Windows input. Availability and naming depend on the interface. Test the actual routing rather than assuming LUFFY hears the DAW.

## True Peak, WAV captures and screenshots

- Adjust the True Peak limit and screen-edge warning in **Settings → True Peak**.
- Peak Capture WAV and True Peak screenshots are separate optional features, both off by default.
- WAV recordings are stored locally, by default under `Music/LUFFY/Peak Captures`.
- Desktop screenshots are stored locally, by default under `Pictures/LUFFY/Peak Screenshots`. They may include **every connected monitor** and confidential information.
- A screenshot is captured **after** a detected True Peak crossing, so the screen may already have changed.
- Each capture feature has its own Recycle Bin cleanup action.

## Playback limitation

While listening to a saved WAV in the built-in player, live measurement is paused. It resumes after playback with the prior history preserved, but audio arriving during preview is excluded.

## Beta and feedback

Manual compatibility and measurement-reference testing of v3.17 is still outstanding. For important deliveries, compare LUFS and True Peak readings against a trusted reference meter and keep a previously working build for rollback.

For problems, use the [public bug-report form](https://github.com/userewkrewljkel/luffy-site/issues/new?template=bug_report.yml), removing sensitive details from logs and screenshots, or send sensitive reports to contact@andreasbusk.com.
