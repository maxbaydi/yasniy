#!/usr/bin/env bash
set -euo pipefail

RUNTIME="${1:-}"
CONFIGURATION="${CONFIGURATION:-Release}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT_DIR/native/yasn-native/yasn-native.csproj"

if [[ -z "$RUNTIME" ]]; then
  OS_NAME="$(uname -s)"
  ARCH_NAME="$(uname -m)"

  case "$OS_NAME" in
    Linux) RID_OS="linux" ;;
    Darwin) RID_OS="osx" ;;
    *)
      echo "Unsupported OS: $OS_NAME"
      exit 1
      ;;
  esac

  case "$ARCH_NAME" in
    x86_64|amd64) RID_ARCH="x64" ;;
    aarch64|arm64) RID_ARCH="arm64" ;;
    *)
      echo "Unsupported architecture: $ARCH_NAME"
      exit 1
      ;;
  esac

  RUNTIME="$RID_OS-$RID_ARCH"
fi

echo "[yasn] Publishing native toolchain ($RUNTIME)..."
dotnet publish "$PROJECT" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishTrimmed=false

PUBLISH_DIR="$ROOT_DIR/native/yasn-native/bin/$CONFIGURATION/net10.0/$RUNTIME/publish"
SOURCE_BIN="$PUBLISH_DIR/yasn"
if [[ ! -f "$SOURCE_BIN" ]]; then
  echo "Published binary not found: $SOURCE_BIN"
  exit 1
fi

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
BIN_HOME="${XDG_BIN_HOME:-$HOME/.local/bin}"
INSTALL_DIR="$DATA_HOME/yasn/toolchain/$RUNTIME"

mkdir -p "$INSTALL_DIR" "$BIN_HOME"
cp "$SOURCE_BIN" "$INSTALL_DIR/yasn"
chmod +x "$INSTALL_DIR/yasn"
ln -sf "$INSTALL_DIR/yasn" "$BIN_HOME/yasn"

echo "[yasn] Installed: $INSTALL_DIR/yasn"
echo "[yasn] Symlink:   $BIN_HOME/yasn"
"$INSTALL_DIR/yasn" version

if [[ ":$PATH:" != *":$BIN_HOME:"* ]]; then
  echo "[yasn] Add to PATH: export PATH=\"$BIN_HOME:\$PATH\""
fi

echo "[yasn] Ready."
