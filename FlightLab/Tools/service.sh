#!/bin/sh
# Bounded, sequential simulation supervisor; no hardware endpoint.
set -eu
task_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
task_name="echo-flightlab-$(date +%s%N)"
set -- --user --unit="$task_name" --property=CPUQuota=150% --property=MemoryMax=2300M --property=MemorySwapMax=0 --property=TasksMax=220 --property=Nice=10 --property=RuntimeMaxSec=1500 --property="ExecStopPost=/bin/sh $task_dir/run.sh cleanup --container $task_name" --setenv="ECHOAI_SOURCE=${ECHOAI_SOURCE:-$task_dir/../../../echoai}" --collect /bin/sh "$task_dir/run.sh" run --container "$task_name" "$@"
if [ "${FLIGHTLAB_WAIT:-0}" = 1 ]; then
    set -- --wait --pipe "$@"
fi
exec systemd-run "$@"
