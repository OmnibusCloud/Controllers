#!/usr/bin/env bash
# Consumer verification of the ParaView controller, end to end and offline from this repository:
#
#   1. packs the Model, controller and Scripts projects into a throw-away local NuGet feed,
#   2. builds a throw-away consumer that references the packages exactly as WitCloud does
#      (plus Grid and Variables from nuget.org, the controller's declared dependencies),
#      which runs the consumer-side targets: module staged to @Controllers/paraview.module/,
#      the three runtime zips fetched from the paraview-v<ver> GitHub Release, SHA-verified
#      and extracted to paraview.module/paraview/<platform>/,
#   3. asserts the resulting layout (module dll + controller.json, every platform's pvpython,
#      the embedded runner/allowlist/reader inside the assembly, the scripts),
#   4. runs the real-runtime test suite in CONSUMER MODE (OUTWIT_PARAVIEW_CONTROLLERS): the node
#      engine loads the consumer's modules and the resolver must find pvpython inside
#      paraview.module — no OUTWIT_PVPYTHON, no @Prerequisites fallback.
#
#   RuntimeTools/verify_consumer.sh [--keep] [--work <dir>] [--skip-tests]
#
# Runs on Windows (git-bash) and Linux (e.g. in a dotnet/sdk container with libgomp1 libpciaccess0
# libx11-6 libxext6 installed). Needs network access to nuget.org and github.com.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
VIS="$REPO/Visualization"
WORK="${TMPDIR:-/tmp}/paraview-consumer"
KEEP=0
SKIP_TESTS=0
while [ $# -gt 0 ]; do
  case "$1" in
    --keep) KEEP=1 ;;
    --skip-tests) SKIP_TESTS=1 ;;
    --work) WORK="$2"; shift ;;
    *) echo "unknown option $1"; exit 2 ;;
  esac
  shift
done

FEED="$WORK/feed"
CONSUMER="$WORK/consumer"
rm -rf "$FEED" "$CONSUMER"
mkdir -p "$FEED" "$CONSUMER"

echo "== 1. build Release (GeneratePackageOnBuild packs; 'dotnet pack' alone does NOT rebuild a stale Release output) and collect into $FEED"
for project in OutWit.Controller.Visualization.ParaView.Model OutWit.Controller.Visualization.ParaView OutWit.Controller.Visualization.ParaView.Scripts; do
  dotnet build "$VIS/$project/$project.csproj" -c Release --nologo -v q
  cp "$VIS/$project/bin/Release/$project".[0-9]*.nupkg "$FEED/"
done
ls -la "$FEED"/*.nupkg
VERSION="$(ls "$FEED"/OutWit.Controller.Visualization.ParaView.[0-9]*.nupkg | sed -E 's/.*ParaView\.([0-9][^/]*)\.nupkg/\1/' | head -1)"
echo "controller package version: $VERSION"

echo "== 2. consumer build (fetches the runtime assets from the paraview-v$VERSION release)"
cat > "$CONSUMER/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
cat > "$CONSUMER/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="OutWit.Controller.Visualization.ParaView" Version="$VERSION" />
    <PackageReference Include="OutWit.Controller.Visualization.ParaView.Scripts" Version="$VERSION" />
    <PackageReference Include="OutWit.Controller.Grid" Version="1.*" />
    <PackageReference Include="OutWit.Controller.Variables" Version="1.*" />
  </ItemGroup>
</Project>
EOF
echo 'Console.WriteLine("paraview-consumer");' > "$CONSUMER/Program.cs"
# An isolated global packages folder: the same package version from a previous run must not be served
# from ~/.nuget/packages instead of the freshly packed one.
export NUGET_PACKAGES="$WORK/packages"
rm -rf "$NUGET_PACKAGES"
( cd "$CONSUMER" && dotnet build Consumer.csproj -c Debug --nologo -v minimal 2>&1 | tail -25 )

OUT="$CONSUMER/bin/Debug/net10.0"
MODULE="$OUT/@Controllers/paraview.module"
echo "== 3. layout"
fail=0
check() { if [ -e "$2" ]; then echo "  OK   $1"; else echo "  FAIL $1 ($2)"; fail=1; fi; }
check "module dll"             "$MODULE/OutWit.Controller.Visualization.ParaView.dll"
check "model dll"              "$MODULE/OutWit.Controller.Visualization.ParaView.Model.dll"
check "controller.json"        "$MODULE/controller.json"
check "grid module"            "$OUT/@Controllers/grid.module"
check "variables module"       "$OUT/@Controllers/variables.module"
check "pvpython windows-x64"   "$MODULE/paraview/windows-x64/bin/pvpython.exe"
check "pvpython linux-x64"     "$MODULE/paraview/linux-x64/bin/pvpython"
check "pvpython-real linux"    "$MODULE/paraview/linux-x64/bin/pvpython-real"
check "mesa linux"             "$MODULE/paraview/linux-x64/lib/mesa/libOSMesa.so.8"
check "pvpython macos-arm64"   "$MODULE/paraview/macos-arm64/ParaView-6.1.1.app/Contents/bin/pvpython"
check "licenses windows"       "$MODULE/paraview/windows-x64/share/licenses"
check "licenses linux"         "$MODULE/paraview/linux-x64/share/licenses"
check "licenses macos"         "$MODULE/paraview/macos-arm64/ParaView-6.1.1.app/Contents/Resources/licenses"
for resource in runner/render_task.py allowlists/paraview-6.1.json plugins/omnibuscloud_frd_reader.py; do
  if grep -q "$resource" "$MODULE/OutWit.Controller.Visualization.ParaView.dll"; then echo "  OK   embedded $resource"; else echo "  FAIL embedded $resource"; fail=1; fi
done
SCRIPTS="$(find "$OUT" -name "RenderParaViewFrames.wit" | head -1)"
if [ -n "$SCRIPTS" ]; then echo "  OK   scripts staged ($(dirname "$SCRIPTS"))"; else echo "  WARN scripts not found under $OUT (the Scripts package stages on first publish, ADR-001)"; fi
du -sh "$MODULE/paraview/"* 2>/dev/null | sed 's/^/  /'
[ $fail -eq 0 ] || { echo "layout assertions FAILED"; exit 1; }

if [ $SKIP_TESTS -eq 1 ]; then echo "== tests skipped"; exit 0; fi
echo "== 4. real-runtime tests in consumer mode"
export OUTWIT_PARAVIEW_CONTROLLERS="$OUT/@Controllers"
unset OUTWIT_PVPYTHON || true
unset NUGET_PACKAGES
dotnet test "$VIS/OutWit.Controller.Visualization.ParaView.Tests/OutWit.Controller.Visualization.ParaView.Tests.csproj" -c Debug --nologo -v q --filter "Category=RealRuntime" 2>&1 | tail -5

if [ $KEEP -eq 0 ]; then rm -rf "$FEED"; fi
echo "== consumer verification done (work: $WORK)"
