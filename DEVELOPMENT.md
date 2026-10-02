# Developing HwProbe

How to build, test and release HwProbe. For installing and using it, see the [README](README.md).

## Layout

| Path | Contents |
|---|---|
| `src/HwProbe.Core` | The probe engine: devices, test clips, probes, verdicts, the report. No Jellyfin dependency. |
| `src/HwProbe.Jellyfin` | Builds every ffmpeg command with Jellyfin's own `EncodingHelper`, so probes run what the server would run. |
| `src/HwProbe.Cli` | The `hwprobe` command-line tool. |
| `src/HwProbe.Plugin` | The Jellyfin plugin: settings page, scheduled task, admin API. |
| `tests/` | Unit, fake-ffmpeg, real-ffmpeg and hardware tests. Recorded ffmpeg output is in `tests/Corpus`. |
| `build.yaml` | Plugin metadata (name, GUID, target ABI) for the plugin repository manifest. |

## Command-line tool

`src/HwProbe.Cli` runs the same probe without the plugin.

- Releases attach builds for linux-x64, linux-arm64, osx-arm64 and win-x64.
- Run it where Jellyfin runs (inside the container, for Docker), so it tests the same ffmpeg and devices.
- `dotnet run --project src/HwProbe.Cli -- --help` lists the options. `--format summary` prints a short report to share.

## Building

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                        # Unit + FakeFfmpeg tests
HWPROBE_HW_TESTS=1 dotnet test     # also RealFfmpeg + Hardware tests
```

- Warnings are errors. The build runs the .NET, StyleCop and threading analyzers.
- The SDK is pinned exactly in `global.json`.
- NuGet restores are pinned by the committed `packages.lock.json` files, and CI enforces them. After changing a package version or the target platforms, run `dotnet restore --force-evaluate` and commit the lock files.

## Reproducible builds

The same commit, built with the pinned SDK on Linux, produces byte-identical DLLs and archives. An arm64 Linux container matched the x64 GitHub runner exactly. macOS builds haven't been compared.

- `Deterministic` is on.
- On GitHub Actions, `ContinuousIntegrationBuild` maps the repo root to `/_/`, so no checkout path ends up in a binary.
- `scripts/package.py` writes archives with sorted entries, fixed permissions and the commit time (or `SOURCE_DATE_EPOCH`). Gzips store no name or time.

To check a release, build its tag from a clone whose `origin` is the GitHub URL, then compare hashes with the release assets. SourceLink records the remote URL, so a clone of a local path gives different bytes.

```sh
git clone https://github.com/Rinn/jellyfin-plugin-hwprobe && cd jellyfin-plugin-hwprobe && git checkout v1.2.3
GITHUB_ACTIONS=true python3 scripts/package.py --version 1.2.3 --out dist
shasum -a 256 dist/*
```

## Pre-commit hook

The three checks above must pass before every commit. Install the hook once per clone:

```sh
git config core.hooksPath scripts/
```

## Testing other platforms

With podman installed:

```sh
scripts/container-linux.sh     # test suite on Linux, then hwprobe against jellyfin-ffmpeg (no GPU)
scripts/container-windows.sh   # win-x64 build under Wine against Windows jellyfin-ffmpeg (no GPU)
scripts/container-plugin.sh    # installs the plugin into Jellyfin 12.1 and probes through its API
HWPROBE_INSTALL=repository scripts/container-plugin.sh   # same, installing from a plugin repository
HWPROBE_INSTALL=existing HWPROBE_BASE=http://host:18096 scripts/container-plugin.sh
                               # checks a server you started yourself, e.g. one with a GPU passed in
```

- Extra arguments are passed to hwprobe.
- On Apple Silicon, the Windows script needs Rosetta for x86_64 containers: add `[machine]` / `rosetta = true` to `~/.config/containers/containers.conf` and restart the podman machine.
- On Apple Silicon, an amd64 Jellyfin image runs under emulation and can crash at start-up. `podman pull --platform linux/arm64 <image>` fixes it.

## Continuous integration

`.github/workflows/ci.yml` runs on pushes to `main` and on pull requests. It:

- builds and tests on Linux, macOS and Windows
- runs the real-ffmpeg tests and a CLI run against jellyfin-ffmpeg's portable builds on all three
- installs the plugin into Jellyfin 12.1, by copying it and from a plugin repository, and probes through its API
- packages with `scripts/package.py` and uploads the archives a release would ship

## Releasing

`.github/workflows/release.yml` runs when a GitHub release is published.

1. Create a release with a tag like `v1.2.3` on `main`:

   ```sh
   gh release create v1.2.3 --target main --title v1.2.3 --notes "What changed"
   ```

   - The tag is the only place the version is set. The workflow stamps it on every assembly with `-p:Version`; `Directory.Build.props` holds the placeholder `0.0.0`.
   - The release notes become the plugin's changelog in Jellyfin.
2. The workflow checks the tag format, builds and tests, and attaches:
   - `hwprobe-plugin_1.2.3.0.zip`
   - `hwprobe-linux-x64.gz`, `hwprobe-linux-arm64.gz`, `hwprobe-osx-arm64.gz`, `hwprobe-win-x64.zip`
3. For a full release (not a pre-release), it adds the version to `manifest.json` on the `manifest` branch, keeping earlier versions. That file is the repository URL users add in Jellyfin. The workflow never pushes to `main`.

`scripts/manifest.py` writes the manifest.

- Its fields follow Jellyfin's `PackageInfo` and `VersionInfo`.
- The checksum is MD5, which Jellyfin checks before installing.
- Jellyfin writes the plugin's `meta.json` from the manifest, so the zip holds only the DLLs.

## Network access

HwProbe itself only downloads test clips from FFmpeg's FATE sample suite. Each is pinned by SHA-256 and cached under `<fixtures>/downloads`.

- Always, on the first run: `vc1/SA00050.vc1` (124 KB). No free VC-1 encoder exists, so this clip is never generated.
- Only when a clip can't be generated (its encoder is missing or crashes):
  - `h264-conformance/BA1_Sony_D.jsv`
  - `h264-conformance/CVFI1_Sony_D.jsv`
  - `hevc-conformance/WP_A_Toshiba_3.bit`
  - `hevc-conformance/WP_A_MAIN10_Toshiba_3.bit`
  - `hevc-conformance/Main_422_10_A_RExt_Sony_1.bin` (4 MB)
  - `vp9-test-vectors/vp90-2-09-lf_deltas.webm`
  - `vp9-test-vectors/vp92-2-20-10bit-yuv420.webm`
  - `vp8-test-vectors-r1/vp80-00-comprehensive-001.ivf`
  - `av1-test-vectors/av1-1-b8-02-allintra.ivf` (1.5 MB)
- HDR10, HEVC RExt 12-bit and AV1 10-bit have no suitable sample. They are generate-only.
- Offline, a clip that can't be made is reported as `Untested` or `Skipped` with the reason. Everything else still runs.

The tests and scripts need more:

| What | Network access |
|---|---|
| `dotnet build` / `dotnet test` | NuGet restore. Unit and FakeFfmpeg tests make no other requests; downloads go through a scripted fake. |
| `HWPROBE_HW_TESTS=1 dotnet test` | The VC-1 clip, unless `HWPROBE_TEST_DOWNLOADS` points at a folder that already has it. |
| `scripts/container-linux.sh` | Pulls `mcr.microsoft.com/dotnet/sdk:10.0` and `docker.io/jellyfin/jellyfin:12.1`. NuGet restore inside the container. |
| `scripts/container-plugin.sh` | Pulls `docker.io/jellyfin/jellyfin:12.1`, and `docker.io/library/python:3-alpine` to serve the test repository. The script only talks to that server on `127.0.0.1`. The server starts with Jellyfin's defaults, which can make their own requests. |
| `scripts/container-windows.sh` | Pulls `docker.io/library/ubuntu:24.04` and `mcr.microsoft.com/dotnet/sdk:10.0`. Installs Wine with `apt-get`. Downloads Windows jellyfin-ffmpeg from the [jellyfin/jellyfin-ffmpeg releases](https://github.com/jellyfin/jellyfin-ffmpeg/releases) with `gh`. |
| GitHub Actions | Everything above, plus the portable jellyfin-ffmpeg builds for Linux, macOS and Windows. Downloads, NuGet packages and test clips are cached between runs. |
