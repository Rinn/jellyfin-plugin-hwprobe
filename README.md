# HwProbe for Jellyfin

Tests which hardware transcoding options work on your Jellyfin server.

Jellyfin offers every hardware acceleration choice on every server, whether or not your GPU supports it. HwProbe runs short test transcodes and shows, for each option on the Transcoding page, whether it works here, with a fix where one exists. It doesn't change any settings.

## Install

Requires Jellyfin 12.1 or newer.

1. **Dashboard > Plugins > Manage Repositories > New Repository**, with this URL:

   ```
   https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json
   ```

2. Install **HwProbe** from **Plugins > Available**, then restart Jellyfin.

## Use

**Dashboard > Plugins > HwProbe > Run probe**, while nothing is playing. The first run takes a few minutes.

## Command-line version

For testing without the plugin, download the build for your system from [Releases](../../releases) and run it where Jellyfin runs (inside the container, for Docker). `--help` lists the options.

## Privacy

Nothing is sent anywhere. The only download is a 124 KB VC-1 test clip from [FFmpeg's sample suite](https://fate-suite.ffmpeg.org/vc1/), once, checked against a pinned hash.

## Status

Tested on real hardware with Intel QuickSync and VAAPI on Linux, and VideoToolbox on macOS (Apple Silicon). Report problems in [Issues](../../issues).

Written with substantial help from Claude (Anthropic) via Claude Code, directed and reviewed by the repository owner.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE)
