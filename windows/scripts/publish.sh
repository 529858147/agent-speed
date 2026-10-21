#!/usr/bin/env bash
# Builds self-contained single-file Agent Speed for Windows (x64 and ARM64) from macOS, Linux or WSL.
# Output: dist/AgentSpeed-Windows-x64.zip and dist/AgentSpeed-Windows-arm64.zip, each containing AgentSpeed.exe.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/windows/src/AgentSpeed/AgentSpeed.csproj"
DIST="$ROOT/dist"

VERSION="${1:-${AGENT_SPEED_VERSION:-}}"
if [[ -z "$VERSION" && -f "$ROOT/VERSION" ]]; then
  VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
fi
VERSION="${VERSION#v}"
VERSION="${VERSION:-1.0.0}"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
  echo "error: invalid version '$VERSION'" >&2
  exit 1
fi
FILE_VERSION="${VERSION%%[-+]*}"

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
  export PATH="$HOME/.dotnet:$PATH"
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
fi
command -v dotnet >/dev/null 2>&1 || { echo "error: the .NET 10 SDK (dotnet) is required" >&2; exit 1; }
command -v zip >/dev/null 2>&1 || { echo "error: zip is required" >&2; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/agentspeed-publish.XXXXXX")"
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

# Uncompressed single-file starts ~600 ms faster and uses about half the private memory (no in-memory
# decompression); the zip is ~60 MB either way. AGENT_SPEED_COMPRESS=1 shrinks the exe to ~65 MB instead of ~150 MB.
COMPRESS=false
[[ "${AGENT_SPEED_COMPRESS:-0}" == "1" ]] && COMPRESS=true

mkdir -p "$DIST"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

for ARCH in x64 arm64; do
  RID="win-$ARCH"
  OUT="$WORK/$RID"
  echo "==> Publishing Agent Speed $VERSION for $RID"
  dotnet publish "$PROJECT" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    --artifacts-path "$WORK/artifacts" \
    -o "$OUT" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile="$COMPRESS" \
    -p:PublishTrimmed=false \
    -p:PublishReadyToRun=true \
    -p:DebugType=none \
    -p:DebugSymbols=false \
    -p:GenerateDocumentationFile=false \
    -p:Version="$FILE_VERSION" \
    -p:InformationalVersion="$VERSION" \
    -p:ContinuousIntegrationBuild=true \
    -nologo

  EXE="$OUT/AgentSpeed.exe"
  [[ -f "$EXE" ]] || { echo "error: $EXE was not produced" >&2; exit 1; }

  ZIP="$DIST/AgentSpeed-Windows-$ARCH.zip"
  rm -f "$ZIP"
  (cd "$OUT" && zip -q -X -9 "$ZIP" AgentSpeed.exe)
  SIZE=$(wc -c < "$EXE" | tr -d ' ')
  echo "    AgentSpeed.exe: $((SIZE / 1024 / 1024)) MiB ($SIZE bytes) -> ${ZIP#"$ROOT"/}"
done

echo "==> Done"
