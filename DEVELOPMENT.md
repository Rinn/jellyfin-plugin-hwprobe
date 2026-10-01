# Developing hwprobe

How to build, test and release hwprobe. For installing and using it, see the [README](README.md).

## Layout

| Path | What it is |
|---|---|
| `src/HwProbe.Core` | The probe engine: device enumeration, fixtures, probes, verdicts, the report. No Jellyfin dependency. |
| `src/HwProbe.Jellyfin` | Generates every ffmpeg argument by calling Jellyfin's own `EncodingHelper`, so probes run what the server would run. |
| `src/HwProbe.Cli` | The `hwprobe` command-line tool. |
| `src/HwProbe.Plugin` | The Jellyfin plugin: configuration page, scheduled task, admin API. |
| `tests/` | Unit, fake-ffmpeg, real-ffmpeg and hardware tests, plus recorded ffmpeg output in `tests/Corpus`. |
| `build.yaml` | Plugin metadata (name, GUID, version, target ABI) used to build the plugin repository manifest. |

## Building

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                        # Unit + FakeFfmpeg tests
HWPROBE_HW_TESTS=1 dotnet test     # also RealFfmpeg + Hardware tests
```

Warnings are errors. The build runs the .NET, StyleCop and threading analyzers.

## Pre-commit hook

All three checks above must pass before every commit. Install the checked-in hook once per clone:

```sh
git config core.hooksPath scripts/
```

## Testing other platforms

With podman installed:

```sh
scripts/container-linux.sh     # test suite on Linux, then hwprobe against jellyfin-ffmpeg (no GPU)
scripts/container-windows.sh   # win-x64 build under Wine against Windows jellyfin-ffmpeg (no GPU)
scripts/container-plugin.sh    # installs the plugin into a Jellyfin 12.1 server and probes through its API
HWPROBE_INSTALL=repository scripts/container-plugin.sh   # same, installing from a plugin repository
HWPROBE_INSTALL=existing HWPROBE_BASE=http://host:18096 scripts/container-plugin.sh
                               # checks a server you started yourself, e.g. one with a GPU passed in
```

Extra arguments are passed to hwprobe. On Apple Silicon the Windows script needs Rosetta for x86_64 containers: add `[machine]` / `rosetta = true` to `~/.config/containers/containers.conf` and restart the podman machine. If a script pulls an amd64 image on Apple Silicon, Jellyfin runs under emulation and can crash at start-up; `podman pull --platform linux/arm64 <image>` fixes it.

## Continuous integration

`.github/workflows/ci.yml` runs on pushes to `main` and on pull requests:

- builds and tests on Linux, macOS and Windows;
- runs the real-ffmpeg tests and a CLI run against jellyfin-ffmpeg's portable builds on all three;
- installs the plugin into Jellyfin 12.1, both by copying it and from a plugin repository, and probes through its API;
- uploads the plugin zip and CLI builds as artifacts.

## Releasing

`.github/workflows/release.yml` runs when a GitHub release is published.

1. Set the version in `build.yaml` (`version: "1.2.3.0"`) and in `src/HwProbe.Plugin/HwProbe.Plugin.csproj` (`<Version>1.2.3</Version>`), and merge that to `main`.
2. Create a release with tag `v1.2.3`. Its description becomes the plugin's changelog in Jellyfin. For example: `gh release create v1.2.3 --target main --title v1.2.3 --notes "What changed"`.
3. The workflow checks the tag matches both versions, builds and tests, and attaches to the release:
   - `hwprobe-plugin_1.2.3.0.zip`
   - `hwprobe-linux-x64.gz`, `hwprobe-linux-arm64.gz`, `hwprobe-osx-arm64.gz`, `hwprobe-win-x64.zip`
4. For a full release (not a pre-release), it adds the version to `manifest.json` on the `manifest` branch, keeping earlier versions. That file is the repository URL users add in Jellyfin. The workflow never pushes to `main`.

`scripts/manifest.py` writes the manifest. Its fields follow Jellyfin's `PackageInfo` and `VersionInfo`. The checksum is MD5, which Jellyfin checks before installing. Jellyfin writes the plugin's `meta.json` itself from the manifest, so the zip holds only the DLLs.

## Network access in development

The tool itself makes one request: `GET https://fate-suite.ffmpeg.org/vc1/SA00050.vc1` (124 KB), on the first run only. No free VC-1 encoder exists, so that clip can't be generated like the others. It is checked against a pinned SHA-256 and cached under `<fixtures>/downloads`. Offline, VC-1 is reported as `Untested` and everything else still runs.

The tests and scripts need more:

| What | Network access |
|---|---|
| `dotnet build` / `dotnet test` | NuGet package restore. Unit and FakeFfmpeg tests make no other requests; downloads go through a scripted fake. |
| `HWPROBE_HW_TESTS=1 dotnet test` | The VC-1 sample download, unless `HWPROBE_TEST_DOWNLOADS` points at a folder that already has it. |
| `scripts/container-linux.sh` | Pulls `mcr.microsoft.com/dotnet/sdk:10.0` and `docker.io/jellyfin/jellyfin:12.1`; NuGet restore inside the container. |
| `scripts/container-plugin.sh` | Pulls `docker.io/jellyfin/jellyfin:12.1`, and `docker.io/library/python:3-alpine` to serve the test repository. The script only talks to that server on `127.0.0.1`; the server itself starts with Jellyfin's defaults, which can make its own outbound requests. |
| `scripts/container-windows.sh` | Pulls `docker.io/library/ubuntu:24.04` and `mcr.microsoft.com/dotnet/sdk:10.0`, installs Wine with `apt-get`, and downloads Windows jellyfin-ffmpeg from the [jellyfin/jellyfin-ffmpeg releases](https://github.com/jellyfin/jellyfin-ffmpeg/releases) with `gh`. |
| GitHub Actions | Everything above, plus the portable jellyfin-ffmpeg builds for Linux, macOS and Windows. Downloads, NuGet packages and generated fixtures are cached between runs. |
