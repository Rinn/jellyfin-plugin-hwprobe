#!/usr/bin/env python3
"""Publishes the plugin and CLI and packs them into release archives, byte-identical across runs.

Archives get sorted entries, fixed permissions and the commit's timestamp (SOURCE_DATE_EPOCH, else the
HEAD commit time), and gzip stores no file name or time, so the same commit always packs the same bytes.
"""

import argparse
import concurrent.futures
import gzip
import os
import re
import subprocess
import sys
import time
import zipfile

CLI_RIDS = ["linux-x64", "linux-arm64", "osx-arm64", "win-x64"]
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def run(*args):
    """Runs a command from the repo root, stopping on failure."""
    subprocess.run(args, cwd=ROOT, check=True)


def run_captured(*args):
    """Runs a command from the repo root, returning its output and raising with it on failure."""
    result = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(args)} failed:\n{result.stdout}{result.stderr}")
    return result.stdout


def source_date_epoch():
    """The archive timestamp: SOURCE_DATE_EPOCH, else the HEAD commit time."""
    if "SOURCE_DATE_EPOCH" in os.environ:
        return int(os.environ["SOURCE_DATE_EPOCH"])
    out = subprocess.run(["git", "log", "-1", "--format=%ct"], cwd=ROOT, check=True, capture_output=True, text=True)
    return int(out.stdout.strip())


def write_zip(path, files, epoch):
    """Writes a zip of (archive name, source path) pairs with fixed metadata."""
    # Zip timestamps have two-second resolution and can't predate 1980.
    stamp = time.gmtime(max(epoch, 315532800))[:6]
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, source in sorted(files):
            info = zipfile.ZipInfo(name, stamp)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            info.create_system = 3
            with open(source, "rb") as f:
                archive.writestr(info, f.read())


def write_gzip(path, source):
    """Gzips one file with no stored name or time."""
    with open(source, "rb") as f, open(path, "wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0, compresslevel=9) as out:
            out.write(f.read())


def default_version():
    """The placeholder version from Directory.Build.props, used when --version isn't given."""
    props = open(os.path.join(ROOT, "Directory.Build.props"), encoding="utf-8").read()
    match = re.search(r"<Version>([^<]+)</Version>", props)
    if not match:
        sys.exit("No <Version> in Directory.Build.props")
    return match.group(1)


def main():
    """Publishes and packs into --out."""
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", required=True, help="Directory for the archives.")
    parser.add_argument("--version", help="Three-part version, e.g. 1.2.3 (a release passes its tag). Default: Directory.Build.props.")
    parser.add_argument("--work", default=os.path.join(ROOT, "artifacts", "package"), help="Scratch directory for publish output.")
    args = parser.parse_args()

    out = os.path.abspath(args.out)
    work = os.path.abspath(args.work)
    os.makedirs(out, exist_ok=True)
    epoch = source_date_epoch()
    version = args.version or default_version()
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        sys.exit(f"version {version!r} must look like 1.2.3")
    stamp = f"-p:Version={version}"
    plugin_zip = f"hwprobe-plugin_{version}.0.zip"

    # One restore covers every publish: the CLI lists all its platforms, and SelfContained pulls their
    # runtime packs. The publishes then run in parallel without restoring; each platform builds into its
    # own obj/ and bin/ folders.
    # The restore records project versions for deps.json, so it needs the version too.
    run("dotnet", "restore", "src/HwProbe.Plugin", stamp)
    run("dotnet", "restore", "src/HwProbe.Cli", "-p:SelfContained=true", stamp)
    plugin = os.path.join(work, "plugin")
    publishes = [("dotnet", "publish", "src/HwProbe.Plugin", "-c", "Release", "--no-restore", stamp, "-o", plugin)]
    publishes += [
        ("dotnet", "publish", "src/HwProbe.Cli", "-c", "Release", "-r", rid, "--self-contained", "--no-restore", stamp, "-o", os.path.join(work, "cli", rid))
        for rid in CLI_RIDS
    ]
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(publishes)) as pool:
        for output in pool.map(lambda command: run_captured(*command), publishes):
            print(output, end="")

    # Compression is most of the remaining time; zlib releases the GIL, so the archives are written in parallel too.
    dlls = [(n, os.path.join(plugin, n)) for n in os.listdir(plugin) if n.startswith("Jellyfin.Plugin.HwProbe") and n.endswith(".dll")]
    archives = [lambda: write_zip(os.path.join(out, plugin_zip), dlls, epoch)]
    for rid in CLI_RIDS:
        target = os.path.join(work, "cli", rid)
        if rid.startswith("win"):
            exe = [("hwprobe.exe", os.path.join(target, "Jellyfin.Plugin.HwProbe.Cli.exe"))]
            archives.append(lambda rid=rid, exe=exe: write_zip(os.path.join(out, f"hwprobe-{rid}.zip"), exe, epoch))
        else:
            binary = os.path.join(target, "Jellyfin.Plugin.HwProbe.Cli")
            archives.append(lambda rid=rid, binary=binary: write_gzip(os.path.join(out, f"hwprobe-{rid}.gz"), binary))
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(archives)) as pool:
        for future in [pool.submit(write) for write in archives]:
            future.result()

    print(plugin_zip)


if __name__ == "__main__":
    main()
