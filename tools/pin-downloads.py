#!/usr/bin/env python3
"""Point every mod.json at one specific release asset, and publish that file's sha256.

CI runs this automatically after each build (see .github/workflows), so the catalog's
checksums track every release without anyone remembering to refresh them. The manual
forms are for fixing things up by hand:

    python3 tools/pin-downloads.py                          # newest release, via the API
    python3 tools/pin-downloads.py v2026.08.09.16           # a specific release, via the API
    python3 tools/pin-downloads.py --dist dist --tag v1.2.3 # hash local zips (what CI uses)
    python3 tools/pin-downloads.py --check                  # verify only, no writes

WHAT THE HASH IS FOR
--------------------
It pins the identity of a *published artifact*. Once a zip is on a release, the manifest
says "this exact file and no other", so a file swapped at the download URL after the fact
is caught and refused rather than installed. That is the whole job.

WHY THE URL IS PINNED TOO
-------------------------
A mod.json may name its download as a fixed `url`, or as `repo` + `assetPattern` resolved
against the LATEST release. The second form cannot carry a checksum, because the thing it
points at is allowed to change underneath it.

That matters here because every push rebuilds every zip, and the zips are not
reproducible - Compress-Archive stamps timestamps, so identical source produces different
bytes each run. Verified by downloading BepInEx-Setup.zip from two consecutive releases
whose content had not changed:

    v2026.08.08.14  76a3070fb99fe762581f9c92b081682df4a979e4644ccce0a81c4143c76f656a
    v2026.08.09.16  5f530a14d08301b18f1b6784a1e0d4c953c069e9b0e1db346471cd2ef96ed040

So "latest" plus a hash is a contradiction: the hash names one build while the pointer
follows another. PUNK Nexus BLOCKS an install on a mismatch, which turns a working mod
into an uninstallable one.

Pinning both together removes the contradiction. During a build the manifests still name
the PREVIOUS release and its hash - an immutable asset that GitHub keeps forever - so the
catalog is never inconsistent, it just lags by the length of one CI job.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

# CI runs this on windows-latest, where Python's stdout defaults to cp1252 and any non-ASCII
# character raises UnicodeEncodeError mid-print. Force UTF-8 so output can never fail the build.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = "Osanchez/PunkMods"
ROOT = Path(__file__).resolve().parent.parent
API = "https://api.github.com"


def fetch(url: str):
    req = urllib.request.Request(url, headers={
        "Accept": "application/vnd.github+json",
        "User-Agent": "pin-downloads",
    })
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def asset_url(tag: str, name: str) -> str:
    """Release asset URLs are formed the same way every time, so a local run needs no API."""
    return f"https://github.com/{REPO}/releases/download/{tag}/{name}"


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def targets_from_dist(dist: Path, tag: str) -> dict[str, tuple[str, str]]:
    """{asset name: (url, sha256)} hashed from the zips this build just produced."""
    out = {}
    for zip_path in sorted(dist.glob("*.zip")):
        out[zip_path.name] = (asset_url(tag, zip_path.name), sha256_of(zip_path))
    return out


def targets_from_release(tag: str | None) -> tuple[str, dict[str, tuple[str, str]]]:
    """{asset name: (url, sha256)} taken from the digests GitHub computed on upload."""
    rel = fetch(f"{API}/repos/{REPO}/releases/tags/{tag}" if tag
                else f"{API}/repos/{REPO}/releases/latest")
    out = {}
    for a in rel["assets"]:
        digest = a.get("digest") or ""
        if digest.startswith("sha256:"):
            out[a["name"]] = (a["browser_download_url"], digest.split(":", 1)[1])
    return rel["tag_name"], out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("tag_positional", nargs="?", metavar="TAG")
    ap.add_argument("--tag")
    ap.add_argument("--dist", type=Path, help="hash these local zips instead of calling the API")
    ap.add_argument("--check", action="store_true", help="report drift, write nothing")
    args = ap.parse_args()

    tag = args.tag or args.tag_positional

    if args.dist:
        if not tag:
            print("--dist needs --tag (the release the zips will be published under)")
            return 2
        targets = targets_from_dist(args.dist, tag)
        source = f"{args.dist} -> {tag}"
    else:
        tag, targets = targets_from_release(tag)
        source = tag

    print(f"pinning against {source} - {len(targets)} asset(s)\n")

    changed, problems = [], []

    for manifest_path in sorted(ROOT.glob("*/mod.json")):
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        mod_id, version = manifest.get("id"), manifest.get("version")
        expected = f"{mod_id}-v{version}.zip"

        target = targets.get(expected)
        if target is None:
            # A folder that is not distributed, or a version bumped but not yet released, is a
            # normal in-between state rather than something to fix here.
            problems.append(f"{mod_id}: no asset named {expected}")
            continue

        url, sha = target
        current = manifest.get("download") or {}
        if current.get("url") == url and manifest.get("sha256") == sha:
            print(f"  = {mod_id:<28} already pinned")
            continue

        if args.check:
            problems.append(f"{mod_id}: not pinned to {tag}")
            continue

        manifest["download"] = {"url": url}
        manifest["sha256"] = sha
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        changed.append(mod_id)
        print(f"  + {mod_id:<28} {sha[:16]}...")

    print()
    if problems:
        print("problems:")
        for p in problems:
            print(f"  - {p}")

    if args.check:
        return 1 if problems else 0

    print(f"pinned {len(changed)} manifest(s) to {tag}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
