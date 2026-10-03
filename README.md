# Transcoding Diagnostics for Jellyfin

Jellyfin offers every hardware acceleration option on every server, whether or not the GPU supports it. Transcoding Diagnostics runs short test transcodes to show which options work, how to fix the ones that don't, and how fast the working ones are.

## Install

Requires Jellyfin 12.1 or newer.

1. In **Dashboard > Plugins > Manage Repositories**, add `https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json`
2. Install **HwProbe** from **Plugins > Available** and restart Jellyfin. It appears in the dashboard sidebar as **Transcoding Diagnostics**.

## Use

Open **Transcoding Diagnostics** in the dashboard sidebar while nothing is being transcoded.

- **Hardware Probe**: press **Run probe** to test each hardware acceleration option.
- **Recommended Settings**: each option on the Transcoding and Trickplay pages, with whether it works. **Apply** changes it and **Revert** undoes the last change.
- **Performance Tests**: pick what to measure and press **Measure performance**. Measurements already made with the same settings can be reused, so a repeated comparison runs faster.
- **Test Results**: the measurements, as bars, values or a share of the fastest. Earlier runs can be viewed or deleted.
- **Help**: download a zip to attach to a [hardware report](../../issues/new?template=hardware-report.yml) (it includes file paths and host names), and see or delete the cached test clips. Clips the current version no longer uses, such as those made by an earlier ffmpeg, are deleted automatically.

No data leaves the server. Test samples are downloaded only when needed and checked against pinned hashes.

## Status

Tested on Intel (QSV, VAAPI), NVIDIA (NVENC) and Apple (VideoToolbox). AMD, Rockchip and V4L2 are untested on real hardware.

## Disclaimer

AI-generated with Claude Code.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE)
