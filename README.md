# jellyfin-plugin-hwprobe

Device-verified hardware-transcode detection for Jellyfin.

Jellyfin's hardware-acceleration dropdown offers the same eight choices on every server, whatever
ffmpeg was built with and whatever hardware the machine has. Picking one tells you nothing about
whether it works. HwProbe runs tiny real transcodes and reports which backends, codecs and
filter-pipeline tiers genuinely work, with a remedy for each failure.

**Status:** early development. Milestone 1 (standalone CLI) and Milestone 2 (read-only plugin) are
implemented. VAAPI and QSV have been validated on one Intel GPU (Apollo Lake, Linux); other
backends have not been run on real hardware.

## Plugin

Download `hwprobe-plugin.zip` from the [releases page](https://github.com/Rinn/jellyfin-plugin-hwprobe/releases)
(or build `src/HwProbe.Plugin`) and copy the `Jellyfin.Plugin.HwProbe*.dll` files into
`<config>/plugins/HwProbe_0.1.0.0/`. The plugin adds a configuration page with a **Run probe**
button, a *Probe hardware transcoding* scheduled task (no default trigger), and admin-only
endpoints: `GET /HwProbe/Report`, `GET /HwProbe/Status`, `POST /HwProbe/Run`. It refuses to probe
while any session is transcoding. It never changes Jellyfin's settings.

## Network access

hwprobe doesn't send data anywhere: no telemetry, no reports uploaded, nothing about your server
leaves the machine. It makes one outbound request:

| When | Request | Why |
|---|---|---|
| CLI or plugin run, first time only | `GET https://fate-suite.ffmpeg.org/vc1/SA00050.vc1` (124 KB) | No free VC-1 encoder exists, so the VC-1 test clip is downloaded from FFmpeg's public test-sample suite instead of generated. It's checked against a pinned SHA-256 and cached under `<fixtures>/downloads`, so later runs reuse it. Offline, VC-1 is reported as `Untested` and everything else still runs. |

All other test clips are generated locally by the ffmpeg under test.

The tests and scripts have their own network needs:

| What | Network access |
|---|---|
| `dotnet build` / `dotnet test` | NuGet package restore. Unit and FakeFfmpeg tests make no other requests; downloads go through a scripted fake. |
| `HWPROBE_HW_TESTS=1 dotnet test` | The VC-1 download above, unless `HWPROBE_TEST_DOWNLOADS` points at a folder that already has it. |
| `scripts/container-linux.sh` | Pulls `mcr.microsoft.com/dotnet/sdk:10.0` and `docker.io/jellyfin/jellyfin:12.1`; NuGet restore inside the container. |
| `scripts/container-plugin.sh` | Pulls `docker.io/jellyfin/jellyfin:12.1`. The script only talks to that server on `127.0.0.1`; the server itself starts with Jellyfin's defaults, which can make its own outbound requests. |
| `scripts/container-windows.sh` | Pulls `docker.io/library/ubuntu:24.04` and `mcr.microsoft.com/dotnet/sdk:10.0`, installs Wine with `apt-get`, and downloads Windows jellyfin-ffmpeg from the [jellyfin/jellyfin-ffmpeg releases](https://github.com/jellyfin/jellyfin-ffmpeg/releases) with `gh`. |
| GitHub Actions CI | Everything above, plus the portable jellyfin-ffmpeg builds for Linux, macOS and Windows. Downloads, NuGet packages and generated fixtures are cached between runs. |

## Building

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                        # Unit + FakeFfmpeg tests
HWPROBE_HW_TESTS=1 dotnet test     # also RealFfmpeg + Hardware tests
```

## Testing other platforms

With podman installed:

```sh
scripts/container-linux.sh     # test suite on Linux, then hwprobe against jellyfin-ffmpeg (no GPU)
scripts/container-windows.sh   # win-x64 build under Wine against Windows jellyfin-ffmpeg (no GPU)
scripts/container-plugin.sh    # installs the plugin into a Jellyfin 12.1 server and probes through its API
```

Extra arguments are passed to hwprobe. On Apple Silicon the Windows script needs Rosetta for
x86_64 containers: add `[machine]` / `rosetta = true` to `~/.config/containers/containers.conf`
and restart the podman machine.

## Continuous integration

`.github/workflows/ci.yml` builds and tests on Linux, macOS and Windows, runs the real-ffmpeg tests
and a CLI run against jellyfin-ffmpeg's portable builds on all three, runs the plugin end-to-end
script, and uploads the plugin zip and CLI builds as artifacts.

## Pre-commit hook

All three checks above must pass before every commit. Install the checked-in hook once per clone:

```sh
git config core.hooksPath scripts/
```

## AI disclosure

This project was written with substantial help from an AI coding assistant: Claude, by Anthropic,
used through Claude Code. The repository owner directed the work, made the design decisions and
reviewed the results along the way; the assistant wrote most of the code, tests, scripts and
documentation. Commits made with the assistant carry a `Co-Authored-By: Claude` trailer.

Everything is checked by the build (warnings are errors, with .NET, StyleCop and threading
analyzers), by the test suite on Linux, macOS and Windows, and by the real runs described in the
commit messages. As the status above says, the hardware conclusions haven't been validated on real
Linux or Windows GPUs yet.

## License

GPL-3.0 — see [`LICENSE`](LICENSE).
