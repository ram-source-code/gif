# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

## Users

Everyday Windows 10/11 users who want an animated GIF as their desktop wallpaper without fuss or slowdown. Three overlapping groups, all confirmed:

- **Anyone wanting a living desktop**, often non-technical: they pick a GIF and expect it to just work.
- **Gamers and setup enthusiasts** who care how their desktop looks but can't afford a wallpaper app taking FPS or RAM.
- **People on low-end PCs and laptops** who were put off by heavy wallpaper engines.

The job: set a GIF as the wallpaper once, then forget the app is running.

## Product Purpose

Play any animated GIF behind the desktop icons, and nothing more. In the owner's words: *you don't need big fancy apps just to load a GIF in the Windows desktop background.* Success means the wallpaper plays, survives restarts if the user wants, and the PC behaves as if the app were not there.

## Positioning

Against heavier wallpaper apps (Wallpaper Engine, Lively and similar), all of these must stay true:

- **Feels like it isn't there:** near-zero CPU and RAM, pauses itself when covered, locked or on low battery, no background clutter.
- **One portable exe:** no installer, no admin rights; put it anywhere, delete it and it's gone.
- **Free and open source:** MIT, readable source, builds with the C# compiler already in Windows (`build.cmd`).
- **Dead simple:** pick a GIF and it plays. Few settings, no accounts, no store.

## Operating Context

- Downloaded as `GifWinLive.exe` from GitHub Releases (ram-source-code/gif), often run straight from Downloads or the Desktop. SmartScreen warns on first launch because the exe is unsigned.
- Opened once to choose a GIF, then lives behind the desktop. Reopening the exe brings the control panel back; closing the panel keeps the wallpaper playing.
- Optional Start with Windows (`--background` at sign-in). `--stop` ends it.

## Capabilities and Constraints

- **Capabilities:** plays a GIF in the desktop layer behind icons (primary screen, span, or each monitor); Fill, Fit, Stretch, Center; max frame rate; background colour; Memory: Auto, Less CPU, Less RAM; pause when covered, on lock, and on low battery (unplugged and 20 % or less, or Battery saver); Start with Windows; Remove from this PC.
- **Must hold for every change:**
  - No network access and no telemetry, ever.
  - Windows 10 21H2 or newer and Windows 11 only (older versions are not supported), on the .NET Framework 4.8 built into them; no extra runtimes to install.
  - The control panel follows the user's Windows light/dark mode and accent colour, like a built-in Windows tool.
  - Portable: settings are a hidden `GifWinLive.json` next to the exe (or `%APPDATA%\GifWinLive` if that folder is read-only); the only thing written elsewhere is the optional sign-in entry.
- **Terminology:** "Choose GIF" / "Change GIF", "Play" / "Stop", "Start with Windows", "Remove from this PC", "Less CPU" / "Less RAM".
- **Stack (existing):** C# 5 with WPF and GDI+, compiled by `build.cmd`; sources in `src/`.

## Brand Commitments

- Name: **GIF WinLive** (exe: `GifWinLive.exe`). Renamed from "GIF Live Wallpaper" before anyone saw it; the app migrates settings and the sign-in entry from the old name.
- Icon: `src/app.ico`, supplied by the owner. It replaced a generated icon the owner rejected as looking AI-made, so do not substitute generated artwork or palettes for the owner's assets.

## Evidence on Hand

- Measured footprint in `README.md` (Windows 10, 1080p, Memory: Auto): about 30–60 MB in Task Manager and roughly 0–4 % of one CPU core while playing; 0 % while paused.
- `screenshot.png`: the control panel's empty state.
- No testimonials, user counts, reviews or download figures exist. Do not invent any.
- The local sample GIFs are not cleared for redistribution and are excluded from the repo; don't use them in public material.

## Product Principles

1. **Invisible by default.** Every feature is judged by what it costs the PC; when in doubt, use less.
2. **One file, no traces.** Nothing installs, nothing phones home, and removal leaves nothing behind.
3. **Choosing a GIF is the whole onboarding.** Anything beyond that is optional and stays out of the way.
4. **Feel native to Windows.** Familiar controls, the user's own theme, no invented affordances.
