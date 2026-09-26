#!/usr/bin/env sh
# Builds and tests the library, CLI and tests in Debug and Release (the MAUI app needs Windows/macOS or the Android workload).
set -e
cd "$(dirname "$0")"
for c in Debug Release; do
  for p in src/MidiTimecode/MidiTimecode.csproj tools/MidiTimecode.Cli/MidiTimecode.Cli.csproj tests/MidiTimecode.Tests/MidiTimecode.Tests.csproj; do
    dotnet build "$p" -c "$c" --nologo
  done
  dotnet test tests/MidiTimecode.Tests/MidiTimecode.Tests.csproj -c "$c" --no-build --nologo
done
