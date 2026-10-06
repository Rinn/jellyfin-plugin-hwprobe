#!/usr/bin/env python3
"""Writes the third-party notices for the libraries the plugin and the CLI ship.

Reads each project's restore output (obj/project.assets.json) for the NuGet packages with runtime assemblies, and
each package's .nuspec in the NuGet cache for its licence and source repository; licences are linked, not copied. Writes THIRD-PARTY-NOTICES.md and the
plugin's libraries.json, which the Help tab lists. --check fails when either file is out of date. Versions are left out, so a package update
changes neither file unless its licence changes. A declared licence links to NuGet's page for it; a licence shipped as a file
links to the source repository's copy on its default branch, looked up through the GitHub API only for libraries the notices don't already link.
"""

import argparse
import json
import os
import re
import sys
import urllib.request
import xml.etree.ElementTree as ElementTree

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
from package import PLUGIN_LIBRARIES  # noqa: E402
PRODUCTS = [("Plugin", "src/HwProbe.Plugin"), ("CLI", "src/HwProbe.Cli")]
NOTICES = os.path.join(ROOT, "THIRD-PARTY-NOTICES.md")
LIBRARIES = os.path.join(ROOT, "src/HwProbe.Plugin/Configuration/libraries.json")


def packages(project, shipped=None):
    """Returns (id, version) of each package that puts an assembly in the project's output, from its restore output; only those whose assemblies are in shipped, when given."""
    path = os.path.join(ROOT, project, "obj", "project.assets.json")
    if not os.path.exists(path):
        sys.exit(f"{path} is missing; run dotnet restore first")
    with open(path, encoding="utf-8") as f:
        assets = json.load(f)
    target = assets["targets"]["net10.0"]
    found = []
    for key, entry in target.items():
        runtime = [name for name in entry.get("runtime", {}) if not name.endswith("_._")]
        if entry.get("type") == "package" and runtime and (shipped is None or any(os.path.basename(name) in shipped for name in runtime)):
            name, version = key.split("/")
            found.append((name, version))
    return sorted(found, key=lambda p: p[0].lower())


def nuspec(name, version):
    """Returns the licence's name and a link to it, and the package's NuGet page, from the package's .nuspec."""
    folder = os.path.join(os.path.expanduser(os.environ.get("NUGET_PACKAGES", "~/.nuget/packages")), name.lower(), version.lower())
    with open(os.path.join(folder, name.lower() + ".nuspec"), encoding="utf-8") as f:
        text = f.read()
    # Each nuspec uses its own schema namespace; matching on local names reads them all.
    metadata = next(e for e in ElementTree.fromstring(text).iter() if e.tag.endswith("metadata"))
    fields = {e.tag.split("}")[-1]: e for e in metadata}
    # The source repository rather than projectUrl, which some packages point at a wiki or marketing page.
    repository = fields["repository"].get("url") if "repository" in fields and fields["repository"].get("url") else fields["projectUrl"].text.strip()
    repository = re.sub(r"\.git$", "", repository)
    license_element = fields.get("license")
    if license_element is not None and license_element.get("type") == "expression":
        license_name = license_element.text.strip()
        license_url = f"https://licenses.nuget.org/{license_name}"
    elif license_element is not None:
        # A licence shipped as a file: named from its text when it's a common one.
        with open(os.path.join(folder, license_element.text.strip()), encoding="utf-8-sig") as f:
            body = f.read()
        license_name = "Apache-2.0" if "Apache License" in body and "Version 2.0" in body else "MIT" if "MIT License" in body else "Licence"
        # NuGet shows a packaged licence file only on a per-version page, so the repository's copy is linked instead.
        license_url = KNOWN_LICENSE_URLS.get(name) or github_license(repository) or f"https://www.nuget.org/packages/{name}/{version}/license"
    else:
        license_name = "Licence"
        license_url = fields["licenseUrl"].text.strip()
    return license_name, license_url, f"https://www.nuget.org/packages/{name}"


def known_license_urls():
    """Returns the licence link of each library the current notices file lists, so only new libraries need the network."""
    if not os.path.exists(NOTICES):
        return {}
    with open(NOTICES, encoding="utf-8") as f:
        return {name: url for name, url in re.findall(r"^\| \[([^\]]+)\]\([^)]*\) \| \[[^\]]*\]\(([^)]*)\) \|$", f.read(), re.M) if url.startswith("https://github.com/")}


def github_license(url):
    """Returns the link to the licence file on a GitHub repository's default branch, or None for other hosts."""
    match = re.match(r"https://github\.com/([^/]+/[^/]+)", url)
    if not match:
        return None
    request = urllib.request.Request(f"https://api.github.com/repos/{match.group(1)}/license", headers={"Accept": "application/vnd.github+json"})
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request) as response:
        return json.load(response)["html_url"]


KNOWN_LICENSE_URLS = known_license_urls()


def build():
    """Returns the notices file's text and the plugin's library list."""
    lines = [
        "# Third-party notices",
        "",
        "Libraries shipped in the HwProbe plugin and the hwprobe CLI. Generated by `scripts/notices.py` from the projects' NuGet restore output; the plugin's list is also on its Help tab. The CLI is self-contained, so it also carries the .NET runtime ([MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), https://github.com/dotnet/runtime). Assets are listed in `assets/NOTICE.md`.",
    ]
    libraries = []
    for product, project in PRODUCTS:
        lines += ["", f"## {product}", "", "| Library | Licence |", "|---|---|"]
        for name, version in packages(project, PLUGIN_LIBRARIES if product == "Plugin" else None):
            license_name, license_url, url = nuspec(name, version)
            lines.append(f"| [{name}]({url}) | [{license_name}]({license_url}) |")
            if product == "Plugin":
                libraries.append({"Name": name, "License": license_name, "LicenseUrl": license_url, "Url": url})
    return "\n".join(lines) + "\n", json.dumps(libraries, indent=2) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail instead of writing when a file is out of date.")
    args = parser.parse_args()
    notices, libraries = build()
    stale = []
    for path, text in ((NOTICES, notices), (LIBRARIES, libraries)):
        current = open(path, encoding="utf-8").read() if os.path.exists(path) else None
        if current == text:
            continue
        if args.check:
            stale.append(os.path.relpath(path, ROOT))
        else:
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                f.write(text)
    if stale:
        sys.exit(f"out of date: {', '.join(stale)}; run python3 scripts/notices.py")


if __name__ == "__main__":
    main()
