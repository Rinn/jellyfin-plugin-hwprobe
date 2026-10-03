# HwProbe for Jellyfin

Tests which hardware transcoding options work on a Jellyfin server.

Jellyfin lists every hardware acceleration option on every server, whether or not the GPU supports it. HwProbe runs short test transcodes and shows which options work, how to fix the ones that don't, and how fast the working ones are. Advised settings can be applied to Jellyfin and reverted later.

## Install

Requires Jellyfin 12.1 or newer.

1. **Dashboard > Plugins > Manage Repositories > New Repository**, with this URL:

   ```
   https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json
   ```

2. Install **HwProbe** from **Plugins > Available**, then restart Jellyfin.

## Use

Open **HwProbe** in the dashboard sidebar, under Plugins, while nothing is playing.

- **Probe**: press **Run probe** to test every backend; the first run takes a few minutes. Each option on the Transcoding and Trickplay pages shows whether it works, with **Apply** to change it and **Revert** to undo.
- **Speed**: choose backends, inputs, codecs and qualities, then press **Measure speed** to see how fast each backend transcodes and how many transcodes keep up at once.
- **Diagnostics**, at the bottom of the Probe tab: download a zip of the results and ffmpeg logs to attach to a [hardware report](../../issues/new?template=hardware-report.yml).

After updating HwProbe or changing ffmpeg, run the probe again; older results are cleared.

The same probe runs from the command line, where Jellyfin runs (inside the container, for Docker):

```sh
hwprobe                    # probe every backend
hwprobe --speed confirm    # and measure speed
```

`hwprobe --help` lists the options.

## Privacy

- Nothing is sent anywhere.
- The diagnostics zip is only made on request. It includes file paths and the server's user and host names; check it before sharing.
- Film samples for speed runs (2 to 21 MB each) are downloaded from Wikimedia Commons only when chosen. Test clips that can't be made on the server come from [FFmpeg's sample suite](https://fate-suite.ffmpeg.org/). All downloads are checked against pinned hashes and cached.

## Status

Tested on Intel QuickSync and VAAPI (Linux), NVIDIA NVENC (Windows, Linux) and Apple VideoToolbox (macOS). AMD, Rockchip and V4L2 are not tested on real hardware, so results for them may be wrong; report problems in [Issues](../../issues) with the diagnostics zip.

Written with substantial help from Claude (Anthropic) via Claude Code, directed and reviewed by the repository owner.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE)
