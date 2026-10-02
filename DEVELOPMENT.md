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

## Speed runs

`SpeedEngine` measures every chosen output from every chosen video in `SpeedCatalog` (a test is a video|output pair) on each viable backend and software, reporting each result as it finishes. Arguments come from `EncodingHelper` with `ProbeCell.FullQuality` (quality, audio and input arguments, as a real request gets them); `SpeedCommandLine` loops the inputs and bounds the run with `-t`. `SpeedMeter` does the counting and is tested without ffmpeg. A library file (`SpeedFile`) comes from the item's media source in the plugin and from ffprobe (`FfprobeFile`) in the CLI, as the `library` video, read from a tenth of the way in. Clips are generated with the server's ffmpeg and cached with the other fixtures; the PGS sample is downloaded from FFmpeg's FATE suite.

```sh
hwprobe --speed confirm --speed-videos pattern,live-action --speed-outputs 720p-h264,decode --speed-compare preset,vbr --speed-json speed.json
```

## Diagnostics zips

A user's zip (plugin **Download diagnostics**, or `--diagnostics`) is laid out like `tests/Corpus`:

- `ffmpeg/*.txt`: the capability listings. Copy them to `tests/Corpus/ffmpeg/<build>/` for `ScriptedFfmpegRunner.FromCorpus`.
- `stderr/NNN-<probe>.txt`: every launch in order (`NNN-launch.txt` for launches that aren't probes, such as making test clips), with `#` lines for the arguments, environment, probe outcome and result, then the complete stderr. Copy one to `tests/Corpus/stderr/`, replacing the header with an `# Observed:` line naming the host and build.
- `report.json`: the report.

Nothing is removed from a zip. Take out user names, host names and home paths before committing anything from one.

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
- Only for the PGS subtitle speed test: `sub/pgs_sub.sup`. ffmpeg has no PGS encoder.

Speed samples, only when chosen: a pinned piece of each file (its WebM header and about 30 seconds of whole clusters, two range requests), checked by SHA-256 before ffmpeg reads it (`FixturePiece`). The 1080p ones are encoded the same way (24 fps H.264, CRF 18 capped at 10 Mbps, stereo AAC); the 4K one to HEVC 10-bit HDR10 at CRF 18. The encode is cached and the piece deleted. Tests never download them, and `HWPROBE_NO_DOWNLOADS=1` (set in CI and `scripts/container-plugin.sh`) turns off every download, so a fixture that only downloads, like the VC-1 sample, is reported as untested. To re-pin after Wikimedia re-encodes a file, find the cluster offsets around the wanted time and hash the header plus those bytes.

- Wikimedia Commons 1080p VP9 transcodes: Tears of Steel (CC BY 3.0), Sintel (CC BY 3.0), Sol Levante (CC BY 4.0).
- Wikimedia Commons' 4K HDR10 AV1 copy of Sol Levante, encoded to 4K HEVC 10-bit HDR10 (about 108 MB).
- Requests carry a descriptive User-Agent, as Wikimedia asks. Samples are cached apart from the ffmpeg build, so an ffmpeg update doesn't fetch them again.
