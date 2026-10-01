# jellyfin-plugin-hwprobe

Device-verified hardware-transcode detection for Jellyfin.

Jellyfin's hardware-acceleration dropdown is built from `ffmpeg -hwaccels`, which lists what the
binary was *compiled* with, not what this machine can actually *open*. HwProbe runs tiny real
transcodes and reports which backends, codecs and filter-pipeline tiers genuinely work, with a
remedy for each failure.

**Status:** early development — Milestone 1 (standalone CLI).

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
```

Extra arguments are passed to hwprobe. On Apple Silicon the Windows script needs Rosetta for
x86_64 containers: add `[machine]` / `rosetta = true` to `~/.config/containers/containers.conf`
and restart the podman machine.

## Pre-commit hook

All three checks above must pass before every commit. Install the checked-in hook once per clone:

```sh
git config core.hooksPath scripts/
```

## License

GPL-3.0 — see [`LICENSE`](LICENSE).
