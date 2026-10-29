# LUFFY v3.14 Beta — Windows source

Small Windows loudness meter by Andreas Busk. **Beta: keep your working v3.13 executable as a backup.**

## What's new in 3.14

**Playback Mode:** When you play a saved WAV in the built-in player, the compact
LUFFY bar switches to **LUFFY v3.14 · PLAYBACK**. Its stereo bars, Integrated
LUFS, Short-term LUFS, True Peak and clock reflect the ORIGINAL saved WAV samples
(before any listening gain). The live WASAPI measurement continues to be paused
and is restored afterward without losing its Integrated LUFS history. The compact
Pause/Resume and Reset buttons control playback when the player is active:
Reset jumps to the beginning of the WAV, and seeking resets that preview's
Integrated LUFS calculation so skipped audio is never counted as measured.
Playback analysis uses a dedicated `LoudnessMeter` instance and is not connected
to Peak Capture recording or TP warning events. Very short captures may not
accumulate enough audio for Short-term LUFS to become available.

**Delete all Peak Captures:** Settings → True Peak → Manage saved recordings.
The button asks for confirmation, turns off automatic capture, waits for pending
WAV writers, stops the internal player to release open files, and moves only
recognized `LUFFY_Peak_*.wav` files and matching `.wav.txt` companions to the
Windows Recycle Bin. Other WAV files, settings and `history.csv` are not touched.
If Recycle Bin is unavailable, deletion fails safely (no permanent-delete fallback).

## Previously added in 3.13

**Peak Capture Player** (Settings → True Peak → Open Peak Capture Player):

- An in-app, nonmodal player with a list of up to 300 recent local Peak Capture WAVs.
- True waveform preview generated in the background, showing the recorded audio and a dashed coral first-trigger marker when the companion `.wav.txt` metadata exists. The marker is rounded to the WASAPI callback, not sample-exact. The marker parser accepts both `.` and `,` decimal separators.
- Click in the waveform to seek; Play, Pause/Resume, Stop and Previous/Next clip.
- Select a playback device, including Windows default output. Playback uses NAudio's WASAPI shared mode. Outputs which are currently unavailable or used exclusively may fail gracefully with an error message.
- Adjustable **listening gain** from −24 to +12 dB and a quick **Original 0 dB** button. The original WAV is always read-only and never normalized or rewritten. Positive preview gain can clip the listening signal, and Windows/monitor gain is independent of FMOD gain.
- **Prevents self-recording:** During LUFFY's own WAV playback, the original live measurement is automatically paused, including its elapsed timer and True Peak capture/warning. The compact display instead shows a separate live measurement of the saved WAV file. It resumes the prior live session afterward, preserving Integrated LUFS, but live audio arriving during playback is deliberately excluded; it is not suitable for unattended monitoring while reviewing clips.
- A separate **Open WAV externally** control is still provided. Playback by an external program is not automatically isolated from system loopback capture.
- Persist the listening gain and preferred preview output in `settings.json` without overwriting earlier choices.

Everything from v3.12 (stereo meter, settings gear, input selection, original Peak Capture, 5 s before/after default, True Peak warnings and loudness engine) is retained. Peak Capture remains OFF for first-time users.

## Before sharing

**A self-contained Windows x64 beta is available in the GitHub 3.14 release.** GitHub Actions compiles it from the tracked source and publishes a SHA-256 checksum. The full manual audio, device and playback checks below remain outstanding. You can also build the source on Windows using the .NET 8 SDK.

1. Extract the ZIP.
2. Run `BUILD-WINDOWS.ps1` from the extracted `LUFFY-v3.14` folder.
3. It runs `dotnet publish -c Release -r win-x64 --self-contained true` and starts the resulting `LUFFY-v3.14.exe`.
4. Test the new player, waveform, listening gain, output switching, automatic measurement pause/resume, and WAV capture before publishing to testers.

## Capture notes and limitations

- Peak Capture works on the **selected Windows audio source**; direct ASIO capture is not supported. A DAW using ASIO requires a loopback input provided by an audio interface.
- It is a continuously updated in-memory buffer only when Peak Capture is enabled. No disk writes occur on the real-time audio callback; completed clips are written in a background task.
- A clip can be shorter at the beginning of a measurement or when interrupted. Additional above-limit buffers can extend the clip; maximum 60 seconds. Under sustained loudness a clip is capped at 60 seconds. Recording re-arms when the signal falls below threshold and crosses again. Turn it off after debugging and monitor disk usage.
- Threshold is the same as the True Peak warning threshold, but **Peak Capture can operate while the visual warning is off**. The alert overlay itself is unchanged.
- The beginning of the clip is rounded to an audio callback boundary (typically milliseconds). The metadata text gives an approximate trigger position, not a sample-accurate seek marker.
- If you select a high sample rate or many channels, resulting WAVs consume more disk space. For example, 10 sec × 48 kHz × 2 ch × 32-bit = ~3.8 MB.
- The app does not identify the FMOD event responsible for a peak; it captures the actual audio signal.
- Stored settings from existing versions are preserved. New Peak Capture defaults to off, 5s+5s, Music folder.
- The app currently supports 16/24/32-bit PCM and 32-bit float WASAPI sources.
- True Peak calculations may differ marginally from Youlean and should be validated before critical compliance work.

## Feedback and data

Error log: `%LOCALAPPDATA%\LufsTaskbarMeter\errors.log`.
History CSV: `%LOCALAPPDATA%\LufsTaskbarMeter\history.csv`.
Settings: `%LOCALAPPDATA%\LufsTaskbarMeter\settings.json`.
Contact: contact@andreasbusk.com

### Safety limits and manual test checklist

- Ring buffer memory is capped at 64 MiB and a single clip at 128 MiB. Unusually high channel counts or sample rates can shorten the requested pre-roll or clip. The application favors uninterrupted loudness measurement over unlimited audio buffering.
- If Peak Capture encounters a processing error, it disables recording for that run rather than disrupting the loudness measurement; the error is surfaced in Settings → Audio. Toggle recording off and on after fixing the problem.
- Suggested first test: set the True Peak threshold temporarily to −20 dBTP, enable Peak Capture, play a known short audio signal through the selected Windows source for several seconds, then check that a WAV and companion text file appear in Music/LUFFY/Peak Captures. Restore the threshold to −1 dBTP afterward.
- Suggested regression tests: Pause/Resume, Reset during capture, input device switching, repeated peaks in a short interval, enabling/disabling while running, a 30–60 minute uninterrupted playthrough, and warning appearance on each monitor. Verify Integrated LUFS and True Peak against Youlean as before.

## v3.14 player test checklist

1. Make a capture in v3.12 or v3.14 and check that it appears in the Peak Capture Player.
2. Confirm the waveform renders and the first-trigger marker is near the expected sample, even with Danish decimal commas in the `.wav.txt` metadata.
3. Play it at Original 0 dB; adjust gain; pause, resume and seek; stop. Ensure WAV bytes have **not** changed.
4. While a clip plays, verify the compact meter switches to PLAYBACK mode, measures the WAV (not listening gain), and playing the file does not generate a new WAV. Check live monitoring resumes immediately after Stop or natural end.
5. Change output device and repeat playback; unplug the device while playing and verify a graceful error without crashing.
6. Open several clips, close the player or Settings while playing, and confirm there is only one player and playback stops safely.
7. Compare Integrated LUFS and True Peak to your known v3.12 behavior once playback is stopped; the loudness algorithms are unmodified.

**Known limitations:** The first-trigger mark is approximate because the v3.12 recorder marks an entire WASAPI callback, not a sample. There is no multi-peak timeline, session tagging, automatic cleanup, or auto-update yet. The Windows build passes automated compilation. This does not verify audio accuracy or replace the manual device and playback checks below.

## v3.14 manual regression checks

1. Open a saved clip and press Play. The compact bar should show PLAYBACK and I/S/TP values and moving L/R peak bars after sufficient audio has played. The original live measurement remains paused.
2. Change listening gain while playing. The audible volume may change but the WAV's metering must not. Stop returns to the original live measurements and timer.
3. Use Pause/Resume and Reset in the compact bar during preview, then seek in the waveform; check the original live session is not reset.
4. Enable Peak Capture, create several WAVs, then click Delete all Peak Captures. Review the confirmation count, confirm, and check WAV + .wav.txt go to the Recycle Bin. Verify unrelated files and measurement history remain.
5. Try an empty folder and a read-only folder: no crash, and failures should be reported without permanently deleting anything.
6. Keep v3.13 to revert if playback analysis causes stuttering or excess CPU on the test PC.
