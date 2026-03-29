#!/bin/sh
# Install the DaisiGit CLI (dg)
# Usage: curl -fsSL https://git.daisi.ai/cli/install.sh | sh
set -e

BASE_URL="${DG_BASE_URL:-https://git.daisi.ai}"
INSTALL_DIR="${DG_INSTALL_DIR:-/usr/local/bin}"

# Detect platform
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
    Linux)  PLATFORM="linux" ;;
    Darwin) PLATFORM="osx" ;;
    *)      echo "Unsupported OS: $OS"; exit 1 ;;
esac

case "$ARCH" in
    x86_64|amd64) ARCH="x64" ;;
    arm64|aarch64) ARCH="arm64" ;;
    *)             echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

BINARY="dg-${PLATFORM}-${ARCH}"
URL="${BASE_URL}/cli/download/${BINARY}"

echo "Downloading dg for ${PLATFORM}-${ARCH}..."

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$URL" -o "$TMPDIR/dg"
elif command -v wget >/dev/null 2>&1; then
    wget -q "$URL" -O "$TMPDIR/dg"
else
    echo "Error: curl or wget is required"
    exit 1
fi

chmod +x "$TMPDIR/dg"

# Install — try with sudo if needed
if [ -w "$INSTALL_DIR" ]; then
    mv "$TMPDIR/dg" "$INSTALL_DIR/dg"
else
    echo "Installing to $INSTALL_DIR (requires sudo)..."
    sudo mv "$TMPDIR/dg" "$INSTALL_DIR/dg"
fi

echo "dg installed to $INSTALL_DIR/dg"
echo ""
echo "Get started:"
echo "  dg auth login --server $BASE_URL"
echo ""
