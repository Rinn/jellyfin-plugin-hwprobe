#!/bin/sh
# Runs the test suite on Linux, then hwprobe against jellyfin-ffmpeg in the jellyfin/jellyfin image.
# No GPU is passed through, so every backend should report NotPresent with a remedy.
set -eu

root="$(git rev-parse --show-toplevel)"
out="$root/artifacts/linux-publish"
sdk=mcr.microsoft.com/dotnet/sdk:10.0
copy='mkdir /work && cd /src && tar --exclude=./.git --exclude=./.claude --exclude=./artifacts --exclude="*/bin" --exclude="*/obj" -cf - . | tar -C /work -xf - && cd /work'

podman run --rm -v "$root":/src:ro "$sdk" sh -c "$copy && dotnet test"

rm -rf "$out" && mkdir -p "$out"
podman run --rm -v "$root":/src:ro -v "$out":/out "$sdk" sh -c \
    "$copy && dotnet publish src/HwProbe.Cli -c Release -r linux-arm64 --self-contained -o /out -v q"
podman run --rm --entrypoint /bin/sh -v "$out":/hwprobe:ro "${JELLYFIN_IMAGE:-ghcr.io/jellyfin/jellyfin:12.2}" -c \
    '/hwprobe/Jellyfin.Plugin.HwProbe.Cli "$@"' -- "$@"
