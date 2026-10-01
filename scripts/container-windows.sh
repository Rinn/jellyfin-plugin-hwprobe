#!/bin/sh
# Runs the win-x64 build under Wine against Windows jellyfin-ffmpeg.
# Needs x86_64 containers; on Apple Silicon enable Rosetta ([machine] rosetta = true in
# ~/.config/containers/containers.conf) or Wine is too slow to use.
set -eu

root="$(git rev-parse --show-toplevel)"
win="$root/artifacts/win"
release=v8.1.3-1
zip="jellyfin-ffmpeg_8.1.3-1_portable_win64-clang-gpl.zip"
copy='mkdir /work && cd /src && tar --exclude=./.git --exclude=./.claude --exclude=./artifacts --exclude="*/bin" --exclude="*/obj" -cf - . | tar -C /work -xf - && cd /work'

mkdir -p "$win/ffmpeg"
if [ ! -f "$win/ffmpeg/ffmpeg.exe" ]; then
    gh release download "$release" -R jellyfin/jellyfin-ffmpeg -p "$zip" -D "$win" --clobber
    unzip -q -o "$win/$zip" -d "$win/ffmpeg"
fi

cat > "$win/Containerfile" <<'CONTAINERFILE'
FROM docker.io/library/ubuntu:24.04
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends wine64 ca-certificates && rm -rf /var/lib/apt/lists/*
ENV WINEDEBUG=-all WINEPREFIX=/wine PATH=/usr/lib/wine:$PATH
RUN wine64 wineboot --init && wineserver -w
CONTAINERFILE
podman build -q --platform linux/amd64 -t hwprobe-wine -f "$win/Containerfile" "$win" >/dev/null

rm -rf "$win/hwprobe" && mkdir -p "$win/hwprobe"
podman run --rm -v "$root":/src:ro -v "$win/hwprobe":/out mcr.microsoft.com/dotnet/sdk:10.0 sh -c \
    "$copy && dotnet publish src/HwProbe.Cli -c Release -r win-x64 --self-contained -o /out -v q"
podman run --rm --platform linux/amd64 -v "$win":/win hwprobe-wine sh -c \
    'cd /win/hwprobe && wine64 Jellyfin.Plugin.HwProbe.Cli.exe --ffmpeg "Z:\\win\\ffmpeg\\ffmpeg.exe" "$@"' -- "$@"
