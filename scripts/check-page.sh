#!/bin/sh
# Syntax-checks the plugin page's script: one error there stops the whole page, every button with it.
set -eu
cd "$(git rev-parse --show-toplevel)"
js="$(mktemp "${TMPDIR:-/tmp}/hwprobe-page.XXXXXX").js"
trap 'rm -f "$js"' EXIT
python3 - "$js" <<'PY'
import re, sys
page = open("src/HwProbe.Plugin/Configuration/configPage.html", encoding="utf-8").read()
scripts = re.findall(r'<script type="text/javascript">(.*?)</script>', page, re.S)
if not scripts:
    sys.exit("check-page: no script in configPage.html")
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(scripts))
PY
node --check "$js"
echo "check-page: configPage.html script parses"
