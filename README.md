# HwProbe for Jellyfin

Tests which hardware transcoding options work on a Jellyfin server.

Jellyfin lists every hardware acceleration option on every server, whether or not the GPU supports it. HwProbe runs short test transcodes and shows, for each option on the Transcoding page:

- whether it works on this server
- how to fix it, if it doesn't

You can apply the results to Jellyfin's settings and revert them later.

## Install

Requires Jellyfin 12.1 or newer.

1. **Dashboard > Plugins > Manage Repositories > New Repository**, with this URL:

   ```
   https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json
   ```

2. Install **HwProbe** from **Plugins > Available**.
3. Restart Jellyfin.

## Use

1. Make sure nothing is playing.
2. **Dashboard > Plugins > HwProbe > Settings > Run probe**. The first run takes a few minutes.
3. Optionally, press **Apply** on the options you want to change.

## Privacy

- Nothing is sent anywhere.
- Test clips may be downloaded from [FFmpeg's sample suite](https://fate-suite.ffmpeg.org/) when they can't be made on the server. Each is downloaded once and checked against a pinned hash.

## Status

Work in progress.

- Tested on real hardware: Intel QuickSync and VAAPI on Linux, Apple VideoToolbox on macOS.
- Not tested on real hardware: NVIDIA, AMD, Rockchip and V4L2. Results for these may be wrong.

Report problems in [Issues](../../issues).

Written with substantial help from Claude (Anthropic) via Claude Code, directed and reviewed by the repository owner.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE)
