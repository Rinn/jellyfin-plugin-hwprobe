# HwProbe for Jellyfin

Jellyfin offers every hardware acceleration option on every server, whether or not the GPU supports it. HwProbe runs short test transcodes to show which options work, how to fix the ones that don't, and how fast the working ones are.

## Install

Requires Jellyfin 12.1 or newer.

1. In **Dashboard > Plugins > Manage Repositories**, add `https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json`
2. Install **HwProbe** from **Plugins > Available** and restart Jellyfin.

## Use

Open **HwProbe** in the dashboard sidebar while nothing is playing.

- **Probe**: press **Run probe**. Each option on the Transcoding and Trickplay pages shows whether it works; **Apply** changes it and **Revert** undoes the last change.
- **Speed**: pick what to measure and press **Measure speed**.
- **Diagnostics** (bottom of the Probe tab): download a zip to attach to a [hardware report](../../issues/new?template=hardware-report.yml). It includes file paths and host names.

No data leaves the server. Test samples are downloaded only when needed and checked against pinned hashes.

## Status

Tested on Intel (QSV, VAAPI), NVIDIA (NVENC) and Apple (VideoToolbox). AMD, Rockchip and V4L2 are untested on real hardware.

## Disclaimer

AI-generated with Claude Code.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE)
