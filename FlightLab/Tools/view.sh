#!/bin/sh
set -eu
task_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
task_player="$task_dir/../Unity/Build/EchoFlightLab.x86_64"
test -x "$task_player" || { echo 'Build the Linux viewer first: sh FlightLab/Tools/build.sh'; exit 2; }
task_unit="echo-flightlab-view-$(date +%s%N)"
exec systemd-run --user --unit="$task_unit" --setenv="DISPLAY=${DISPLAY:-:0}" --property=CPUQuota=100% --property=MemoryMax=1536M --property=MemorySwapMax=0 --property=TasksMax=96 --property=Nice=10 --collect "$task_player" -screen-fullscreen 0 -screen-width 1280 -screen-height 800 -force-glcore -job-worker-count 1 "$@"
