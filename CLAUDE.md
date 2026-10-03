# CLAUDE.md

Operating manual for this repo: what it is, commands, conventions, the traps that cost real time, and the work still to do. End-user docs are in `README.md`; build, release and test-host details are in `DEVELOPMENT.md`.

## What this is

A device-verified hardware-transcode detector for Jellyfin, shipped as a CLI (`src/HwProbe.Cli`) and a plugin (`src/HwProbe.Plugin`) over a shared engine (`src/HwProbe.Core`, with Jellyfin's own `EncodingHelper` wired in by `src/HwProbe.Jellyfin`). Jellyfin's hardware acceleration dropdown is a fixed list in jellyfin-web, so it offers backends that fail every job; HwProbe runs small real transcodes and reports what works, with a fix for each failure. The plugin page also applies the advised settings (with history and Revert) and measures transcode speed per backend (the Speed tab, driven by `src/HwProbe.Core/Data/catalog.yaml`).

Public at https://github.com/Rinn/jellyfin-plugin-hwprobe (`origin`). Latest release v0.9.0; the speed tier is merged (PR #32) but not released.

## Commands

```sh
dotnet build -warnaserror          # must be clean
dotnet format --verify-no-changes  # formatting/style, non-mutating
dotnet test                        # Unit + FakeFfmpeg traits only
sh scripts/check-page.sh           # syntax-checks the plugin page's script

HWPROBE_HW_TESTS=1 dotnet test     # adds RealFfmpeg + Hardware traits
sh scripts/container-plugin.sh     # installs the plugin in Jellyfin 12.1 (podman) and checks it through the API
```

The first four must pass before every commit; `scripts/pre-commit` enforces them. A test run with zero tests exits 8 and fails the gate; don't hide it with `--ignore-exit-code`.

Releases: run `release.yml` by hand (see `DEVELOPMENT.md`). Releases are immutable, so a used version can never be reused. The version comes only from the release tag; `Directory.Build.props` holds `0.0.0`.

## Environment

- Dev machine: macOS. Only VideoToolbox is exercisable here. ffmpeg at `/opt/homebrew/bin/ffmpeg` is not the binary the server uses; always report the resolved path and flag non-Jellyfin builds.
- Real hardware: an Intel NAS (QSV over VAAPI) and an RTX 5080 Windows PC; see the NAS and Windows testing memories.
- No AMD, AMF or Rockchip hardware: ship those as argument-string tests plus recorded logs and report them `Untested`. Never imply a verdict that wasn't tested.
- Pinned argument strings: Linux in `LinuxDriftTests` (from recorded jellyfin-ffmpeg builds in `tests/Corpus/ffmpeg/`), Windows in `WindowsDriftTests` (blessed on the PC with `HWPROBE_BLESS`). Each runs only on its OS; re-bless when upstream changes.

## Consulting upstream Jellyfin source

```sh
gh api 'repos/jellyfin/jellyfin/contents/<path>?ref=v12.1' --jq '.content' | base64 -d > /tmp/x.cs
```

Pull files with `gh` and grep locally. WebFetch truncates `EncodingHelper.cs` (8000+ lines) and summarises the part it saw as if it were the whole file. Quote the URL in zsh. jellyfin-web works the same way (`repos/jellyfin/jellyfin-web`, e.g. `src/apps/dashboard/routes/playback/transcoding.tsx`, `src/strings/en-us.json`).

| Path | Landmarks (grep by symbol; line numbers drift) |
|---|---|
| `MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs` | `IsOpenclFullSupported` · device args · `GetInputVideoHwaccelArgs` · `GetVideoProcessingFilterParam` and the tier dispatch · decoder args · `GetNumberOfThreads` · `ScaleBitrate` |
| `MediaBrowser.MediaEncoding/Encoder/EncoderValidator.cs` | capability lists, parsing regexes, `CheckVaapiDeviceByDriverName` |
| `MediaBrowser.Model/Configuration/EncodingOptions.cs` | every setting the plugin reads or writes |
| `Jellyfin.Api/Helpers/StreamingHelpers.cs`, `MediaBrowser.Model/Dlna/ResolutionNormalizer.cs` | how the server picks output size from bitrate |

## House rules

- **Verify against source; never describe upstream behaviour from memory.** The owner checks provenance and has caught real errors. Anything claimed about Jellyfin should trace to a file and symbol; if it rests on a summary, say so.
- **Don't hardcode ffmpeg arguments.** Generate them through `EncodingHelper`. The pinned drift strings detect upstream changes; they aren't the source of truth. The one exception is the bare device-open probe, which upstream never emits.
- **Never auto-apply settings and never restart the server.** Apply writes only advised `EncodingOptions` values through `SaveConfiguration`, with history and Revert.

## Code style

- **Docstrings on every member**, private included: `<summary>`, a `<param>` per parameter, `<returns>` for non-void, one line each where possible. Enforced by StyleCop SA1600 (with `documentPrivateElements`), SA1611 and SA1615. Never suppress these.
- **One type per file**, named after the type, small enums and records included (SA1402 + SA1649, with `topLevelTypes` widened in `stylecop.json`).
- **Async entry points**: `static async Task<int> Main` in an explicit `Program` class. No `Thread.Sleep` or blocking I/O; no `ConfigureAwait(false)` (CA2007 is off, as upstream has it).
- **Comments only where the code isn't self-documenting**, terse, explaining why. Upstream constraints and ffmpeg quirks are the usual reason; cite the source.
- **Gate platform- and hardware-bound tests declaratively** with `[Fact(Skip = "Requires …", SkipUnless = nameof(TestEnvironment.X), SkipType = typeof(TestEnvironment))]` from `tests/TestSupport/TestEnvironment.cs`, never an `if (OperatingSystem.Is…)` inside a test. Real ffmpeg comes from `TestEnvironment.RealFfmpeg`.
- **Plugin page**: only jellyfin-web's own classes and `--jf-palette` theme variables, styled like the dashboard's pages; no `${` anywhere in `configPage.html`.

## Zero warnings

`Directory.Build.props` promotes every warning to an error (empty `WarningsAsErrors`), with `AnalysisMode=AllEnabledByDefault`, `EnforceCodeStyleInBuild` and `GenerateDocumentationFile`. `.editorconfig` rules are `:warning` or `:error`, never `:suggestion` or `:silent`. Suppressions are narrow `[SuppressMessage(..., Justification = "…")]` at the member; a rule that's wrong for this codebase is turned off once in `.editorconfig` with a comment.

## Traps that will bite

1. **Probes and speed measurements run strictly serially.** `GetInputVideoHwaccelArgs` sets process-wide environment variables as a side effect; concurrent generation corrupts a neighbour's environment and gives plausible, wrong results. Snapshot, generate, launch, await, restore is one critical section (`SerialProbeGate`).
2. **`GetInputVideoHwaccelArgs` returns empty** when neither decoder nor encoder matches the backend. Empty means the probe can't be built, not that no arguments are needed; running it anyway scores a software transcode as a hardware pass. Exception: v4l2m2m, which is encoder-only upstream and always empty.
3. **Exit code 0 is not a pass.** ffmpeg exits cleanly after falling back to software. Require exit 0, frames produced, and device-init confirmation in verbose stderr.
4. **Drain stdout and stderr concurrently**, or output past about 1 MB deadlocks (jellyfin#17429).
5. **Kill the whole process tree on timeout.** A leaked ffmpeg holding a render node makes later, unrelated probes fail.
6. **The pipeline tier comes from OpenCL/Vulkan/`alphasrc`, not codec support.** A host can pass every codec probe and still be on the slow copy-back path; reporting that is the headline feature.
7. **Replacing plugin DLLs under the same version and using Jellyfin's in-process restart marks the plugin NotSupported.** Stop and start the server instead, and reset `status` in the plugin's `meta.json` if it already happened.

## Known limits

- Vulkan DRM interop isn't probed, so AMD never resolves to `FullVulkan` (a finding says so).
- A second i965/AMD GPU (not the configured device) is reported `Untested` by the plugin, since probing it would change the server's environment; the CLI can test it.
- CUDA is probed at index 0 only; `EncodingHelper` hard-codes device 0.
- v4l2m2m is confirmed from a recorded Raspberry Pi run (`Using device /dev/videoN`), not yet through Jellyfin.

## To do

- **Speed tier on real hardware**: run it on the RTX 5080 PC (NVENC, session limits) and the NAS (QSV/VAAPI, the QSV low-power settings). So far only macOS (VideoToolbox) and the software container.
- **Resource stats (on hold)**: memory and CPU time per encode process, and GPU use and power where exposed (nvidia-smi, intel_gpu_top, powermetrics, RAPL), to compare cost as well as speed.

Queued by the user on 2026-10-03, in no particular order:

- Speed results grid: make it more readable, so results can be compared between backends at a glance.
- Optional reuse of cached results: when a measurement's settings are exactly the same (keyed by a hash of the settings), load the earlier result instead of measuring again, so repeated runs compare faster.
- Rename the Probe and Speed tabs to something more descriptive.
- Rename the plugin to "Transcoding Diagnostics", and generally call the speed feature "performance" in user-facing text.
- Reorganize the Speed tab; perhaps move the results to a tab of their own.
- Theme the Probe tab to match the dashboard, as was done for the Speed tab.
- Move Diagnostics to a page/tab of its own; move the video file cache info there too and list the cache's contents.
- Check for further transcoding options the Speed page should offer (e.g. trickplay image generation), against Jellyfin's Transcoding and Trickplay pages.
- Split the recommended settings (the per-backend checklist with Apply/Revert) out of the Probe tab into their own tab; the Probe tab then no longer needs the current-backend card.
- Tables across the page are hard to read: consider borders or alternating row colours.
- Non-English use: find what breaks when the server or browser isn't in English, and consider translations, ideally reusing jellyfin-web's own strings. Include a non-English server locale: whether ffmpeg's output (stderr, progress, numbers) can be localized and break parsing, and whether .NET culture affects number formatting or parsing.
- Recommendations from speed results: suggest other transcoding settings (preset, CRF, threads, etc.) that balance quality against performance, based on what was measured.
