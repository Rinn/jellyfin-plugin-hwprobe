# CLAUDE.md

Operating manual for this repo: what it is, commands, conventions, the traps that cost real time, and the work still to do. End-user docs are in `README.md`; how to build, test, and release is in `DEVELOPMENT.md`; how the engines, caches, and suggestions work is in `ARCHITECTURE.md`.

## What this is

A device-verified hardware-transcode detector for Jellyfin, shipped as a CLI (`src/HwProbe.Cli`) and a plugin (`src/HwProbe.Plugin`) over a shared engine (`src/HwProbe.Core`, with Jellyfin's own `EncodingHelper` wired in by `src/HwProbe.Jellyfin`). Jellyfin's hardware acceleration dropdown is a fixed list in jellyfin-web, so it offers backends that fail every job; HwProbe runs small real transcodes and reports what works, with a fix for each failure. The page has five tabs: Hardware Probe, Recommended Settings (Apply, history and Revert), Performance Tests and Test Results (transcode speed per backend, single runs or test suites, driven by `src/HwProbe.Core/Data/catalog.yaml`; "speed" in code, "performance" in user-facing text), and Help (diagnostics zip, cache contents).

Public at https://github.com/Rinn/jellyfin-plugin-hwprobe (`origin`).

## Commands

```sh
dotnet build -warnaserror          # must be clean
dotnet format --verify-no-changes  # formatting/style, non-mutating
dotnet test                        # Unit + FakeFfmpeg traits only
sh scripts/check-page.sh           # syntax-checks the plugin page's script
python3 scripts/notices.py --check # third-party notices match the restored packages

HWPROBE_HW_TESTS=1 dotnet test     # adds RealFfmpeg + Hardware traits
sh scripts/container-plugin.sh     # installs the plugin in Jellyfin 12.1 (podman) and checks it through the API
```

The first five must pass before every commit; `scripts/pre-commit` enforces them. A test run with zero tests exits 8 and fails the gate; don't hide it with `--ignore-exit-code`.

Releases: run `release.yml` by hand (see `DEVELOPMENT.md`). Releases are immutable, so a used version can never be reused. The version comes only from the release tag; `Directory.Build.props` holds `0.0.0`.

## Environment

- Dev machine: macOS. Only VideoToolbox is exercisable here. ffmpeg at `/opt/homebrew/bin/ffmpeg` is not the binary the server uses; always report the resolved path and flag non-Jellyfin builds.
- Real hardware: an Intel NAS (QSV over VAAPI) and an RTX 5080 Windows PC; see the NAS and Windows testing memories.
- No AMD, AMF, or Rockchip hardware: ship those as argument-string tests plus recorded logs and report them `Untested`. Never imply a verdict that wasn't tested.
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
- **Put catalog-like data in `catalog.yaml`, not code.** Lists, labels, defaults, and orderings the page, CLI, or advisor share (inputs, outputs, run options, quality order) belong in the catalog, served to the page by `HwProbe/Catalog`; the page shouldn't keep its own copy. Upstream constants and logic stay in code.
- **Don't hardcode ffmpeg arguments.** Generate them through `EncodingHelper`. The pinned drift strings detect upstream changes; they aren't the source of truth. The one exception is the bare device-open probe, which upstream never emits.
- **Keep this file current in every PR**, along with `README.md`, `DEVELOPMENT.md`, `ARCHITECTURE.md`, and `build.yaml`: state, commands, traps, and the to-do list.
- **Never auto-apply settings and never restart the server.** Apply writes only advised `EncodingOptions` values through `SaveConfiguration`, with history and Revert.

## Code style

- **Docstrings on every member**, private included: `<summary>`, a `<param>` per parameter, `<returns>` for non-void, one line each where possible. Enforced by StyleCop SA1600 (with `documentPrivateElements`), SA1611 and SA1615. Never suppress these.
- **One type per file**, named after the type, small enums and records included (SA1402 + SA1649, with `topLevelTypes` widened in `stylecop.json`).
- **Async entry points**: `static async Task<int> Main` in an explicit `Program` class. No `Thread.Sleep` or blocking I/O; no `ConfigureAwait(false)` (CA2007 is off, as upstream has it).
- **Comments only where the code isn't self-documenting**, terse, explaining why. Upstream constraints and ffmpeg quirks are the usual reason; cite the source.
- **Gate platform- and hardware-bound tests declaratively** with `[Fact(Skip = "Requires …", SkipUnless = nameof(TestEnvironment.X), SkipType = typeof(TestEnvironment))]` from `tests/TestSupport/TestEnvironment.cs`, never an `if (OperatingSystem.Is…)` inside a test. Real ffmpeg comes from `TestEnvironment.RealFfmpeg`.

## Plugin page conventions

The page should look and behave like a native Jellyfin dashboard page, stay calm while work runs, and say only what's needed.

- **Consistency**: the same kind of thing looks and reads the same everywhere. Sections on every tab use the same collapsible heading, and buttons for the same kind of action share a style: raised for an action, red for a destructive one. Notices are the same banner, and icons mean one thing across tabs. Each concept has one term, used alike on the page, in the CLI, in the docs, and in `catalog.yaml`: setting, backend, input, output, Duration. When a term or style changes, change it everywhere in the same commit.
- **Wording**: terse, impersonal (no "you"), plain words. Serial commas in every list. "requires", not "needs". "performance" in UI text, "speed" in code. A label shouldn't repeat what an icon or heading already says. Help text only where the control isn't self-explanatory.
- **Look**: only jellyfin-web classes and `--jf-palette` variables; no `${` anywhere in `configPage.html`. When jellyfin-web's legacy styles differ from the dashboard's MUI components (disabled buttons and selects), match the MUI look. Status uses emoji icons placed before the item: ✅ works, ❌ failed, ⚠️ software fallback, ➖ not used, 🚫 unavailable, ❔ untested.
- **Layout**: the page uses the window's full width, never narrower than the dashboard's 54em (short of a narrower screen). Tables full width and sortable, by value for numbers and naturally for text. Distinct header and group rows, no alternating stripes. Action buttons right-aligned. Destructive actions are red, and the bulk one asks first, the question going away on a click elsewhere. Main actions go at the top of a tab, and long settings sit in collapsible sections that keep their open state across redraws.
- **Order and defaults**: follow Jellyfin's own order (the Hardware acceleration dropdown, the Transcoding page), with None (software) first. Real video comes before test video, and defaults favour real video.
- **While work runs**: give feedback the moment a button is pressed. Show progress as a loading bar with the count done and the time elapsed, updated about every 250 ms, without changing the layout's size. Redraw only what changed, never rebuilding controls under the cursor. Disable whatever the server would refuse. Hide results that a running probe will replace instead of showing them as current.
- **Data**: lists, labels, defaults, and orderings come from `catalog.yaml` through `HwProbe/Catalog`, not from copies in the page.

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
8. **Never rename the package** (build.yaml `name`, `Plugin.Name`). Jellyfin removes an updated plugin's old folder by name, not GUID (`PluginManager.DiscoverPlugins`), so after an update both versions load and every `HwProbe/` route returns 500 (AmbiguousMatchException). Reproduced 2026-10-03 updating 0.10.2 to a renamed build.

## Known limits

- Vulkan DRM interop isn't probed, so AMD never resolves to `FullVulkan` (a finding says so).
- A second i965/AMD GPU (not the configured device) is reported `Untested` by the plugin, since probing it would change the server's environment; the CLI can test it.
- CUDA is probed at index 0 only; `EncodingHelper` hard-codes device 0.
- v4l2m2m is confirmed from a recorded Raspberry Pi run (`Using device /dev/videoN`), not yet through Jellyfin.
- Page and CLI text are English only. Behaviour doesn't depend on locale: ffmpeg runs with `LC_ALL=C`, numbers are formatted and parsed invariantly, and the page reads structured fields rather than message text. `CultureScope` tests and `HWPROBE_LOCALE` in `container-plugin.sh` check it.
- The busy check (`ProbeService.IsTranscoding`) skips direct play and remux, but a transcode that finished ahead of playback still blocks until playback stops: `TranscodeManager.OnFfMpegProcessExited` leaves the session's `TranscodingInfo` set, and Jellyfin has no API to list running jobs by session.

## To do

- **Power draw, next steps**: AMD GPU power (amdgpu hwmon `power1_average`, watts only, to integrate over samples) and macOS (IOReport is a private API whose CPU channels read 0 on macOS 27). Intel and AMD GPU usage on Linux 5.19+ is unverified, as no such host is available.

Queued by the user on 2026-10-03, in no particular order:

- Trickplay generation as a Performance Tests output (researched 2026-10-04). Input, filter, and encoder come from public EncodingHelper methods; only `MediaEncoder.ExtractVideoImagesOnIntervalAccelerated`'s wrapper (skip_frame, setpts, quality per encoder, threads, image2) needs a pinned copy, guarded by drift tests and a log diff in `container-plugin.sh`. A direct call was rejected: configured backend only, no CLI, no stderr to confirm hardware, slow cancellation.
