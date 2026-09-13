#!/usr/bin/env python3
"""Prepare the pinned gVisor IPv4 forwarding fix without changing Go's cache."""
import base64
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import tempfile
import zipfile

MODULE = "gvisor.dev/gvisor"
VERSION = "v0.0.0-20260701204157-69c2d17aea96"
SUM = "h1:LZXOf4NwvAwUy/eI8P+Y3DUdYLyaCEDNPEjsL2OP2ro="
SOURCE_SHA = "998e0ccecd9d2274f83dcc4bad5048a448e8ace3061499f2849433184f4937d1"
PATCHED_SHA = "d3e0582dbd70f231c87010cf46e1bf068d39cb509bfac9164e1349c2a6c4c173"
IPV4 = "pkg/tcpip/network/ipv4/ipv4.go"
CACHE = "gvisor-69c2d17aea96-asura1"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def patch_forwarding(source):
    if digest(source) != SOURCE_SHA:
        raise ValueError("The gVisor IPv4 source differs from the reviewed pin")
    start = source.index(b"func (e *endpoint) forwardPacketWithRoute(")
    end = source.index(b"\n}\n", start) + 3
    body = source[start:end]
    body = body.replace(b"\th := header.IPv4(pkt.NetworkHeader().Slice())\n", b"")
    body = body.replace(b"\tttl := h.TTL()\n", b"")
    copy = b"\tnewPkt := pkt.DeepCopyForForwarding(int(route.MaxHeaderLength()))\n"
    body = body.replace(copy, b"\t// Copying can release the storage behind header slices. Keep the TTL value.\n"
                        b"\tttl := header.IPv4(pkt.NetworkHeader().Slice()).TTL()\n" + copy)
    patched = source[:start] + body + source[end:]
    if digest(patched) != PATCHED_SHA:
        raise ValueError("The gVisor forwarding patch differs from the reviewed result")
    return patched


def verified_sources(archive_path):
    # Go's h1 sum hashes sorted names and per-file SHA-256 values, independently
    # of ZIP compression and timestamps (golang.org/x/mod/sumdb/dirhash.Hash1).
    prefix = f"{MODULE}@{VERSION}/"
    files = {}
    checksum = hashlib.sha256()
    with zipfile.ZipFile(archive_path) as archive:
        for entry in sorted(archive.infolist(), key=lambda item: item.filename):
            name = entry.filename
            relative = name.removeprefix(prefix)
            if (not name.startswith(prefix) or entry.is_dir() or "\n" in name or "\\" in name
                    or ".." in PurePosixPath(relative).parts or relative in files
                    or PurePosixPath(relative).is_absolute()):
                raise ValueError("Invalid path in the pinned gVisor archive")
            data = archive.read(entry)
            checksum.update(f"{digest(data)}  {name}\n".encode())
            files[relative] = data
    if "h1:" + base64.b64encode(checksum.digest()).decode() != SUM:
        raise ValueError("The gVisor archive does not match go.sum")
    files[IPV4] = patch_forwarding(files[IPV4])
    return files


def verify_cache(cache, files):
    entries = list(cache.rglob("*"))
    if cache.is_symlink() or any(path.is_symlink() or not (path.is_file() or path.is_dir()) for path in entries):
        raise ValueError("The patched gVisor cache contains a symbolic link or special file")
    actual = {path.relative_to(cache).as_posix(): digest(path.read_bytes())
              for path in entries if path.is_file()}
    if actual != {name: digest(data) for name, data in files.items()}:
        raise ValueError("The patched gVisor cache differs from the verified sources")


def main():
    repository = Path(__file__).resolve().parent.parent
    module = repository / "native/workspace-network-gateway"
    if f"{MODULE} {VERSION} {SUM}" not in (module / "go.sum").read_text().splitlines():
        raise ValueError("Update the reviewed gVisor patch when changing its module pin")
    downloaded = json.loads(subprocess.check_output(
        ["go", "mod", "download", "-json", f"{MODULE}@{VERSION}"], cwd=module))
    files = verified_sources(downloaded["Zip"])
    cache = repository / ".deps" / CACHE
    cache.parent.mkdir(parents=True, exist_ok=True)
    if not cache.exists():
        candidate = Path(tempfile.mkdtemp(prefix=".gvisor-forwarding-", dir=cache.parent))
        try:
            for name, data in files.items():
                output = candidate / name
                output.parent.mkdir(parents=True, exist_ok=True)
                output.write_bytes(data)
                output.chmod(0o444)
            try:
                os.rename(candidate, cache)
            except OSError:
                if not cache.exists():
                    raise
                # Another build may have published this identical immutable cache.
        finally:
            if candidate.exists():
                shutil.rmtree(candidate)
    verify_cache(cache, files)
    print("Verified pinned gVisor sources and IPv4 forwarding patch.")


if __name__ == "__main__":
    main()
