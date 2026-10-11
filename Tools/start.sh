#!/usr/bin/env bash
set -euo pipefail
station_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$station_root/Tools/launch.py" "$@"
