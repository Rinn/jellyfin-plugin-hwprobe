# HwProbe for Jellyfin

Jellyfin offers every hardware acceleration backend on every server, whether or not the GPU supports it. HwProbe runs short test transcodes to show which backends work, how to fix the ones that don't, and how fast the working ones are.

## Install

Requires Jellyfin 12.1 or newer.

1. In **Dashboard > Plugins > Manage Repositories**, add `https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json`
2. Install **HwProbe** from **Plugins > Available** and restart Jellyfin.

## Usage

Open **HwProbe** in the dashboard sidebar.

- **Hardware Probe**: press **Run probe** to test each hardware acceleration backend.
- **Recommended Settings**: each setting on the Transcoding and Trickplay pages, with whether it works, for each working backend and for software (None), which is advised even when no hardware works: HEVC and AV1 encoding off, BWDIF deinterlacing and key-frame trickplay from their tests, and subtitle extraction on so text subtitles aren't burned in. **Apply** changes it and **Revert** undoes the last change.
- **Performance Tests**: run a test suite (encoder presets, quality ladder, decoding, tone mapping, thread count, Intel low power, subtitle burn-in, deinterlacing, VBR audio, or legacy formats), or pick what to measure and press **Measure performance**. Measurements already made with the same settings can be reused, so a repeated comparison completes faster. By default a test pauses while the server transcodes for a player and repeats the measurement that was interrupted; it can instead keep measuring or cancel.
- **Test Results**: the measurements, as bars, values, a share of the fastest, or the CPU, memory, GPU, and power each transcode used. Earlier runs can be viewed or deleted. Suggestions from the results (a faster hardware backend, outputs that fall behind, settings worth changing) can be applied and reverted.
- **Help**: download a zip to attach to a [hardware report](../../issues/new?template=hardware-report.yml) (it includes file paths and host names), and see or delete the cached test clips. Clips the current version no longer uses, such as those made by an earlier ffmpeg, are deleted automatically.

No data leaves the server. Test samples are downloaded only when needed and checked against pinned hashes.

## Status

Tested on Intel (QSV, VAAPI), NVIDIA (NVENC), and Apple (VideoToolbox). AMD, Rockchip, and V4L2 are untested on real hardware.

## Disclaimer

AI-generated with Claude Code.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)
