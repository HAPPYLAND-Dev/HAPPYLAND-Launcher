#!/usr/bin/env python3
"""Publish self-contained launcher and server archives for a .NET runtime."""

import argparse
import hashlib
import os
from pathlib import Path
import plistlib
import re
import shutil
import struct
import subprocess
import tarfile
import tempfile
import zipfile


ROOT = Path(__file__).resolve().parent.parent
VERSION = re.compile(r"(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?")


def build_version(explicit):
    if explicit is not None:
        version = explicit
    elif os.environ.get("GITHUB_REF_TYPE") == "tag":
        tag = os.environ["GITHUB_REF_NAME"]
        if not tag.startswith("v"):
            raise ValueError("Release tags must start with v, for example v1.2.3.")
        version = tag[1:]
    else:
        version = "0.0.0-ci." + os.environ.get("GITHUB_RUN_NUMBER", "0")
    if VERSION.fullmatch(version) is None:
        raise ValueError("Expected a version such as 1.2.3 or 1.2.3-rc.1.")
    return version


def publish(project, runtime, version, destination):
    command = [
        "dotnet", "publish", str(ROOT / "src" / project / (project + ".csproj")),
        "--configuration", "Release", "--runtime", runtime, "--self-contained", "true",
        "--output", str(destination), "-p:PublishTrimmed=false", "-p:PublishSingleFile=false",
        "-p:DebugType=none", "-p:DebugSymbols=false", "-p:ContinuousIntegrationBuild=true",
        "-p:Version=" + version,
    ]
    if os.environ.get("GITHUB_SHA"):
        command.append("-p:SourceRevisionId=" + os.environ["GITHUB_SHA"])
    subprocess.run(command, cwd=ROOT, check=True)


def add_notices(destination, launcher):
    notices = destination / "Licenses"
    notices.mkdir(parents=True)
    shutil.copy2(ROOT / "LICENSE", notices / "HAPPYLAND-MIT.txt")
    if launcher:
        shutil.copy2(ROOT / "src/Hyperion/LICENSE.txt", notices / "Hyperion-MIT.txt")
        shutil.copy2(ROOT / "src/HAPPYLAND.Launcher/Assets/Fonts/LICENSE-OFL.txt", notices / "Fusion-Pixel-OFL.txt")
    shutil.copy2(ROOT / "README.md", destination / "README.md")


def is_macho(file):
    if not file.is_file() or file.is_symlink():
        return False
    with file.open("rb") as stream:
        return stream.read(4) in {
            b"\xcf\xfa\xed\xfe", b"\xfe\xed\xfa\xcf", b"\xce\xfa\xed\xfe", b"\xfe\xed\xfa\xce",
            b"\xca\xfe\xba\xbe", b"\xbe\xba\xfe\xca", b"\xca\xfe\xba\xbf", b"\xbf\xba\xfe\xca",
        }


def sign_macos(directory, executable, entitlements):
    for file in sorted(directory.rglob("*"), key=lambda item: (item == executable, str(item))):
        if not is_macho(file):
            continue
        command = ["codesign", "--force", "--sign", "-"]
        if file == executable:
            command += ["--entitlements", str(entitlements)]
        subprocess.run(command + [str(file)], check=True)
    subprocess.run(["codesign", "--verify", "--strict", str(executable)], check=True)


def mac_bundle(published, workspace, version, entitlements):
    bundle = workspace / "HAPPYLAND Launcher.app"
    contents = bundle / "Contents"
    contents.mkdir(parents=True)
    shutil.move(str(published), contents / "MacOS")
    resources = contents / "Resources"
    resources.mkdir()
    frameworks = contents / "Frameworks"
    frameworks.mkdir()
    managed = resources / "app"
    managed.mkdir()
    # Keep code and resources in their bundle locations while preserving .NET lookup paths.
    for file in list((contents / "MacOS").iterdir()):
        if file.name == "HAPPYLAND.Launcher":
            continue
        if is_macho(file):
            if file.suffix != ".dylib":
                continue
            target = frameworks / file.name
        else:
            target = managed / file.name
        shutil.move(str(file), target)
        file.symlink_to(os.path.relpath(target, file.parent), target_is_directory=target.is_dir())
    png = (ROOT / "src/HAPPYLAND.Launcher/Assets/server-icon.png").read_bytes()
    # Preserve the original 64-pixel icon in standard and Retina ICNS entries.
    entries = b"".join(kind + struct.pack(">I", len(png) + 8) + png for kind in (b"icp6", b"ic11"))
    (resources / "AppIcon.icns").write_bytes(b"icns" + struct.pack(">I", len(entries) + 8) + entries)
    with (contents / "Info.plist").open("wb") as output:
        plistlib.dump({
            "CFBundleIdentifier": "cc.happyland.launcher",
            "CFBundleName": "HAPPYLAND Launcher",
            "CFBundleDisplayName": "HAPPYLAND Launcher",
            "CFBundleExecutable": "HAPPYLAND.Launcher",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": version.split("-")[0],
            "CFBundleVersion": os.environ.get("GITHUB_RUN_NUMBER", "1"),
            "CFBundleIconFile": "AppIcon.icns",
            "NSHighResolutionCapable": True,
        }, output)
    add_notices(resources, launcher=True)
    sign_macos(contents, contents / "MacOS/HAPPYLAND.Launcher", entitlements)
    subprocess.run(["codesign", "--force", "--sign", "-", "--entitlements", str(entitlements), str(bundle)], check=True)
    subprocess.run(["codesign", "--verify", "--deep", "--strict", str(bundle)], check=True)
    return bundle


def archive(source, output, name, windows):
    destination = output / (name + (".zip" if windows else ".tar.gz"))
    if windows:
        with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as package:
            for file in sorted(source.rglob("*")):
                if file.is_file():
                    package.write(file, source.name + "/" + file.relative_to(source).as_posix())
    else:
        with tarfile.open(destination, "w:gz") as package:
            package.add(source, arcname=source.name)
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", required=True, choices=["win-x64", "linux-x64", "osx-x64", "osx-arm64"])
    parser.add_argument("--version")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts")
    args = parser.parse_args()
    version = build_version(args.version)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    archives = []
    with tempfile.TemporaryDirectory(prefix=".publish-", dir=output) as temporary:
        workspace = Path(temporary)
        entitlements = workspace / "runtime.entitlements"
        if args.runtime.startswith("osx-"):
            with entitlements.open("wb") as file:
                plistlib.dump({"com.apple.security.cs.allow-jit": True}, file)
        for project, kind in [("HAPPYLAND.Launcher", "Launcher"), ("HAPPYLAND.Server", "Server")]:
            name = f"HAPPYLAND-{kind}-{version}-{args.runtime}"
            published = workspace / name
            publish(project, args.runtime, version, published)
            if kind == "Launcher" and args.runtime.startswith("osx-"):
                published = mac_bundle(published, workspace, version, entitlements)
            else:
                add_notices(published, launcher=kind == "Launcher")
                if args.runtime.startswith("osx-"):
                    sign_macos(published, published / project, entitlements)
            archives.append(archive(published, output, name, windows=args.runtime.startswith("win-")))
    with (output / f"SHA256SUMS-{args.runtime}.txt").open("w", encoding="utf-8", newline="\n") as sums:
        for file in sorted(archives):
            with file.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            sums.write(f"{digest}  {file.name}\n")
            print(file)


if __name__ == "__main__":
    main()
