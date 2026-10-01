#!/bin/sh
# Installs the plugin into the jellyfin/jellyfin image, completes the setup wizard over the API,
# runs a probe through the plugin's endpoints and checks the results. Exits non-zero on any failed check.
# No GPU is passed through, so checks don't depend on what hardware is found.
set -eu

root="$(git rev-parse --show-toplevel)"
work="$root/artifacts/plugin-e2e"
image="${JELLYFIN_IMAGE:-docker.io/jellyfin/jellyfin:12.1}"
name=hwprobe-e2e
version="$(sed -n 's/^version: "\(.*\)"/\1/p' "$root/build.yaml")"
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

rm -rf "$work" && mkdir -p "$work/config/plugins/HwProbe_$version" "$work/cache"
dotnet publish "$root/src/HwProbe.Plugin" -c Release -o "$work/publish" -v q --nologo
cp "$work"/publish/Jellyfin.Plugin.HwProbe*.dll "$work/config/plugins/HwProbe_$version/"

podman rm -f "$name" >/dev/null 2>&1 || true
podman run -d --name "$name" -p 127.0.0.1::8096 -v "$work/config":/config -v "$work/cache":/cache "$image" >/dev/null
trap 'podman rm -f "$name" >/dev/null 2>&1' EXIT
base="http://$(podman port "$name" 8096 | head -1)"

echo "waiting for $image at $base"
healthy=no
for _ in $(seq 1 60); do
    if [ "$(curl -s "$base/health" || true)" = "Healthy" ]; then healthy=yes; break; fi
    sleep 2
done
if [ "$healthy" != yes ]; then
    echo "FAIL  server never became healthy"; podman logs --tail 50 "$name"; exit 1
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

check "plugin status" Active "$(curl -sf "$base/Plugins" -H "$h" | json 'next((p["Status"] for p in j if p["Name"]=="HwProbe"), "missing")')"
check "plugin version" "$version" "$(curl -sf "$base/Plugins" -H "$h" | json 'next((p["Version"] for p in j if p["Name"]=="HwProbe"), "missing")')"
check "scheduled task" "Probe hardware transcoding" "$(curl -sf "$base/ScheduledTasks" -H "$h" | json 'next((t["Name"] for t in j if t["Key"]=="HwProbeHardwareProbe"), "missing")')"
check "config page" 200 "$(code "$base/web/ConfigurationPage?name=HwProbe" -H "$h")"
check "report without token" 401 "$(code "$base/HwProbe/Report")"
check "report before a probe" 404 "$(code "$base/HwProbe/Report" -H "$h")"
check "start probe" 202 "$(code -X POST "$base/HwProbe/Run" -H "$h")"

state=Running
for _ in $(seq 1 60); do
    state="$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j["State"]')"
    [ "$state" = Idle ] && break
    sleep 2
done
check "probe finished" Idle "$state"
check "probe error" None "$(curl -sf "$base/HwProbe/Status" -H "$h" | json 'j.get("LastError")')"

report="$(curl -sf "$base/HwProbe/Report" -H "$h")"
printf "%s" "$report" | json '"\n".join("      %-8s %-8s %-12s %s" % (b["type"], b["device"] or "-", b["verdict"], b["hint"]) for b in j["backends"])'
check "report schema" 2 "$(printf "%s" "$report" | json 'j["schemaVersion"]')"
check "ffmpeg source" Server "$(printf "%s" "$report" | json 'j["ffmpeg"]["source"]')"
check "backends reported" True "$(printf "%s" "$report" | json 'len(j["backends"]) > 0')"
check "every failure has a remedy" True "$(printf "%s" "$report" | json 'all(b["hint"] for b in j["backends"] if b["verdict"] != "Viable")')"

task_id="$(curl -sf "$base/ScheduledTasks" -H "$h" | json 'next(t["Id"] for t in j if t["Key"]=="HwProbeHardwareProbe")')"
check "run task" 204 "$(code -X POST "$base/ScheduledTasks/Running/$task_id" -H "$h")"
result=-
for _ in $(seq 1 60); do
    result="$(curl -sf "$base/ScheduledTasks/$task_id" -H "$h" | json 'j["State"] + " " + (j.get("LastExecutionResult") or {}).get("Status", "-")')"
    case "$result" in "Idle Completed" | "Idle Failed") break ;; esac
    sleep 2
done
check "task result" "Idle Completed" "$result"

echo "$failures failed check(s)"
[ "$failures" -eq 0 ]
