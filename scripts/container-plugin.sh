#!/bin/sh
# Installs the plugin into the jellyfin/jellyfin image, completes the setup wizard over the API,
# runs a probe through the plugin's endpoints and checks the results. Exits non-zero on any failed check.
# No GPU is passed through, so checks don't depend on what hardware is found.
#
# HWPROBE_INSTALL=copy (default) copies the DLLs into the plugins folder. HWPROBE_INSTALL=repository
# installs the way users do: the zip and a manifest built by scripts/manifest.py are served from a
# second container, added as a plugin repository, and installed through Jellyfin's package API.
# HWPROBE_INSTALL=existing checks a server that is already running with the plugin installed and the
# setup wizard not yet done, at HWPROBE_BASE (e.g. http://nas.local:18096); nothing is built or started.
# HWPROBE_LOCALE (e.g. de_DE.UTF-8) runs the server under that locale, to check nothing depends on the server's language.
set -eu

root="$(git rev-parse --show-toplevel)"
work="$root/artifacts/plugin-e2e"
image="${JELLYFIN_IMAGE:-ghcr.io/jellyfin/jellyfin:12.2}"
name=hwprobe-e2e
repo=hwprobe-e2e-repo
net=hwprobe-e2e-net
install="${HWPROBE_INSTALL:-copy}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props").0"
guid="$(sed -n 's/^guid: "\(.*\)"/\1/p' "$root/build.yaml")"
auth='MediaBrowser Client="hwprobe-e2e", Device="script", DeviceId="hwprobe-e2e", Version="1.0"'
failures=0

check() { # label expected actual
    if [ "$2" = "$3" ]; then
        echo "ok    $1: $3"
    else
        echo "FAIL  $1: expected '$2', got '$3'"
        failures=$((failures + 1))
    fi
}

json() { # python expression over the JSON on stdin, bound to j
    python3 -c "import json,sys; j=json.load(sys.stdin); print($1)"
}

wait_healthy() {
    echo "waiting for $image at $base"
    for _ in $(seq 1 60); do
        [ "$(curl -s "$base/health" || true)" = "Healthy" ] && return 0
        sleep 2
    done
    echo "FAIL  server never became healthy"
    [ "$install" = existing ] || podman logs --tail 50 "$name"
    exit 1
}

if [ "$install" = existing ]; then
    base="${HWPROBE_BASE:?HWPROBE_BASE must be set for HWPROBE_INSTALL=existing}"
    wait_healthy
else

rm -rf "$work" && mkdir -p "$work/config/plugins" "$work/cache" "$work/repo"
# Analyzers are skipped: the build already enforces them and they don't change the output.
dotnet publish "$root/src/HwProbe.Plugin" -c Release -p:RunAnalyzers=false -o "$work/publish" -v q --nologo

# --ignore: without it, a missing container (repo, in copy mode) stops podman removing the others.
podman rm -f --ignore "$name" "$repo" >/dev/null
podman network rm "$net" >/dev/null 2>&1 || true
trap 'podman rm -f --ignore "$name" "$repo" >/dev/null; podman network rm "$net" >/dev/null 2>&1 || true' EXIT
case "$install" in
    copy)
        mkdir -p "$work/config/plugins/HwProbe_$version"
        cp "$work"/publish/Jellyfin.Plugin.HwProbe*.dll "$work"/publish/YamlDotNet.dll "$work"/publish/Meziantou.Framework.Win32.Jobs.dll "$work/config/plugins/HwProbe_$version/"
        ;;
    repository)
        zip="hwprobe-plugin_$version.zip"
        (cd "$work/publish" && zip -q "$work/repo/$zip" Jellyfin.Plugin.HwProbe*.dll YamlDotNet.dll Meziantou.Framework.Win32.Jobs.dll)
        python3 "$root/scripts/manifest.py" --build-yaml "$root/build.yaml" --zip "$work/repo/$zip" \
            --version "$version" --source-url "http://$repo:8000/$zip" --out "$work/repo/manifest.json"
        podman network create "$net" >/dev/null
        podman run -d --name "$repo" --network "$net" -v "$work/repo":/repo:ro -w /repo \
            docker.io/library/python:3-alpine python -m http.server 8000 >/dev/null
        ;;
    *) echo "HWPROBE_INSTALL must be copy or repository, not '$install'"; exit 2 ;;
esac

network=""
[ "$install" = repository ] && network="--network $net"
# Nothing is downloaded: fixtures that only download (the VC-1 sample) are reported as untested.
locale=""
[ -n "${HWPROBE_LOCALE:-}" ] && locale="-e LANG=$HWPROBE_LOCALE -e LC_ALL=$HWPROBE_LOCALE"
podman run -d --name "$name" $network $locale -p 127.0.0.1::8096 -e HWPROBE_NO_DOWNLOADS=1 \
    -v "$work/config":/config -v "$work/cache":/cache "$image" >/dev/null
base="http://$(podman port "$name" 8096 | head -1)"
wait_healthy
fi

curl -sf -X POST "$base/Startup/Configuration" -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
curl -sf "$base/Startup/User" >/dev/null
curl -sf -X POST "$base/Startup/User" -H 'Content-Type: application/json' -d '{"Name":"admin","Password":"hwprobe"}'
curl -sf -X POST "$base/Startup/Complete"
token="$(curl -sf -X POST "$base/Users/AuthenticateByName" -H "Authorization: $auth" -H 'Content-Type: application/json' \
    -d '{"Username":"admin","Pw":"hwprobe"}' | json 'j["AccessToken"]')"
h="Authorization: $auth, Token=\"$token\""
code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }

if [ "$install" = repository ]; then
    url="http://$repo:8000/manifest.json"
    added="$(curl -s -w ' %{http_code}' -X POST "$base/Repositories" -H "$h" -H 'Content-Type: application/json' \
        -d "[{\"Name\":\"hwprobe-e2e\",\"Url\":\"$url\",\"Enabled\":true}]")"
    check "add repository" 204 "${added##* }"
    [ "${added##* }" = 204 ] || echo "      $added"
    check "listed in catalog" "$version" "$(curl -sf "$base/Packages" -H "$h" | json 'next((v["version"] for p in j if p["name"]=="HwProbe" for v in p["versions"]), "missing")')"
    check "install" 204 "$(code -X POST "$base/Packages/Installed/HwProbe?assemblyGuid=$guid&version=$version&repositoryUrl=$url" -H "$h")"
    installed=no
    for _ in $(seq 1 30); do
        [ -f "$work/config/plugins/HwProbe_$version/meta.json" ] && { installed=yes; break; }
        sleep 2
    done
    check "installed with meta.json" yes "$installed"
    podman restart "$name" >/dev/null
    base="http://$(podman port "$name" 8096 | head -1)"
    wait_healthy
fi

check "plugin status" Active "$(curl -sf "$base/Plugins" -H "$h" | json 'next((p["Status"] for p in j if p["Name"]=="HwProbe"), "missing")')"
check "plugin version" "$version" "$(curl -sf "$base/Plugins" -H "$h" | json 'next((p["Version"] for p in j if p["Name"]=="HwProbe"), "missing")')"
check "config page" 200 "$(code "$base/web/ConfigurationPage?name=HwProbe" -H "$h")"
check "sidebar entry" "HwProbe developer_board" "$(curl -sf "$base/web/ConfigurationPages?enableInMainMenu=true" -H "$h" | json 'next((p["DisplayName"] + " " + p["MenuIcon"] for p in j if p["Name"]=="HwProbe"), "missing")')"
check "report without token" 401 "$(code "$base/HwProbe/Report")"
check "report before a probe" 404 "$(code "$base/HwProbe/Report" -H "$h")"
check "diagnostics before a probe" 404 "$(code "$base/HwProbe/Diagnostics" -H "$h")"
check "start probe" 202 "$(code -X POST "$base/HwProbe/Run" -H "$h")"

# A probe on real hardware runs the full matrix, which takes minutes.
state=Running
for _ in $(seq 1 300); do
    state="$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j["State"]')"
    [ "$state" = Idle ] && break
    sleep 2
done
check "probe finished" Idle "$state"
check "probe error" None "$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j.get("LastError")')"

report="$(curl -sf "$base/HwProbe/Report" -H "$h")"
printf "%s" "$report" | json '"\n".join("      %-8s %-8s %-12s %s" % (b["type"], b["device"] or "-", b["verdict"], b["hint"]) for b in j["backends"])'
check "report schema" 3 "$(printf "%s" "$report" | json 'j["schemaVersion"]')"
check "ffmpeg source" Server "$(printf "%s" "$report" | json 'j["ffmpeg"]["source"]')"
check "backends reported" True "$(printf "%s" "$report" | json 'len(j["backends"]) > 0')"
curl -sf "$base/HwProbe/Diagnostics" -H "$h" -o "$work/diagnostics.zip"
check "diagnostics zip" True "$(python3 -c "import sys,zipfile; n=zipfile.ZipFile(sys.argv[1]).namelist(); print('report.json' in n and 'ffmpeg/version.txt' in n and any(x.startswith('stderr/') for x in n))" "$work/diagnostics.zip")"
check "catalog listed" True "$(curl -sf "$base/HwProbe/Catalog" -H "$h" | json 'any(v["Key"] == "drama" and v["Default"] for v in j["Videos"]) and any(o["Key"] == "decode" for o in j["Outputs"]) and j["Backends"][0]["Type"] == "amf" and j["Tiers"]["FullOpencl"] != ""')"
check "libraries listed" True "$(curl -sf "$base/HwProbe/Libraries" -H "$h" | json 'any(l["Name"] == "YamlDotNet" and l["License"] == "MIT" for l in j)')"
check "speed with an unknown video" 400 "$(code -X POST "$base/HwProbe/Speed" -H "$h" -H 'Content-Type: application/json' -d '{"Method":"Quick","Videos":["nope"],"Outputs":[]}')"
check "start speed run" 202 "$(code -X POST "$base/HwProbe/Speed" -H "$h" -H 'Content-Type: application/json' -d '{"Method":"Quick","Videos":["pattern"],"Outputs":["decode"]}')"
state=Running
for _ in $(seq 1 150); do
    state="$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j["State"]')"
    [ "$state" = Idle ] && break
    sleep 2
done
check "speed run finished" Idle "$state"
check "speed run error" None "$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j.get("LastError")')"
check "speed run in history" True "$(curl -sf "$base/HwProbe/SpeedHistory" -H "$h" | json 'len(j) >= 1')"
check "start a reusing run" 202 "$(code -X POST "$base/HwProbe/Speed" -H "$h" -H 'Content-Type: application/json' -d '{"Method":"Quick","Videos":["pattern"],"Outputs":["decode"],"ReuseResults":true}')"
for _ in $(seq 1 150); do
    [ "$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j["State"]')" = Idle ] && break
    sleep 2
done
check "earlier measurement reused" True "$(curl -sf "$base/HwProbe/Speed" -H "$h" | json 'all(r.get("reusedFromUtc") for r in j["results"] if r["fps"])')"
check "delete an unknown run" 404 "$(code -X DELETE "$base/HwProbe/SpeedHistory/20000101T000000Z" -H "$h")"
check "cache size" True "$(curl -sf "$base/HwProbe/Cache" -H "$h" | json 'j["Files"] > 0')"
check "cache contents named" True "$(curl -sf "$base/HwProbe/Cache/Contents" -H "$h" | json 'len(j) > 0 and any(e["Description"] for e in j)')"
check "software decode measured" True "$(curl -sf "$base/HwProbe/Speed" -H "$h" | json 'any(r["type"] == "none" and r["test"] == "pattern|decode" and (r["fps"] or 0) > 0 for r in j["results"])')"
check "every failure has a remedy" True "$(printf "%s" "$report" | json 'all(b["hint"] for b in j["backends"] if b["verdict"] != "Viable")')"

before="$(curl -sf "$base/System/Configuration/encoding" -H "$h")"
check "software advised" True "$(printf "%s" "$report" | json 'any(a["setting"] == "EnableSubtitleExtraction" and a["state"] == "TurnOn" for a in j["software"]["settings"])')"
check "apply against the software advice" 400 "$(code -X POST "$base/HwProbe/Apply" -H "$h" -H 'Content-Type: application/json' -d '[{"Setting":"AllowAv1Encoding","Value":true}]')"
check "switch to a backend that didn't work" 400 "$(code -X POST "$base/HwProbe/UseBackend" -H "$h" -H 'Content-Type: application/json' -d '{"Type":"nvenc","Device":"0"}')"
check "revert with no history" 409 "$(code -X POST "$base/HwProbe/Revert" -H "$h")"
check "history" "[]" "$(curl -sf "$base/HwProbe/History" -H "$h")"
check "no restart pending" false "$(curl -sf "$base/HwProbe/RestartRequired" -H "$h")"
check "refused changes left settings alone" True "$(curl -sf "$base/System/Configuration/encoding" -H "$h" | python3 -c "import json,sys; print(json.load(sys.stdin) == json.loads(sys.argv[1]))" "$before")"

check "no scheduled task" missing "$(curl -sf "$base/ScheduledTasks" -H "$h" | json 'next((t["Name"] for t in j if t["Key"]=="HwProbeHardwareProbe"), "missing")')"

# Uninstalling only marks the plugin deleted; Jellyfin removes its folder and stops loading it at the next start.
# Run last, and not against an existing server, which this would leave without the plugin.
if [ "$install" != existing ]; then
    check "uninstall" 204 "$(code -X DELETE "$base/Plugins/$guid/$version" -H "$h")"
    podman restart "$name" >/dev/null
    base="http://$(podman port "$name" 8096 | head -1)"
    wait_healthy
    check "uninstalled: not listed" missing "$(curl -sf "$base/Plugins" -H "$h" | json 'next((p["Status"] for p in j if p["Id"].replace("-", "")=="'"$(echo "$guid" | tr -d -)"'"), "missing")')"
    check "uninstalled: folder removed" none "$(ls -d "$work"/config/plugins/HwProbe_* 2>/dev/null || echo none)"
    check "uninstalled: no sidebar entry" missing "$(curl -sf "$base/web/ConfigurationPages?enableInMainMenu=true" -H "$h" | json 'next((p["DisplayName"] for p in j if p["Name"]=="HwProbe"), "missing")')"
    check "uninstalled: API gone" 404 "$(code "$base/HwProbe/Status" -H "$h")"
fi

echo "$failures failed check(s)"
[ "$failures" -eq 0 ]
