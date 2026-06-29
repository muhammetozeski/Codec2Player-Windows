# Codec2Player

A small Windows desktop player for **Codec 2** (`.c2`) speech files — including the
ultra‑low‑bitrate **450 bps** mode. WinForms UI, a simple playlist and a transport
bar.

[![build](https://github.com/muhammetozeski/Codec2Player-Windows/actions/workflows/build.yml/badge.svg)](https://github.com/muhammetozeski/Codec2Player-Windows/actions/workflows/build.yml)

---

## Why this exists

Nothing off the shelf plays `.c2` files: ffplay/ffmpeg only read old‑header files up
to 700C, VLC has no Codec 2 support, and FreeDV is a live‑radio app, not a file
player. Codec2Player fills that gap and also plays the **450 / 450PWB** modes that
upstream Codec 2 dropped just before v1.2.0.

## How it plays Codec 2 "directly"

A sound card only accepts PCM, so every codec player — MP3, Opus, Codec 2 — decodes
to PCM in memory and hands those samples to the OS audio sink. Codec2Player does
exactly that, with no intermediate file:

```
.c2 file  ──►  libcodec2 (decode)  ──►  16‑bit PCM in memory  ──►  winmm waveOut  ──►  speakers
```

That is the same design as a native Android player (libcodec2 decode → `AudioTrack`);
here the output sink is the Windows `waveOut` API. The only dependencies are
`libcodec2.dll` (the decoder) and `winmm.dll` (a Windows system library) — no
third‑party audio library.

## Features

- Playlist: add individual `.c2` files or a whole folder.
- Play / pause / stop, previous / next, auto‑advance.
- Seekable progress bar with elapsed / total time.
- Volume control.
- Detects the mode from each file's header and shows it (e.g. `[450]`).
- Plays every Codec 2 mode: 3200, 2400, 1600, 1400, 1300, 1200, 700C, **450, 450PWB**.

## Requirements

- Windows x64.
- The release build is self‑contained — no .NET install needed. If you build from
  source, you need the **.NET 10 SDK**.

## Download

Get the latest build from the
[**Releases**](https://github.com/muhammetozeski/Codec2Player-Windows/releases) page and run
`Codec2Player.exe`.

## Build from source

```powershell
git clone --recursive https://github.com/muhammetozeski/Codec2Player-Windows.git
cd Codec2Player-Windows
dotnet build src/Codec2Player.csproj -c Release
dotnet run  --project src/Codec2Player.csproj -c Release
```

`src/native/win-x64/libcodec2.dll` (the decoder) is committed and copied next to the
executable automatically. To rebuild it from the Codec 2 submodule (requires
MinGW‑w64 GCC + CMake + Ninja on `PATH`):

```powershell
./scripts/build-native.ps1
```

## How a `.c2` file is read

Codec 2 files start with a 7‑byte header — magic `C0 DE C2`, version, **mode**, flags
— followed by fixed‑size frames. The player reads the mode from the header, creates a
matching `libcodec2` decoder, and decodes each frame to 320 PCM samples (8 kHz mono;
450PWB decodes to 16 kHz). Note that the 450 vocoder synthesises unvoiced segments
with randomised phase, so its output is not bit‑identical run to run — by design, and
inaudible.

## Credits & license

- **Codec 2** © David Rowe and contributors — [LGPL‑2.1](https://github.com/drowe67/codec2)
  (submodule, built unmodified into `libcodec2.dll`).
- This player's code, scripts, CI and docs are MIT‑licensed — see [LICENSE](LICENSE).
- Companion project that *creates* `.c2` files incl. 450:
  [C2EncWindows](https://github.com/muhammetozeski/C2EncWindows).
