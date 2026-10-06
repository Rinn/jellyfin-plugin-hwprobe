# Contributing to HwProbe

## Hardware reports

Results from backends not yet tested on real hardware (AMD AMF, VAAPI on AMD, Rockchip MPP, and Video4Linux2) are the most useful contribution. Run the probe, download the diagnostics zip from the **Help** tab, and attach it to a [hardware report](../../issues/new?template=hardware-report.yml). The zip includes file paths and host names.

## Issues

Bug reports should include the Jellyfin version, the HwProbe version, the operating system, and the diagnostics zip where relevant.

## Pull requests

Building and testing are covered in [DEVELOPMENT.md](DEVELOPMENT.md), and the design in [ARCHITECTURE.md](ARCHITECTURE.md). Install the pre-commit hook once per clone:

```sh
git config core.hooksPath scripts/
```

It runs these checks, which must all pass. Code style and documentation rules are enforced by the build.

```sh
dotnet build -warnaserror
dotnet format --verify-no-changes
dotnet test
sh scripts/check-page.sh
python3 scripts/notices.py --check
editorconfig-checker
```

Requirements:

- Lists, labels, defaults, and report text go in `src/HwProbe.Core/Data/catalog.yaml`, not code.
- ffmpeg arguments come from Jellyfin's `EncodingHelper`, never hardcoded.
- Claims about Jellyfin's behaviour cite the upstream file and symbol.
- Settings are never applied automatically, and the server is never restarted.
- `README.md`, `DEVELOPMENT.md`, and `ARCHITECTURE.md` are updated in the same pull request as the change they describe.

Contributions are licensed under [GPL-3.0](LICENSE).
