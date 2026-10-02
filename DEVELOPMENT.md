# Developing HwProbe

## Layout

| Path | Contents |
|---|---|
| `src/HwProbe.Core` | Probe engine. No Jellyfin dependency. |
| `src/HwProbe.Jellyfin` | Builds ffmpeg commands with Jellyfin's own `EncodingHelper`. |
| `src/HwProbe.Cli` | The `hwprobe` command-line tool. |
| `src/HwProbe.Plugin` | The Jellyfin plugin. |
| `tests/` | Tests, and recorded ffmpeg output in `tests/Corpus`. |
| `build.yaml` | Plugin metadata for the repository manifest. |
| `assets/` | Sidebar icon source and the catalog image. `assets/make-plugin-image.sh` rebuilds `plugin.png` with headless Chrome. Licences are in `assets/NOTICE.md`. |

## Building

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                        # Unit + FakeFfmpeg tests
HWPROBE_HW_TESTS=1 dotnet test     # also RealFfmpeg + Hardware tests
```

- All three checks must pass before every commit. Install the hook once per clone: `git config core.hooksPath scripts/`
- After changing a package version, run `dotnet restore --force-evaluate` and commit the lock files.

## Testing with containers

With podman installed:

```sh
scripts/container-linux.sh     # test suite on Linux, then hwprobe against jellyfin-ffmpeg (no GPU)
scripts/container-windows.sh   # win-x64 build under Wine (no GPU)
scripts/container-plugin.sh    # installs the plugin into Jellyfin 12.1 and probes through its API
HWPROBE_INSTALL=repository scripts/container-plugin.sh   # same, installing from a plugin repository
HWPROBE_INSTALL=existing HWPROBE_BASE=http://host:18096 scripts/container-plugin.sh   # an existing server
```

## Command-line tool

`src/HwProbe.Cli` runs the same probe without the plugin. Run it where Jellyfin runs (inside the container, for Docker). `--help` lists the options.

## Diagnostics zips

A user's zip (plugin **Download diagnostics**, or `--diagnostics`) is laid out like `tests/Corpus`:

- `ffmpeg/*.txt`: the capability listings. Copy them to `tests/Corpus/ffmpeg/<build>/` for `ScriptedFfmpegRunner.FromCorpus`.
- `stderr/NNN-<probe>.txt`: every launch in order, with `#` lines for the arguments, environment, probe outcome and result, then the complete stderr. Copy one to `tests/Corpus/stderr/`, replacing the header with an `# Observed:` line naming the host and build.
- `report.json`: the report.

`DiagnosticsScrubber` removes the home and cache directories, the user name and the host name. Check a zip before committing anything from it.

## Releasing

```sh
gh workflow run release.yml --ref main -f version=1.2.3 -f notes="What changed"
```

- `release.yml` builds the plugin zip and CLI builds, publishes them as release `v1.2.3`, and adds the version to `manifest.json` on the `manifest` branch.
- The version comes only from this input. The notes become the plugin's changelog.
- Add `-f prerelease=true` for a prerelease, which isn't added to the plugin repository.
- Releases are immutable, so a version can't be reused once published.

Builds are reproducible. To check a release, build its tag from a clone of the GitHub URL and compare hashes:

```sh
GITHUB_ACTIONS=true python3 scripts/package.py --version 1.2.3 --out dist
shasum -a 256 dist/*
```

## Test clips

HwProbe makes its test clips with the server's ffmpeg. When a clip can't be made, it uses a copy bundled in the plugin, or downloads a sample.

- Bundled, for clips with no public sample: HEVC RExt 4:4:4 10-bit, 4:2:2 12-bit and 4:4:4 12-bit, AV1 10-bit, and H.264 with frequent key frames. `scripts/make-bundled-fixtures.sh` remakes them; put the hashes it prints in `FixtureCatalog`.

## Network access

HwProbe only downloads test clips from FFmpeg's FATE sample suite, each pinned by SHA-256 and cached.

- Always: `vc1/SA00050.vc1`. No free VC-1 encoder exists.
- Only when a clip can't be generated:
  - `h264-conformance/BA1_Sony_D.jsv`
  - `h264-conformance/CVFI1_Sony_D.jsv`
  - `hevc-conformance/WP_A_Toshiba_3.bit`
  - `hevc-conformance/WP_A_MAIN10_Toshiba_3.bit`
  - `hevc-conformance/Main_422_10_A_RExt_Sony_1.bin`
  - `vp9-test-vectors/vp90-2-09-lf_deltas.webm`
  - `vp9-test-vectors/vp92-2-20-10bit-yuv420.webm`
  - `vp8-test-vectors-r1/vp80-00-comprehensive-001.ivf`
  - `av1-test-vectors/av1-1-b8-02-allintra.ivf`
