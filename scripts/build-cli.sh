#!/bin/bash
# Build standalone CLI binaries for all platforms
# Output: dist/dg-{platform}.{ext}

set -e

PROJECT="DaisiGit.Cli/DaisiGit.Cli.csproj"
DIST="dist"
VERSION="${1:-0.1.0}"

rm -rf "$DIST"
mkdir -p "$DIST"

echo "Building DaisiGit CLI v$VERSION..."

# Windows x64
echo "  Building win-x64..."
dotnet publish "$PROJECT" -c Release -r win-x64 -o "$DIST/win-x64" --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
cp "$DIST/win-x64/dg.exe" "$DIST/dg-win-x64.exe"

# Linux x64
echo "  Building linux-x64..."
dotnet publish "$PROJECT" -c Release -r linux-x64 -o "$DIST/linux-x64" --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
cp "$DIST/linux-x64/dg" "$DIST/dg-linux-x64"

# macOS x64
echo "  Building osx-x64..."
dotnet publish "$PROJECT" -c Release -r osx-x64 -o "$DIST/osx-x64" --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
cp "$DIST/dg-osx-x64" "$DIST/dg-osx-x64" 2>/dev/null || cp "$DIST/osx-x64/dg" "$DIST/dg-osx-x64"

# macOS ARM
echo "  Building osx-arm64..."
dotnet publish "$PROJECT" -c Release -r osx-arm64 -o "$DIST/osx-arm64" --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:EnableCompressionInSingleFile=true
cp "$DIST/osx-arm64/dg" "$DIST/dg-osx-arm64"

# NuGet tool package
echo "  Building NuGet package..."
dotnet pack "$PROJECT" -c Release -o "$DIST" -p:Version="$VERSION"

# Cleanup intermediate dirs
rm -rf "$DIST/win-x64" "$DIST/linux-x64" "$DIST/osx-x64" "$DIST/osx-arm64"

# Copy to wwwroot/cli/ for serving via the web app
WWWROOT="DaisiGit.Web/wwwroot/cli"
echo "  Copying to $WWWROOT..."
mkdir -p "$WWWROOT"
cp "$DIST/dg-win-x64.exe" "$WWWROOT/"
cp "$DIST/dg-linux-x64" "$WWWROOT/"
cp "$DIST/dg-osx-x64" "$WWWROOT/"
cp "$DIST/dg-osx-arm64" "$WWWROOT/"
cp scripts/install.sh "$WWWROOT/"
cp scripts/install.ps1 "$WWWROOT/"
echo "$VERSION" > "$WWWROOT/version.txt"

echo ""
echo "Build complete! Artifacts in $DIST/:"
ls -lh "$DIST/"
echo ""
echo "Install as dotnet tool:"
echo "  dotnet tool install -g DaisiGit.Cli --add-source ./dist"
echo ""
echo "Or download the standalone binary for your platform."
echo ""
echo "Install one-liners:"
echo "  Linux/macOS: curl -fsSL https://git.daisi.ai/cli/install.sh | sh"
echo "  Windows:     irm https://git.daisi.ai/cli/install.ps1 | iex"
