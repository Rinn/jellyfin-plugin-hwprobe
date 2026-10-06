#!/usr/bin/env python3
"""Adds a plugin version to a Jellyfin plugin repository manifest.

Reads the plugin's metadata from build.yaml, hashes the zip, and writes the manifest with the new
version first. An existing entry for the same version is replaced, so a re-run release is safe.

Field names follow MediaBrowser.Model/Updates/PackageInfo.cs and VersionInfo.cs (Jellyfin 12.2).
The checksum is MD5, which InstallationManager compares case-insensitively when installing.
"""

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime, timezone


def read_build_yaml(path):
    """Parses the subset of YAML that build.yaml uses: quoted scalars, folded (>) blocks and lists."""
    fields, key, folded, items = {}, None, None, None
    for number, line in enumerate(open(path, encoding="utf-8").read().splitlines(), 1):
        if line.strip() in ("", "---") or line.startswith("#"):
            continue
        if folded is not None and line.startswith("  ") and not line.startswith("- "):
            folded.append(line.strip())
            continue
        if items is not None and line.startswith("- "):
            items.append(line[2:].strip().strip('"'))
            continue
        folded = items = None
        match = re.fullmatch(r'([A-Za-z]+):\s*(.*)', line)
        if not match:
            sys.exit(f"{path}:{number}: unsupported line: {line!r}")
        key, value = match.groups()
        if value == ">":
            folded = []
            fields[key] = folded
        elif value == "":
            items = []
            fields[key] = items
        else:
            fields[key] = value.strip('"')
    return {k: " ".join(v) if k in ("description", "changelog") else v for k, v in fields.items()}


def main():
    """Builds the manifest and writes it to --out."""
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--build-yaml", required=True)
    parser.add_argument("--zip", required=True, help="The plugin zip; its MD5 goes in the manifest.")
    parser.add_argument("--version", required=True, help="Four-part version, e.g. 0.2.0.0.")
    parser.add_argument("--source-url", required=True, help="Where Jellyfin downloads the zip.")
    parser.add_argument("--changelog", default="")
    parser.add_argument("--manifest", help="Existing manifest to add to; omitted or missing starts a new one.")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", args.version):
        sys.exit(f"version {args.version!r} must have four numeric parts")

    meta = read_build_yaml(args.build_yaml)
    try:
        manifest = json.load(open(args.manifest, encoding="utf-8")) if args.manifest else []
    except FileNotFoundError:
        manifest = []

    package = next((p for p in manifest if p["guid"] == meta["guid"]), None)
    if package is None:
        package = {"guid": meta["guid"], "versions": []}
        manifest.append(package)
    package.update({k: meta[k] for k in ("name", "description", "overview", "owner", "category", "imageUrl") if k in meta})

    version = {
        "version": args.version,
        "changelog": args.changelog or meta.get("changelog", ""),
        "targetAbi": meta["targetAbi"],
        "sourceUrl": args.source_url,
        "checksum": hashlib.md5(open(args.zip, "rb").read()).hexdigest(),
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    package["versions"] = [version] + [v for v in package["versions"] if v["version"] != args.version]

    with open(args.out, "w", encoding="utf-8") as out:
        json.dump(manifest, out, indent=2)
        out.write("\n")


if __name__ == "__main__":
    main()
