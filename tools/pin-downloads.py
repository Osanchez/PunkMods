#!/usr/bin/env python3
"""Pin every mod.json download to a specific release, with its sha256.

    python3 tools/pin-downloads.py                  # newest release
    python3 tools/pin-downloads.py v2026.08.09.16   # a specific one
    python3 tools/pin-downloads.py --check          # verify, change nothing (CI-friendly)

WHY PINNING AND HASHING GO TOGETHER
-----------------------------------
A mod.json may point at its download either as a fixed `url`, or as `repo` +
`assetPattern` resolved against the LATEST release. The second form is convenient — a
new release is picked up with no manifest edit — but it cannot carry a `sha256`.

This pipeline rebuilds every zip on every push to main, and the zips are not
reproducible: Compress-Archive stamps file timestamps, so identical source produces a
different archive every run. Verified by downloading BepInEx-Setup.zip from two
consecutive releases whose content had not changed —

    v2026.08.08.14  76a3070fb99fe762581f9c92b081682df4a979e4644ccce0a81c4143c76f656a
    v2026.08.09.16  5f530a14d08301b18f1b6784a1e0d4c953c069e9b0e1db346471cd2ef96ed040

So a hash written against "latest" is wrong the moment anything is pushed. PUNK Nexus
BLOCKS an install on a hash mismatch, which means a stale hash is strictly worse than
no hash at all: it takes a working mod and makes it uninstallable.

Pinning the url removes the moving part. The manifest then names one immutable artifact
and the hash of exactly that artifact, and both stay true until someone deliberately
moves them — which is the same edit as bumping `version`, so it costs a release nothing.

Run this after a release when you actually want the catalog to serve it.
"""

from __future__ import annotations

import json
import sys
import urllib.request
from pathlib import Path

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


def release(tag: str | None):
    if tag:
        return fetch(f"{API}/repos/{REPO}/releases/tags/{tag}")
    return fetch(f"{API}/repos/{REPO}/releases/latest")


def main() -> int:
    args = [a for a in sys.argv[1:]]
    check_only = "--check" in args
    tag = next((a for a in args if not a.startswith("-")), None)

    rel = release(tag)
    assets = {a["name"]: a for a in rel["assets"]}
    print(f"release {rel['tag_name']} — {len(assets)} assets\n")

    changed, problems = [], []

    for manifest_path in sorted(ROOT.glob("*/mod.json")):
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        mod_id, version = manifest.get("id"), manifest.get("version")
        expected = f"{mod_id}-v{version}.zip"

        asset = assets.get(expected)
        if asset is None:
            # Not every folder is distributed, and a version bump that has not been
            # released yet is a normal in-between state, not a failure to fix here.
            problems.append(f"{mod_id}: no asset named {expected} in {rel['tag_name']}")
            continue

        # GitHub reports the digest it computed on upload, so the bytes never have to be
        # downloaded to be pinned. Format is "sha256:<hex>".
        digest = (asset.get("digest") or "")
        if not digest.startswith("sha256:"):
            problems.append(f"{mod_id}: release asset carries no sha256 digest")
            continue
        sha = digest.split(":", 1)[1]

        url = asset["browser_download_url"]
        current = manifest.get("download") or {}
        if current.get("url") == url and manifest.get("sha256") == sha:
            print(f"  = {mod_id:<28} already pinned")
            continue

        if check_only:
            problems.append(f"{mod_id}: not pinned to {rel['tag_name']}")
            continue

        manifest["download"] = {"url": url}
        manifest["sha256"] = sha
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        changed.append(mod_id)
        print(f"  + {mod_id:<28} {sha[:16]}…")

    print()
    if problems:
        print("problems:")
        for p in problems:
            print(f"  - {p}")
    if check_only:
        return 1 if problems else 0

    print(f"pinned {len(changed)} manifest(s) to {rel['tag_name']}")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
