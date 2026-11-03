#!/usr/bin/env python3
"""Publish self-contained launcher and server archives for a .NET runtime."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import shutil
import struct
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET
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
        version = "0.0.1-ci." + os.environ.get("GITHUB_RUN_NUMBER", "0")
    if VERSION.fullmatch(version) is None:
        raise ValueError("Expected a version such as 1.2.3 or 1.2.3-rc.1.")
    return version


def publish(project, runtime, version, destination):
    command = [
        "dotnet", "publish", str(ROOT / "src" / project / (project + ".csproj")),
        "--configuration", "Release", "--runtime", runtime, "--self-contained", "true",
        "--output", str(destination), "-p:PublishTrimmed=false", "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=none", "-p:DebugSymbols=false", "-p:ContinuousIntegrationBuild=true",
        "-p:Version=" + version,
    ]
    if project == "HAPPYLAND.Server":
        command += ["-p:StaticWebAssetsEnabled=false", "-p:PublishIISAssets=false"]
    if os.environ.get("GITHUB_SHA"):
        command.append("-p:SourceRevisionId=" + os.environ["GITHUB_SHA"])
    subprocess.run(command, cwd=ROOT, check=True)

    executable = project + (".exe" if runtime.startswith("win-") else "")
    expected = {executable, "appsettings.json"} if project == "HAPPYLAND.Server" else {executable}
    published = {file.relative_to(destination).as_posix() for file in destination.rglob("*") if file.is_file()}
    if published != expected:
        raise RuntimeError(f"Unexpected files in {project} publish output: {sorted(published)}")


def add_notices(destination, project, runtime, launcher):
    notices = [("HAPPYLAND", ROOT / "LICENSE")]
    if launcher:
        notices += [
            ("Hyperion", ROOT / "src/Hyperion/LICENSE.txt"),
            ("Fusion Pixel", ROOT / "src/HAPPYLAND.Launcher/Assets/Fonts/LICENSE-OFL.txt"),
            ("Wallpapers", ROOT / "src/HAPPYLAND.Launcher/Assets/Backgrounds/NOTICE.txt"),
        ]
    packages = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget/packages"))
    project_file = ROOT / "src" / project / (project + ".csproj")
    framework = ET.parse(project_file).findtext("./PropertyGroup/TargetFramework")
    config_file = ROOT / "src" / project / "bin" / "Release" / framework / runtime / (project + ".runtimeconfig.json")
    config = json.loads(config_file.read_text(encoding="utf-8"))
    for framework in config["runtimeOptions"]["includedFrameworks"]:
        name = framework["name"]
        package = packages / (name.lower() + ".runtime." + runtime) / framework["version"].lower()
        for file in sorted(package.iterdir()):
            if file.name.lower() in {"license.txt", "third-party-notices.txt"}:
                notices.append((name + " - " + file.name, file))
    with (destination / "LICENSES.txt").open("w", encoding="utf-8", newline="\n") as output:
        for title, file in notices:
            output.write("=== " + title + " ===\n\n")
            output.write(file.read_text(encoding="utf-8-sig").rstrip() + "\n\n")
    shutil.copy2(ROOT / "README.md", destination / "README.md")


def sign_macos(executable, entitlements):
    subprocess.run(["codesign", "--force", "--sign", "-", "--entitlements", str(entitlements), str(executable)], check=True)
    subprocess.run(["codesign", "--verify", "--strict", str(executable)], check=True)


def mac_bundle(published, workspace, runtime, version, entitlements):
    bundle = workspace / "HAPPYLAND Launcher.app"
    contents = bundle / "Contents"
    contents.mkdir(parents=True)
    macos = contents / "MacOS"
    macos.mkdir()
    executable = macos / "HAPPYLAND.Launcher"
    shutil.move(str(published / "HAPPYLAND.Launcher"), executable)
    resources = contents / "Resources"
    resources.mkdir()
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
    add_notices(resources, "HAPPYLAND.Launcher", runtime, launcher=True)
    sign_macos(executable, entitlements)
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
                published = mac_bundle(published, workspace, args.runtime, version, entitlements)
            else:
                add_notices(published, project, args.runtime, launcher=kind == "Launcher")
                if args.runtime.startswith("osx-"):
                    sign_macos(published / project, entitlements)
            archives.append(archive(published, output, name, windows=args.runtime.startswith("win-")))
    with (output / f"SHA256SUMS-{args.runtime}.txt").open("w", encoding="utf-8", newline="\n") as sums:
        for file in sorted(archives):
            with file.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            sums.write(f"{digest}  {file.name}\n")
            print(file)


if __name__ == "__main__":
    main()
