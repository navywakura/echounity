#!/usr/bin/env python3
"""Launch the local prototype with the installed Unity Editor; no cloud services."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LOGS = ROOT / "Logs"


def editor_path():
    configured = os.environ.get("UNITY_EDITOR")
    if configured:
        candidate = Path(configured).expanduser()
        if candidate.is_file():
            return str(candidate)
        raise SystemExit("UNITY_EDITOR must point to the Unity executable.")
    installed = Path.home() / "Unity/Hub/Editor/6000.0.84f1/Editor/Unity"
    if installed.is_file():
        return str(installed)
    found = shutil.which("Unity") or shutil.which("unity")
    if found:
        return found
    raise SystemExit("Install Unity 6000.0.84f1 or set UNITY_EDITOR to its executable.")


def running_editor():
    """Check this project only. Never stop a different editor or research process."""
    proc = Path("/proc")
    if proc.is_dir():
        for item in proc.iterdir():
            if not item.name.isdigit():
                continue
            try:
                args = (item / "cmdline").read_bytes().split(b"\0")
                if b"-projectPath" in args and os.fsencode(ROOT) in args:
                    return int(item.name)
            except (OSError, ValueError):
                pass
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("verify", "smoke", "open"))
    parser.add_argument("--seed", type=int, default=72831)
    args = parser.parse_args()
    if not -(2**31) <= args.seed < 2**31:
        parser.error("seed must fit a signed 32-bit integer")
    active = running_editor()
    if active:
        raise SystemExit(f"This project is already open (PID {active}). Close that editor before this command.")
    LOGS.mkdir(exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    out = LOGS / f"{args.mode}-{stamp}"
    out.mkdir()
    command = [editor_path(), "-projectPath", str(ROOT), "-job-worker-count", "2",
               "-logFile", str(out / "editor.log")]
    scratch = None
    if args.mode == "verify":
        (LOGS / "verification.json").unlink(missing_ok=True)
        command += ["-batchmode", "-nographics", "-executeMethod", "EchoWorld.Editor.PrototypeEditor.Verify"]
    else:
        if sys.platform.startswith("linux") and not (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
            raise SystemExit("Open/smoke requires a desktop display; use verify for headless checks.")
        command += ["-executeMethod", "EchoWorld.Editor.PrototypeEditor.Open", "--world-seed", str(args.seed)]
        if args.mode == "smoke":
            scratch = tempfile.TemporaryDirectory(prefix="echo-world-smoke-")
            command += ["--world-smoke", "--world-save-root", scratch.name,
                        "--world-check-output", str(out)]
    with (out / "launcher.log").open("wb") as log:
        process = subprocess.Popen(command, cwd=ROOT, stdin=subprocess.DEVNULL,
                                   stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    (out / "launch.json").write_text(json.dumps({"pid": process.pid, "mode": args.mode,
        "seed": args.seed, "log": str(out / "editor.log"), "utc": stamp}, indent=2) + "\n")
    print(f"Unity PID {process.pid}; {out}", flush=True)
    if args.mode == "open":
        return 0
    try:
        result = process.wait(timeout=480)
    except subprocess.TimeoutExpired:
        process.terminate()
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
        print("Unity check timed out; inspect editor.log. No passing result inferred.", file=sys.stderr)
        return 124
    finally:
        if scratch:
            scratch.cleanup()
    receipt = LOGS / "verification.json" if args.mode == "verify" else out / "play-smoke.json"
    if result or not receipt.is_file():
        print(f"Check failed (exit {result}); inspect {out / 'editor.log'}", file=sys.stderr)
        return result or 1
    data = json.loads(receipt.read_text())
    print(json.dumps(data, ensure_ascii=False, indent=2))
    if args.mode == "verify":
        shutil.copyfile(receipt, out / "verification.json")
    return 0 if data.get("valid") else 1


if __name__ == "__main__":
    sys.exit(main())
