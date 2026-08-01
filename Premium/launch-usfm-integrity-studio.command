#!/bin/zsh
set -e
cd "$(dirname "$0")"
export AVALONIA_TELEMETRY_OPTOUT=1
nohup dotnet run > /tmp/usfm-integrity-studio.log 2>&1 &
disown
