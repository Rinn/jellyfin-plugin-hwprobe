# Developing HwProbe

The performance tests are called speed runs in the code. Never rename the package (build.yaml `name`, `Plugin.Name`): Jellyfin removes an updated plugin's old folder by name, so a renamed package would leave both versions loaded.

## Layout

| Path | Contents |
|---|---|
| `src/HwProbe.Core` | Probe and speed engines, report, fixtures, and the cache listing. No Jellyfin dependency. |
| `src/HwProbe.Core/Data/catalog.yaml` | Everything the plugin page lists: speed inputs, codecs, qualities, run options, and labels. Compiled in and checked by `Catalog.Parse`; served to the page by `HwProbe/Catalog`. |
| `src/HwProbe.Jellyfin` | Builds ffmpeg commands with Jellyfin's own `EncodingHelper`. |
| `src/HwProbe.Plugin` | The Jellyfin plugin: service, API, and the page (`Configuration/configPage.html`). |
| `src/HwProbe.Cli` | The `hwprobe` command-line tool. |
| `tests/` | Tests, with recorded ffmpeg output in `tests/Corpus`. |
| `scripts/` | Pre-commit hook, container tests, packaging. |
| `assets/` | Plugin image and icon; `make-plugin-image.sh` rebuilds `plugin.png`. Licences in `assets/NOTICE.md`. |

## Build and test

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test                        # unit and FakeFfmpeg tests
sh scripts/check-page.sh           # syntax-checks the plugin page's script
HWPROBE_HW_TESTS=1 dotnet test     # also real-ffmpeg and hardware tests
```

The first four run before every commit through the hook; install it once per clone with `git config core.hooksPath scripts/`. After changing a package version, run `dotnet restore --force-evaluate` and commit the lock files.

With podman:

```sh
scripts/container-plugin.sh    # installs the plugin into Jellyfin 12.1 and checks it through the API
scripts/container-linux.sh     # test suite on Linux, then hwprobe against jellyfin-ffmpeg (no GPU)
scripts/container-windows.sh   # win-x64 build under Wine (no GPU)
```

`container-plugin.sh` also takes `HWPROBE_LOCALE=de_DE.UTF-8` (run the server under another locale) and `HWPROBE_INSTALL=repository` (install through a plugin repository, as users do) or `HWPROBE_INSTALL=existing HWPROBE_BASE=http://host:port` (check a running server).

## Command-line tool

Each release has a build per platform: `hwprobe-<rid>.zip` for Windows, `hwprobe-<rid>.tar.gz` for macOS and Linux. Run it where Jellyfin runs (inside the container, for Docker); `--help` lists the options.

```sh
hwprobe                    # probe every backend
hwprobe --speed confirm    # and measure speed
hwprobe --speed confirm --speed-videos pattern,live-action --speed-outputs h264-8mbps,decode \
  --speed-backends vaapi,none --speed-option EncoderPreset=fast --speed-json speed.json
```

## Performance tests (speed runs)

- `SpeedEngine` measures every chosen output (a codec at a player quality, or decode only) from every chosen input on each chosen backend and software, and reports each result as it finishes.
- Arguments come from `EncodingHelper` with `ProbeCell.FullQuality`, as a real request gets them. The output size goes through Jellyfin's `ResolutionNormalizer`, as `StreamingHelpers` does.
- `SpeedCommandLine` loops the inputs and bounds each run with `-t`. `SpeedMeter` counts speed and concurrent streams, and is tested without ffmpeg.
- A hardware backend only measures what it encodes (or, for decode tests, decodes) on the GPU.
- Every full measurement (not one cut off by a timeout or the time limit) is saved under `speed-results` beside the clip cache, keyed by a SHA-256 of the measurement method (`SpeedResultCache.MeasurementVersion`, bumped when how a measurement is taken changes), ffmpeg path and version, backend, device, test, library file size and time, accuracy, repeats, and the ffmpeg command and environment EncodingHelper generates for it (`SpeedResultCache.Key`). Any setting or server change that alters the command misses; GPU driver updates aren't keyed. With **Earlier results: Reuse** (`ReuseResults`, the default on the page), a matching measurement is shown with the date it was made instead of being measured again. Entries from another measurement method or ffmpeg are deleted at the start of each run; deleting the cache deletes them all.
- **When the server starts transcoding** (`WhenTranscoding`): **Cancel** stops the run when a session starts transcoding (`ProbeService.WatchForTranscodeAsync`), keeping finished measurements and marking the report `CancelledForTranscode`; **Keep measuring** ignores transcodes. Both refuse to start during one. The choices and the default (Pause, for the page and the API alike) come from `whenTranscoding` and `defaultWhenTranscoding` in `catalog.yaml`. With **Pause**, the run starts even while a session transcodes and waits for it to end before making clips, opening devices and measuring (`SpeedPause.HoldWhileBusyAsync`, phase `Deferring`; two idle checks in a row end the wait). A transcode starting mid-measurement cancels that measurement, which kills its ffmpeg trees; once nothing transcodes it runs again from the start. The busy check is the plugin's own (`ProbeService.IsTranscoding`), polled every second.
- `SpeedAdvisor` draws the Test Results suggestions (`HwProbe/SpeedSuggestions`): the fastest hardware backend (software is never suggested over a working one), outputs where the configured backend falls behind real time, and settings compared between saved runs of this version and ffmpeg that differ in that setting alone (audio and burn-in must match too), on the same inputs, against the server's current value, and whose ffmpeg commands differ (each result keeps a hash of its command). Turning tone mapping off is never suggested; faster values that cost picture quality are marked. A faster value is suggested when every output gained more than 5%; a better-quality preset or CRF when it still keeps 1.5x real time on real video. Test videos encode faster than real video, so headroom is judged on samples and library files only, and suggestions resting on test videos alone say so. `HwProbe/ApplyMeasured` applies a suggested setting through the same history and Revert as the probe's advice.
- A library file comes from the item's media source in the plugin, or from ffprobe with `--speed-file` in the CLI, and is read from a tenth of the way in.

## Test clips and downloads

Test clips are made with the server's ffmpeg and cached per ffmpeg build. A clip that can't be made comes from a copy bundled in the plugin, or from FFmpeg's FATE sample suite, pinned by SHA-256. The cache is under Jellyfin's cache folder in `hwprobe/fixtures`: a folder per ffmpeg build, `samples` (kept across builds) and `downloads` (named by SHA-256). The Help tab lists it through `HwProbe/Cache/Contents` (`FixtureCacheContents`, which names each file from the catalogs). Before each probe or performance test builds clips, `FixtureCacheContents.Prune` deletes other ffmpeg builds' folders, files this version doesn't use, and leftovers of interrupted writes.

- Bundled (no public sample exists): HEVC RExt 4:4:4 10-bit, 4:2:2 12-bit and 4:4:4 12-bit, AV1 10-bit, and H.264 with frequent key frames. `scripts/make-bundled-fixtures.sh` remakes them; put the hashes it prints in `FixtureCatalog`.
- Always downloaded: `vc1/SA00050.vc1` (no free VC-1 encoder exists).
- Downloaded only when generation fails: `h264-conformance/BA1_Sony_D.jsv`, `h264-conformance/CVFI1_Sony_D.jsv`, `hevc-conformance/WP_A_Toshiba_3.bit`, `hevc-conformance/WP_A_MAIN10_Toshiba_3.bit`, `hevc-conformance/Main_422_10_A_RExt_Sony_1.bin`, `vp9-test-vectors/vp90-2-09-lf_deltas.webm`, `vp9-test-vectors/vp92-2-20-10bit-yuv420.webm`, `vp8-test-vectors-r1/vp80-00-comprehensive-001.ivf`, `av1-test-vectors/av1-1-b8-02-allintra.ivf`.
- For PGS subtitle burn-in: `sub/pgs_sub.sup` (ffmpeg has no PGS encoder).

Film samples for speed runs are downloaded only when chosen: a pinned piece of each Wikimedia Commons file (its WebM header plus whole clusters, two range requests), checked by SHA-256, used as downloaded, and cached apart from the ffmpeg build. The 1080p ones are VP9 with Opus audio: Tears of Steel and Sintel (CC BY 3.0), Sol Levante (CC BY 4.0). The 4K one is Sol Levante's HDR10 AV1 copy (Professional profile, 4:4:4 12-bit, which GPUs don't decode). Requests carry a descriptive User-Agent, as Wikimedia asks. To re-pin after Wikimedia re-encodes a file, find the cluster offsets around the wanted time and hash the header plus those bytes.

Tests never download, and `HWPROBE_NO_DOWNLOADS=1` (set in CI and `container-plugin.sh`) turns off every download, so a download-only clip like the VC-1 sample is reported as untested.

## Diagnostics zips

A user's zip (**Download diagnostics** on the Help tab, or `--diagnostics`) is laid out like `tests/Corpus`:

- `ffmpeg/*.txt`: capability listings. Copy them to `tests/Corpus/ffmpeg/<build>/` for `ScriptedFfmpegRunner.FromCorpus`.
- `stderr/NNN-<probe>.txt`: every launch in order, with `#` lines for the arguments, environment, outcome, and result, then the full stderr. Copy one to `tests/Corpus/stderr/`, replacing the header with an `# Observed:` line naming the host and build.
- `report.json`: the report.

Zips aren't anonymised. Remove user names, host names, and home paths before committing anything from one.

## Releasing

```sh
gh workflow run release.yml --ref main -f version=1.2.3 -f notes="What changed"
```

The workflow builds the plugin zip and CLI builds, publishes release `v1.2.3`, and adds it to `manifest.json` on the `manifest` branch. The notes become the plugin's changelog. `-f prerelease=true` publishes without adding it to the plugin repository. Releases are immutable, so a version can't be reused.

Builds are reproducible. To check a release, build its tag from a fresh clone and compare hashes:

```sh
GITHUB_ACTIONS=true python3 scripts/package.py --version 1.2.3 --out dist
shasum -a 256 dist/*
```
