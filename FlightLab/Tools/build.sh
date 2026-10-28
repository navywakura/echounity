#!/bin/sh
set -eu
task_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
task_project=$(CDPATH= cd -- "$task_dir/../Unity" && pwd)
task_editor=${UNITY_EDITOR:-"$HOME/Unity/Hub/Editor/6000.0.84f1/Editor/Unity"}
test -x "$task_editor" || { echo 'Set UNITY_EDITOR to the Unity 6000.0.84f1 executable with Linux Build Support.'; exit 2; }
exec systemd-run --user --wait --pipe --collect --unit="echo-flightlab-build-$(date +%s%N)" --property=CPUQuota=150% --property=MemoryMax=3G --property=MemorySwapMax=0 --property=TasksMax=256 --property=RuntimeMaxSec=1200 /usr/bin/flock -n /tmp/echo58-heavy.lock "$task_editor" -batchmode -nographics -quit -projectPath "$task_project" -buildTarget linux64 -job-worker-count 1 -executeMethod FlightBuild.Build -logFile "$task_project/build.log"
