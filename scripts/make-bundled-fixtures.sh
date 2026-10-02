#!/bin/sh
# Remakes the test clips bundled in HwProbe.Core, with the arguments FixtureCatalog uses to generate them.
# After running, put the printed SHA-256 values in FixtureCatalog.
set -eu
out="$(cd "$(dirname "$0")/.." && pwd)/src/HwProbe.Core/Fixtures/Bundled"
ffmpeg=${FFMPEG:-ffmpeg}
source="-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=25 -frames:v 25"

# shellcheck disable=SC2086
"$ffmpeg" $source -c:v libx265 -pix_fmt yuv444p10le "$out/hevc_rext_444_10bit.mp4"
# shellcheck disable=SC2086
"$ffmpeg" $source -c:v libx265 -pix_fmt yuv422p12le "$out/hevc_rext_422_12bit.mp4"
# shellcheck disable=SC2086
"$ffmpeg" $source -c:v libx265 -pix_fmt yuv444p12le "$out/hevc_rext_12bit.mp4"
# shellcheck disable=SC2086
"$ffmpeg" $source -c:v libsvtav1 -pix_fmt yuv420p10le "$out/av1_10bit.mp4"
# shellcheck disable=SC2086
"$ffmpeg" $source -c:v libx264 -g 2 -pix_fmt yuv420p "$out/h264_keyframes.mp4"

shasum -a 256 "$out"/*.mp4
