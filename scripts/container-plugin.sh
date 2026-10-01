#!/bin/sh
# Installs the plugin into the jellyfin/jellyfin image, completes the setup wizard over the API,
# runs a probe through the plugin's endpoints and prints the report. No GPU is passed through.
set -eu

root="$(git rev-parse --show-toplevel)"
work="$root/.claude/plugin-e2e"
name=hwprobe-e2e
version="$(sed -n 's/^version: "\(.*\)"/\1/p' "$root/build.yaml")"
auth='MediaBrowser Client="hwprobe-e2e", Device="script", DeviceId="hwprobe-e2e", Version="1.0"'

rm -rf "$work" && mkdir -p "$work/config/plugins/HwProbe_$version" "$work/cache"
dotnet publish "$root/src/HwProbe.Plugin" -c Release -o "$work/publish" -v q --nologo
cp "$work"/publish/Jellyfin.Plugin.HwProbe*.dll "$work/config/plugins/HwProbe_$version/"

podman rm -f "$name" >/dev/null 2>&1 || true
podman run -d --name "$name" -p 127.0.0.1::8096 -v "$work/config":/config -v "$work/cache":/cache docker.io/jellyfin/jellyfin:latest >/dev/null
trap 'podman rm -f "$name" >/dev/null 2>&1' EXIT
base="http://$(podman port "$name" 8096 | head -1)"

echo "waiting for $base"
for _ in $(seq 1 60); do
    [ "$(curl -s "$base/health" || true)" = "Healthy" ] && break
    sleep 2
done

curl -sf -X POST "$base/Startup/Configuration" -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
curl -sf "$base/Startup/User" >/dev/null
curl -sf -X POST "$base/Startup/User" -H 'Content-Type: application/json' -d '{"Name":"admin","Password":"hwprobe"}'
curl -sf -X POST "$base/Startup/Complete"
token="$(curl -sf -X POST "$base/Users/AuthenticateByName" -H "Authorization: $auth" -H 'Content-Type: application/json' \
    -d '{"Username":"admin","Pw":"hwprobe"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["AccessToken"])')"
h="Authorization: $auth, Token=\"$token\""

echo "plugin:      $(curl -sf "$base/Plugins" -H "$h" | python3 -c 'import json,sys; print([(p["Name"],p["Version"],p["Status"]) for p in json.load(sys.stdin) if p["Name"]=="HwProbe"])')"
echo "task:        $(curl -sf "$base/ScheduledTasks" -H "$h" | python3 -c 'import json,sys; print([t["Name"] for t in json.load(sys.stdin) if t["Key"]=="HwProbeHardwareProbe"])')"
echo "config page: HTTP $(curl -s -o /dev/null -w '%{http_code}' "$base/web/ConfigurationPage?name=HwProbe" -H "$h")"
echo "no token:    HTTP $(curl -s -o /dev/null -w '%{http_code}' "$base/HwProbe/Report")"
echo "report:      HTTP $(curl -s -o /dev/null -w '%{http_code}' "$base/HwProbe/Report" -H "$h") before any probe"
echo "run:         HTTP $(curl -s -o /dev/null -w '%{http_code}' -X POST "$base/HwProbe/Run" -H "$h")"

for _ in $(seq 1 60); do
    state="$(curl -sf "$base/HwProbe/Status" -H "$h" | python3 -c 'import json,sys; print(json.load(sys.stdin)["State"])')"
    [ "$state" = "Idle" ] && break
    sleep 2
done
curl -sf "$base/HwProbe/Status" -H "$h"; echo
curl -sf "$base/HwProbe/Report" -H "$h" | python3 -c '
import json, sys
r = json.load(sys.stdin)
print("ffmpeg:", r["ffmpeg"]["path"], r["ffmpeg"]["version"], r["ffmpeg"]["source"])
for b in r["backends"]:
    print("  %-8s %-8s %-12s %s" % (b["type"], b["device"] or "-", b["verdict"], b["hint"]))'

task_id="$(curl -sf "$base/ScheduledTasks" -H "$h" | python3 -c 'import json,sys; print([t["Id"] for t in json.load(sys.stdin) if t["Key"]=="HwProbeHardwareProbe"][0])')"
echo "task run:    HTTP $(curl -s -o /dev/null -w '%{http_code}' -X POST "$base/ScheduledTasks/Running/$task_id" -H "$h")"
for _ in $(seq 1 60); do
    result="$(curl -sf "$base/ScheduledTasks/$task_id" -H "$h" | python3 -c 'import json,sys; t=json.load(sys.stdin); r=t.get("LastExecutionResult") or {}; print(t["State"], r.get("Status", "-"))')"
    case "$result" in Idle\ Completed|Idle\ Failed) break ;; esac
    sleep 2
done
echo "task result: $result"
