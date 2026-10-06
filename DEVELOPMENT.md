# Developing HwProbe

How to build, test, and release. How the engines, caches, and suggestions work is in `ARCHITECTURE.md`. The performance tests are called speed runs in the code. Never rename the package (build.yaml `name`, `Plugin.Name`): Jellyfin removes an updated plugin's old folder by name, so a renamed package would leave both versions loaded.

## Layout

| Path | Contents |
|---|---|
| `src/HwProbe.Core` | Probe and speed engines, report, fixtures, and the cache listing. No Jellyfin dependency. |
| `src/HwProbe.Core/Data/catalog.yaml` | Everything the plugin page lists: speed inputs, codecs, qualities, run options, test suites, and labels. Compiled in and checked by `Catalog.Parse`; served to the page by `HwProbe/Catalog`. |
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
editorconfig-checker               # .editorconfig rules on files dotnet format skips (brew install editorconfig-checker)
HWPROBE_HW_TESTS=1 dotnet test     # also real-ffmpeg and hardware tests
```

`python3 scripts/notices.py` rewrites `THIRD-PARTY-NOTICES.md` and the plugin's `libraries.json` (the Help tab's list) from the restore output after a package change; `--check` runs with the others. The checks above and `notices.py --check` run before every commit through the hook; install it once per clone with `git config core.hooksPath scripts/`. After changing a package version, run `dotnet restore --force-evaluate` and commit the lock files.

CI runs the whole suite on Linux and only `Category=Platform` tests on macOS and Windows; pushes to main run Linux only.

With podman:

```sh
scripts/container-plugin.sh    # installs the plugin into Jellyfin 12.2 and checks it through the API
scripts/container-linux.sh     # test suite on Linux, then hwprobe against jellyfin-ffmpeg (no GPU)
scripts/container-windows.sh   # win-x64 build under Wine (no GPU)
```

`container-plugin.sh` also takes `JELLYFIN_IMAGE=ghcr.io/jellyfin/jellyfin:latest` (another server version, 12.2 by default), `HWPROBE_LOCALE=de_DE.UTF-8` (run the server under another locale) and `HWPROBE_INSTALL=repository` (install through a plugin repository, as users do) or `HWPROBE_INSTALL=existing HWPROBE_BASE=http://host:port` (check a running server).

## Command-line tool

Each release has a build per platform: `hwprobe-<rid>.zip` for Windows, `hwprobe-<rid>.tar.gz` for macOS and Linux. Run it where Jellyfin runs (inside the container, for Docker); `--help` lists the options.

```sh
hwprobe                    # probe every backend
hwprobe --speed confirm    # and measure speed
hwprobe --speed confirm --speed-videos pattern,live-action --speed-outputs h264-8mbps,decode \
  --speed-backends vaapi,none --speed-option EncoderPreset=fast --speed-json speed.json
hwprobe --suite presets    # run a test suite, compare its steps, and print what it suggests
```

The first stop signal (Ctrl+C, `docker stop`) stops a speed test, stopping the measurement in progress and printing the ones it finished and why it stopped; a second ends the process at once. Throwaway containers of the official image that run `hwprobe` rather than Jellyfin need `--no-healthcheck`, or a watchdog such as autoheal restarts them as unhealthy.

## Test clips

- `scripts/make-bundled-fixtures.sh` remakes the bundled clips; put the hashes it prints in `FixtureCatalog`.
- To re-pin a film sample after Wikimedia re-encodes it, find the cluster offsets around the wanted time and hash the header plus those bytes.
- Tests never download, and `HWPROBE_NO_DOWNLOADS=1` (set in CI and `container-plugin.sh`) turns off every download, so a download-only clip like the VC-1 sample is reported as untested.

Which clips exist and where they're cached is in `ARCHITECTURE.md`.

## Diagnostics zips

A user's zip (**Download diagnostics** on the Help tab, or `--diagnostics`) is laid out like `tests/Corpus`:

- `ffmpeg/*.txt`: capability listings. Copy them to `tests/Corpus/ffmpeg/<build>/` for `ScriptedFfmpegRunner.FromCorpus`.
- `stderr/NNN-<probe>.txt`: every launch in order, with `#` lines for the arguments, environment, outcome, and result, then the full stderr. Copy one to `tests/Corpus/stderr/`, replacing the header with an `# Observed:` line naming the host and build.
- `report.json`: the report.
- `jellyfin.log` (plugin only): HwProbe's entries from Jellyfin's log files, and entries naming it such as the plugin manager loading it, with their stack traces; read when the zip is downloaded.

**Report on GitHub** (Help tab) and the link the CLI prints open the hardware-report form with the OS, versions, and working backends filled in (`IssueLink`; GitHub fills an issue form's fields from query parameters named after their ids). Zips aren't anonymised. Remove user names, host names, and home paths before committing anything from one.

## Releasing

```sh
gh workflow run release.yml --ref main -f version=1.2.3 -f notes="What changed"
```

The workflow requires a passing `CI` check on the commit, runs `container-plugin.sh` in both install modes against each Jellyfin version in its matrix, builds the plugin zip and CLI builds, publishes release `v1.2.3`, and adds it to `manifest.json` on the `manifest` branch. The notes become the plugin's changelog. `-f prerelease=true` publishes without adding it to the plugin repository. Releases are immutable, so a version can't be reused.

Every release file has a build provenance attestation, checked with `gh attestation verify <file> -R Rinn/jellyfin-plugin-hwprobe`. Builds are reproducible. To check a release, build its tag from a fresh clone and compare hashes:

```sh
GITHUB_ACTIONS=true python3 scripts/package.py --version 1.2.3 --out dist
shasum -a 256 dist/*
```
