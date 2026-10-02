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

`SpeedEngine` measures the tests in `SpeedCatalog` on each viable backend and software. Arguments come from `EncodingHelper` with `ProbeCell.FullQuality` (quality, audio and input arguments, as a real request gets them); `SpeedCommandLine` loops the inputs and bounds the run with `-t`. `SpeedMeter` does the counting and is tested without ffmpeg. A real file (`SpeedFile`) comes from the library item's media source in the plugin and from ffprobe (`FfprobeFile`) in the CLI; `SpeedFileTests` lists its tests, starting a tenth of the way in. Clips are generated with the server's ffmpeg and cached with the other fixtures; the PGS sample is downloaded from FFmpeg's FATE suite.

```sh
hwprobe --speed confirm --speed-tests 1080p-h264,decode-hevc --speed-compare preset,vbr --speed-json speed.json
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
- Only for the PGS subtitle speed test: `sub/pgs_sub.sup`. ffmpeg has no PGS encoder.

Speed samples, only when chosen, each a 30-second segment read with HTTP range requests and encoded the same way (1080p 24 fps H.264, CRF 18 capped at 10 Mbps, stereo AAC). They can't be pinned by hash, since the encode depends on the ffmpeg version.

- Wikimedia Commons 1080p VP9 transcodes: Tears of Steel (CC BY 3.0), Sintel (CC BY 3.0), Sol Levante (CC BY 4.0). Requests carry a descriptive User-Agent, as Wikimedia asks.
- Netflix Open Content (`s3.amazonaws.com/download.opencontent.netflix.com`, CC BY 4.0): the Sol Levante HDR10 ProRes master and its 5.1 IMF audio, encoded to 4K HEVC 10-bit HDR10. About 4.3 GB per segment.
  - `av1-test-vectors/av1-1-b8-02-allintra.ivf`
