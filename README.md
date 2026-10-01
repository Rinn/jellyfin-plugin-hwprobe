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

Build `src/HwProbe.Plugin` and copy `Jellyfin.Plugin.HwProbe*.dll` into
`<config>/plugins/HwProbe_0.1.0.0/`. The plugin adds a configuration page with a **Run probe**
button, a *Probe hardware transcoding* scheduled task (no default trigger), and admin-only
endpoints: `GET /HwProbe/Report`, `GET /HwProbe/Status`, `POST /HwProbe/Run`. It refuses to probe
while any session is transcoding. It never changes Jellyfin's settings.

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

## License

GPL-3.0 — see [`LICENSE`](LICENSE).
