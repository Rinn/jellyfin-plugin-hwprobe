# HwProbe for Jellyfin

[![ci](https://img.shields.io/github/actions/workflow/status/Rinn/jellyfin-plugin-hwprobe/ci.yml?branch=main&label=ci&logo=github)](https://github.com/Rinn/jellyfin-plugin-hwprobe/actions/workflows/ci.yml)
[![release](https://img.shields.io/github/v/release/Rinn/jellyfin-plugin-hwprobe?label=release)](https://github.com/Rinn/jellyfin-plugin-hwprobe/releases/latest)
[![jellyfin](https://img.shields.io/static/v1?label=jellyfin&message=12.2%2B&color=00A4DC&logo=jellyfin)](https://jellyfin.org)
[![license](https://img.shields.io/github/license/Rinn/jellyfin-plugin-hwprobe)](LICENSE)
[![downloads](https://img.shields.io/github/downloads/Rinn/jellyfin-plugin-hwprobe/total)](https://github.com/Rinn/jellyfin-plugin-hwprobe/releases)

Jellyfin lists every hardware acceleration backend, whether or not the server supports it. HwProbe tests each one with short transcodes, provides a fix for each failure where available, and measures the speed of the ones that work.

## Disclaimer

AI-generated with Claude Code.

## Install

Requires Jellyfin 12.2 or newer.

1. In **Dashboard > Plugins > Manage Repositories**, add:

   ```
   https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json
   ```

2. Install **HwProbe** from **Plugins > Available**.
3. Restart Jellyfin.

## Usage

Open **HwProbe** in the dashboard sidebar.

- **Hardware Probe**: **Run probe** tests each hardware acceleration backend and provides a fix for each failure where available.
- **Recommended Settings**: the advised Transcoding and Trickplay settings for each working backend and for software. **Apply** changes a setting and **Revert** undoes the last change.
- **Performance Tests**: measures transcode speed, from a test suite (such as encoder presets, tone mapping, or deinterlacing) or a custom selection.
- **Test Results**: speed and resource use (CPU, memory, GPU, and power) per run, with suggested settings that can be applied and reverted.
- **Help**: a diagnostics zip for [hardware reports](../../issues/new?template=hardware-report.yml) (includes file paths and host names) and the cached test clips.

No data leaves the server. Test samples are Creative Commons or public domain, downloaded only when needed, and verified against pinned hashes.

## Backend Verification

✅ marks backends tested on real hardware. Results for the others are welcome as a [hardware report](../../issues/new?template=hardware-report.yml).

- ❔ AMD AMF
- ✅ Nvidia NVENC
- ✅ Intel Quicksync (QSV)
- ✅ Video Acceleration API (VAAPI) on Intel
- ❔ Video Acceleration API (VAAPI) on AMD
- ❔ Rockchip MPP (RKMPP)
- ✅ Apple VideoToolBox
- ❔ Video4Linux2 (V4L2)

[Contributing](CONTRIBUTING.md) · [Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)
