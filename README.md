# HwProbe for Jellyfin

Find out which hardware transcoding options actually work on your Jellyfin server.

Jellyfin's **Hardware acceleration** setting offers the same eight choices on every server
(AMD AMF, NVIDIA NVENC, Intel QuickSync, VAAPI and so on), whatever GPU you have. Choosing one
that doesn't work can mean every transcode fails, or quietly falls back to the CPU. HwProbe runs a
few tiny test transcodes on your server and tells you:

- which hardware acceleration choices work on this machine, and on which device;
- which codecs your GPU can decode and encode, so you know which boxes to tick;
- whether tone mapping, deinterlacing and subtitle burn-in run on the GPU;
- how to fix each one that doesn't work.

HwProbe only reads and tests. It never changes your Jellyfin settings.

## Install the plugin

Requires Jellyfin **12.1** or newer.

1. In Jellyfin, go to **Dashboard > Plugins > Manage Repositories > New Repository**.
2. Enter any name, for example `HwProbe`, and this **Repository URL**:

   ```
   https://raw.githubusercontent.com/Rinn/jellyfin-plugin-hwprobe/manifest/manifest.json
   ```

3. Go back to **Plugins**, find **HwProbe** under **Available**, and install it.
4. Restart Jellyfin.

## Run a probe

1. Make sure nothing is playing. HwProbe won't start while anyone is transcoding, so it doesn't
   disturb playback.
2. Go to **Dashboard > Plugins > HwProbe** and press **Run probe**. It can take a few minutes;
   the first run takes longest because it creates the test clips.
3. Read the results table and the findings below it.

The probe is also available as a scheduled task, **Probe hardware transcoding**, under
**Dashboard > Scheduled Tasks**. It has no schedule by default.

## Reading the results

Each row is one hardware acceleration choice on one device.

| Verdict | Meaning |
|---|---|
| **Viable** | It works. The Decode and Encode columns show which codecs passed. |
| **NotPresent** | No device for it was found, for example NVENC without an NVIDIA card. |
| **PermissionDenied** | The device exists, but Jellyfin isn't allowed to open it. |
| **DevicePresentPipelineBroken** | The device opens, but a test transcode failed or quietly used the CPU. |
| **NotBuilt** | Your Jellyfin's ffmpeg doesn't include this choice at all. |
| **Untested** | HwProbe couldn't check it, for example because no test clip was available. |

For a working choice, the **Pipeline** column says how much of the work stays on the GPU:

| Pipeline | Meaning |
|---|---|
| **FullOpencl**, **FullVulkan**, **FullMetal** | Scaling, tone mapping and subtitles all run on the GPU. |
| **Limited** | Scaling runs on the GPU; tone mapping doesn't. |
| **LegacyCopyBack** | Every frame is copied back to the CPU for filtering, which is much slower. |

Every failure comes with a suggested fix. The common ones:

- **Docker: no device.** Pass the GPU into the container, for example `--device /dev/dri` for
  Intel and AMD, or the NVIDIA container runtime (`--gpus all`) for NVIDIA.
- **Permission denied.** Add the render group to the container (`--group-add`), or add the
  `jellyfin` user to the `render` group on a normal install.
- **OpenCL doesn't start (Intel).** Tone mapping needs Intel's OpenCL runtime. The official
  `jellyfin/jellyfin` Docker image includes it. On `linuxserver/jellyfin`, it comes from the
  `jellyfin-opencl-intel` mod; check the container's start-up log shows it installing.
- **A codec fails.** Your GPU can't handle that codec. Leave it unticked in Jellyfin's hardware
  decoding list.

## Command-line version

HwProbe is also a standalone program, for testing before you install the plugin or on a machine
without Jellyfin. Download the file for your system from the
[releases page](https://github.com/Rinn/jellyfin-plugin-hwprobe/releases), unpack it and run it
(on Linux and macOS: `gunzip hwprobe-linux-x64.gz && chmod +x hwprobe-linux-x64`).
It finds Jellyfin's ffmpeg by itself, or you can point it at one with `--ffmpeg`. Run it where
Jellyfin runs, which for Docker means inside the container, so it tests the same ffmpeg and
devices. `--format summary` prints a short version that's easy to share; `--help` lists every option.

## Privacy and network access

HwProbe doesn't send data anywhere: no telemetry, no reports uploaded, nothing about your server
leaves the machine. It makes one outbound request:

| When | Request | Why |
|---|---|---|
| First run only | `GET https://fate-suite.ffmpeg.org/vc1/SA00050.vc1` (124 KB) | No free VC-1 encoder exists, so the VC-1 test clip is downloaded from FFmpeg's public test-sample suite instead of generated. It's checked against a pinned SHA-256 and cached, so later runs reuse it. Offline, VC-1 is reported as `Untested` and everything else still runs. |

All other test clips are generated on your server by Jellyfin's own ffmpeg.

## Status

Early development. Intel VAAPI and QuickSync have been tested on real hardware (one Intel
Apollo Lake GPU on Linux). The other choices are checked by automated tests but haven't been run on
real GPUs, and the report says so where a result can't be confirmed.

Found a problem? [Open an issue](https://github.com/Rinn/jellyfin-plugin-hwprobe/issues) and
include the output of a probe.

## AI disclosure

This project was written with substantial help from an AI coding assistant: Claude, by Anthropic,
used through Claude Code. The repository owner directed the work, made the design decisions and
reviewed the results along the way; the assistant wrote most of the code, tests, scripts and
documentation. Commits made with the assistant carry a `Co-Authored-By: Claude` trailer.

Everything is checked by the build (warnings are errors, with .NET, StyleCop and threading
analyzers), by the test suite on Linux, macOS and Windows, and by real runs on an Intel GPU.

## Building from source

See [DEVELOPMENT.md](DEVELOPMENT.md).

## License

GPL-3.0 — see [`LICENSE`](LICENSE).
