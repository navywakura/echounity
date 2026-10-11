"""Locate the official ECHO-5 source; never vendor or copy its implementation."""
import importlib
import os
from pathlib import Path
import sys

PROJECT = Path(__file__).resolve().parents[1]


def source_root():
    """ECHO5_SOURCE overrides ECHOAI_SOURCE, then a sibling echoai checkout."""
    if os.environ.get("ECHO5_SOURCE"):
        return Path(os.environ["ECHO5_SOURCE"]).expanduser().resolve()
    checkout = Path(os.environ.get("ECHOAI_SOURCE", PROJECT.parent / "echoai"))
    return (checkout.expanduser() / "ECHO-5").resolve()


def load_core():
    sys.dont_write_bytecode = True
    source = source_root()
    required = [source / "src/echo5" / (name + ".py")
                for name in ("interaction", "budget", "trace", "legacy")]
    required += [source.parent / name for name in
                 ("__init__.py", "echo_arc/observe.py", "nexus0/world.py")]
    missing = [str(path) for path in required if not path.is_file()]
    if missing:
        raise RuntimeError(
            "A complete https://github.com/navywakura/echoai checkout is required. "
            "Place it beside echounity, set ECHOAI_SOURCE to that checkout, or "
            "set ECHO5_SOURCE to its ECHO-5 directory. Missing: " + ", ".join(missing))
    src = str(source / "src")
    if src not in sys.path:
        sys.path.insert(0, src)
    # echo5.legacy owns inherited module loading and aliases. Do not recreate it.
    for name in ("interaction", "budget", "trace", "legacy"):
        module = importlib.import_module("echo5." + name)
        if Path(module.__file__).resolve() != source / "src/echo5" / (name + ".py"):
            raise RuntimeError("another ECHO-5 installation is already imported")
    return source
