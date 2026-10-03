#!/bin/sh
# Rebuilds assets/plugin.png (1920x1080, the plugin catalog image) from developer_board.svg and Noto Sans.
# Renders with headless Google Chrome (set CHROME to its binary if it isn't in the macOS default place).
# The font is downloaded to artifacts/ and checked against a pinned hash.
set -eu

root="$(git rev-parse --show-toplevel)"
work="$root/artifacts/plugin-image"
font_url=https://github.com/notofonts/notofonts.github.io/raw/main/fonts/NotoSans/hinted/ttf/NotoSans-SemiBold.ttf
font_sha=a4e91fd530ac2b4ef5367240144ff37d7d65d66cf76f2e9a2187b93c676f92d0

mkdir -p "$work"
[ -f "$work/NotoSans-SemiBold.ttf" ] || curl -sfL -o "$work/NotoSans-SemiBold.ttf" "$font_url"
[ "$(shasum -a 256 "$work/NotoSans-SemiBold.ttf" | cut -d' ' -f1)" = "$font_sha" ] || { echo "font hash mismatch"; exit 1; }

icon="$(sed 's/ height="24"//; s/ width="24"//; s/<svg /<svg width="300" height="300" fill="#fff" /' "$root/assets/developer_board.svg")"
font="$(base64 < "$work/NotoSans-SemiBold.ttf" | tr -d '\n')"
cat > "$work/plugin.html" <<HTML
<html><head><meta charset="utf-8"><style>
@font-face { font-family: Noto; src: url(data:font/ttf;base64,$font) format("truetype"); }
html, body { margin: 0; width: 1920px; height: 1080px; overflow: hidden; }
body { display: flex; align-items: center; justify-content: center; gap: 70px;
       background: radial-gradient(ellipse at center, #333 0%, #181818 75%); }
.word { font-family: Noto; font-size: 220px; color: #fff; }
</style></head><body>$icon<div class="word">HwProbe</div></body></html>
HTML

chrome="${CHROME:-/Applications/Google Chrome.app/Contents/MacOS/Google Chrome}"
"$chrome" --headless --disable-gpu --hide-scrollbars --window-size=1920,1080 \
    --screenshot="$root/assets/plugin.png" "file://$work/plugin.html" >/dev/null 2>&1
echo "wrote assets/plugin.png"
