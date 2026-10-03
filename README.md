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

The **Speed** tab measures how fast the working options are. Choose videos (generated test videos, a library movie or episode, or short samples (5 to 12 seconds) of freely licensed films: live action with CGI, 3D animation and animation, downloaded only when chosen) and outputs (H.264, HEVC or AV1 at the player's quality choices, from 420 kbps to 120 Mbps, or decoding alone; Jellyfin picks the size from the quality, as it does for a player), then press **Measure speed**. Every chosen output is made from every chosen video. For each working backend and software, the results show how many of each transcode keep up with playback at the same time, and how fast one runs alone; the table shows every planned measurement from the start and fills in as each finishes, with the time left. A run can be paused after its current measurement, resumed, or cancelled, keeping what's finished. The backends to measure are chosen first, with Intel's low-power encoders as a setting for QSV when its probe found them. Jellyfin's transcoding settings (decoders, VPP tone mapping, thread count, VBR audio, encoding preset, CRF and deinterlacing) can be set for the run, in the Transcoding page's order and defaulting to the server's; audio can be copied, and subtitles burned in, as for particular clients. A hardware backend's column only measures transcodes it encodes on the GPU, and decode tests it decodes on the GPU; the rest are skipped, and software has its own column. Accuracy, repeats (each measurement run up to three times, reporting the median) and a time limit for each measurement are chosen under **Accuracy and time**. Earlier runs are kept and can be picked to view again. Measure while nothing is playing and the server is otherwise idle. The command-line tool does the same with `--speed`, `--speed-videos`, `--speed-outputs`, `--speed-repeats`, `--speed-option KEY=VALUE`, `--speed-backends`, `--speed-time-limit` and `--speed-file <video>`.

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
