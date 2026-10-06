#!/usr/bin/env python3
"""Writes the third-party notices for the libraries the plugin and the CLI ship; package.py puts them in each archive.

Reads each project's restore output (obj/project.assets.json) for the NuGet packages with runtime assemblies, and each package's .nuspec in the
NuGet cache. Each library is listed with its version, declared licence, and licence text: the licence file the package ships, else the source
repository's licence file at the commit the package was built from (the .nuspec's repository commit), fetched through the GitHub API. The
self-contained CLI also carries the .NET runtime, whose licence and notices come from its runtime pack.
"""

import argparse
import base64
import functools
import json
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ElementTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
from package import PLUGIN_LIBRARIES  # noqa: E402
NUGET = os.path.expanduser(os.environ.get("NUGET_PACKAGES", "~/.nuget/packages"))


def assets(project):
    """Returns the project's restore output."""
    path = os.path.join(ROOT, project, "obj", "project.assets.json")
    if not os.path.exists(path):
        sys.exit(f"{path} is missing; run dotnet restore first")
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def packages(project, shipped=None):
    """Returns (id, version) of each package that puts an assembly in the project's output; only those whose assemblies are in shipped, when given."""
    found = []
    for key, entry in assets(project)["targets"]["net10.0"].items():
        runtime = [name for name in entry.get("runtime", {}) if not name.endswith("_._")]
        if entry.get("type") == "package" and runtime and (shipped is None or any(os.path.basename(name) in shipped for name in runtime)):
            name, version = key.split("/")
            found.append((name, version))
    return sorted(found, key=lambda p: p[0].lower())


def runtime_pack(project, rid):
    """Returns the folder of the .NET runtime pack a self-contained publish for rid carries."""
    for dependency in assets(project)["project"]["frameworks"]["net10.0"].get("downloadDependencies", []):
        if dependency["name"].lower() == f"microsoft.netcore.app.runtime.{rid}":
            return os.path.join(NUGET, dependency["name"].lower(), dependency["version"].strip("[]").split(",")[0].strip())
    sys.exit(f"no .NET runtime pack for {rid} in {project}'s restore output; restore with -p:SelfContained=true")


def read(path):
    """Returns a text file's contents, without a byte order mark or trailing whitespace."""
    with open(path, encoding="utf-8-sig") as f:
        return f.read().rstrip()


@functools.cache
def repository_license(repository, commit):
    """Returns the licence file's text in a GitHub repository at a commit, or None for other hosts."""
    match = re.match(r"https://github\.com/([^/]+/[^/]+?)(?:\.git)?/?$", repository)
    if not match:
        return None
    request = urllib.request.Request(f"https://api.github.com/repos/{match.group(1)}/license?ref={commit}", headers={"Accept": "application/vnd.github+json"})
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request) as response:
        return base64.b64decode(json.load(response)["content"]).decode("utf-8-sig").rstrip()


def library(name, version):
    """Returns a library's notice fields from its .nuspec: name, version, NuGet page, declared licence, its link, and the licence text."""
    folder = os.path.join(NUGET, name.lower(), version.lower())
    # Each nuspec uses its own schema namespace; matching on local names reads them all.
    metadata = next(e for e in ElementTree.fromstring(read(os.path.join(folder, name.lower() + ".nuspec"))).iter() if e.tag.endswith("metadata"))
    fields = {e.tag.split("}")[-1]: e for e in metadata}
    page = f"https://www.nuget.org/packages/{name}/{version}"
    license_element = fields.get("license")
    repository = fields.get("repository")
    if license_element is not None and license_element.get("type") == "file":
        text = read(os.path.join(folder, license_element.text.strip()))
        license_name = "Apache-2.0" if "Apache License" in text and "Version 2.0" in text else "MIT" if "MIT License" in text else "See text"
        license_url = f"{page}/license"
    else:
        text = repository_license(repository.get("url", ""), repository.get("commit", "")) if repository is not None else None
        license_name = license_element.text.strip() if license_element is not None else "See text"
        license_url = f"https://licenses.nuget.org/{license_name}" if license_element is not None else fields["licenseUrl"].text.strip()
    if not text:
        sys.exit(f"no licence text for {name} {version}: the package ships none and its repository isn't on GitHub")
    return {"Name": name, "Version": version, "Url": page, "License": license_name, "LicenseUrl": license_url, "Text": text}


def notices(title, libraries, extra="", sections=()):
    """Returns a notices file listing the libraries, then each one's licence text, then any further (heading, text) sections."""
    lines = [f"# Third-party notices: {title}", "", "| Library | Version | Declared licence |", "|---|---|---|"]
    lines += [f"| [{lib['Name']}]({lib['Url']}) | {lib['Version']} | [{lib['License']}]({lib['LicenseUrl']}) |" for lib in libraries]
    if extra:
        lines += ["", extra]
    texts = {}
    for lib in libraries:
        texts.setdefault(lib["Text"], []).append(f"{lib['Name']} {lib['Version']}")
    for heading, text in [(", ".join(names), text) for text, names in texts.items()] + list(sections):
        lines += ["", f"## {heading}", "", "```", text, "```"]
    return "\n".join(lines) + "\n"


def write(path, text):
    """Writes a file with LF line endings."""
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def main():
    """Writes the plugin's notices and Help tab list, and the CLI's notices for each runtime identifier, into --out."""
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", required=True, help="Directory for plugin/ and cli-<rid>/ notices.")
    parser.add_argument("--rid", action="append", default=[], help="A CLI runtime identifier to write notices for; repeatable.")
    args = parser.parse_args()

    plugin = [library(name, version) for name, version in packages("src/HwProbe.Plugin", PLUGIN_LIBRARIES)]
    os.makedirs(os.path.join(args.out, "plugin"), exist_ok=True)
    write(os.path.join(args.out, "plugin", "THIRD-PARTY-NOTICES.md"), notices("HwProbe plugin", plugin))
    help_list = [{k: lib[k] for k in ("Name", "Version", "Url", "License", "LicenseUrl")} for lib in plugin]
    write(os.path.join(args.out, "plugin", "libraries.json"), json.dumps(help_list, indent=2) + "\n")

    cli = [library(name, version) for name, version in packages("src/HwProbe.Cli")]
    for rid in args.rid:
        pack = runtime_pack("src/HwProbe.Cli", rid)
        version = os.path.basename(pack)
        runtime = f"The CLI is self-contained, so it also carries the .NET runtime {version}. Its licence follows; the libraries the runtime itself includes are listed in DOTNET-THIRD-PARTY-NOTICES.txt."
        folder = os.path.join(args.out, f"cli-{rid}")
        os.makedirs(folder, exist_ok=True)
        write(os.path.join(folder, "THIRD-PARTY-NOTICES.md"), notices("hwprobe CLI", cli, runtime, [(f".NET runtime {version}", read(os.path.join(pack, "LICENSE.TXT")))]))
        write(os.path.join(folder, "DOTNET-THIRD-PARTY-NOTICES.txt"), read(os.path.join(pack, "THIRD-PARTY-NOTICES.TXT")) + "\n")


if __name__ == "__main__":
    main()
