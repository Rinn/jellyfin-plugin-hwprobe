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

## Pre-commit hook

All three checks above must pass before every commit. Install the checked-in hook once per clone:

```sh
git config core.hooksPath scripts/
```

## License

GPL-3.0 — see [`LICENSE`](LICENSE).
