# HwProbe for Jellyfin

Tests which hardware transcoding options work on a Jellyfin server.

Jellyfin lists every hardware acceleration option on every server, whether or not the GPU supports it. HwProbe runs short test transcodes and shows, for each option on the Transcoding and Trickplay pages:

- whether it works on this server
- how to fix it, if it doesn't

Results can be applied to Jellyfin's settings and reverted later.

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
2. Open **HwProbe** in the dashboard sidebar, under Plugins, and press **Run probe**. The first run takes a few minutes.
3. Optionally, press **Apply** on the options to change.

If Jellyfin is set to a backend that didn't work, the page says so and offers **Switch to None**. Changing the backend needs a restart, which the page offers.

After updating HwProbe or changing ffmpeg, run the probe again: older results are cleared.

The **Speed** tab measures how fast the working options are. Choose videos (generated test videos, a library movie or episode, or 30-second samples of freely licensed films: live action, digital animation and anime, downloaded only when chosen) and outputs (720p or 1080p H.264, HEVC or AV1, with or without burned-in subtitles, or decoding alone), then press **Measure speed**. Every chosen output is made from every chosen video. For each working backend and software, the results show how many of each transcode keep up with playback at the same time, and how fast one runs alone; they fill in as each measurement finishes. Accuracy, repeats (each measurement run up to three times, reporting the median) and a time limit for the whole run are chosen under **Accuracy and time**. Earlier runs are kept and can be picked to view again. Measure while nothing is playing and the server is otherwise idle. The command-line tool does the same with `--speed`, `--speed-videos`, `--speed-outputs`, `--speed-repeats`, `--speed-time-limit` and `--speed-file <video>`.

To share results, press **Download diagnostics** under **Diagnostics** on the Probe tab and attach the zip to a [hardware report](../../issues/new?template=hardware-report.yml). The command-line tool writes the same zip with `--diagnostics <file.zip>`.

## Privacy

- Nothing is sent anywhere.
- The diagnostics zip is only made when downloaded from the page or written with `--diagnostics`. It holds the report and every ffmpeg log from the last probe, including file paths and the server's user and host names. Check it before sharing.
- Speed samples are only downloaded when chosen, from Wikimedia Commons: about 10 to 110 MB each, as each one's label says, and checked against a pinned hash. The page shows the cache's size and can delete it.
- Test clips may be downloaded from [FFmpeg's sample suite](https://fate-suite.ffmpeg.org/) when they can't be made on the server. Each is downloaded once and checked against a pinned hash.

## Status

Work in progress.

- Tested on real hardware: Intel QuickSync and VAAPI on Linux, NVIDIA NVENC on Windows and Linux, Apple VideoToolbox on macOS.
- Not tested on real hardware: AMD, Rockchip and V4L2. Results for these may be wrong.

Report problems in [Issues](../../issues), with the diagnostics zip.

Written with substantial help from Claude (Anthropic) via Claude Code, directed and reviewed by the repository owner.

[Building from source](DEVELOPMENT.md) · [GPL-3.0](LICENSE)
