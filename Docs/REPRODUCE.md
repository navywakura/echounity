# Portable setup and verification

Use Unity **6000.0.84f1** (revision `78ab6fc243d5`) for the recorded editor
version. Package dependencies and their lock are in `Packages/`. Use system
Python 3; Python 3.12 is the ECHO-5 reference runtime. The bridge is stdlib-only
and does not need a virtual environment, pip installation, GPU or LLM.

Clone the official repositories as siblings:

```sh
git clone https://github.com/navywakura/echoai.git
git clone https://github.com/navywakura/echounity.git
cd echounity
python3 -B Tools/check.py --worker --python-only
python3 -B Tools/check_evidence.py --tamper-tests
```

The echoai checkout must include `ECHO-5/src/echo5/{interaction,budget,trace,legacy}.py`
and its inherited modules, including `echo_arc/observe.py` and `nexus0/world.py`.
An older ECHO-5 scaffold is insufficient: the loader fails with an explicit list
of missing files. No alternate legacy implementation is bundled here.

For another checkout layout, set one of:

```sh
export ECHOAI_SOURCE=/path/to/echoai
# Or a more specific override:
export ECHO5_SOURCE=/path/to/echoai/ECHO-5
```

`ECHO5_SOURCE` takes precedence over `ECHOAI_SOURCE`, then the sibling checkout.
The inherited `echo5.legacy` loader retains module identities. Bytecode writing
is disabled when importing that source. Keep any optional Python tooling in a
separately managed environment; no historical project environment is used.

Open the project in Unity Hub, or use the launcher on Linux:

```sh
export UNITY_EDITOR=/path/to/Unity/6000.0.84f1/Editor/Unity
./Tools/start.sh                 # bridge + NavigationLab
./Tools/start.sh --classic       # bridge + EchoStation
./Tools/start.sh --bridge-only   # bridge only
```

`UNITY_EDITOR` selects the editor executable; `Unity` on `PATH` is also accepted.
The launcher reuses the local bridge on `127.0.0.1:8770`. The shell wrapper uses
`python3`; launching `python3 -B Tools/launch.py` directly is equivalent. For a
foreground bridge, use `python3 -B Bridge/server.py`. `--port` and
`--navigation-root` support isolated developer instances; the Unity client uses
8770 by default.

Start the navigation lab in Play mode and press **Iniciar** or space. The public
copy starts without a preloaded motor checkpoint. **Adquirir modelo** selects
acquisition; start each episode to acquire evidence. **Otro mundo** selects a
development world using your locally acquired model. The historical checkpoint
is archived evidence and is not installed as the live model. **Repetir / memoria**
revisits the same world. See [USO.md](Navigation/USO.md) for controls and limits.

For the classic scene, press space to run a mission; WASD or floor clicks allow
manual movement. The public scene uses original geometric placeholders. No art
download is needed; optional authorized import is documented in [ASSETS.md](ASSETS.md).

The first two checks above are bounded CPU checks. They write fresh results
under ignored `Logs/`; they do not open Unity or run a confirmation campaign.
Historical results are under [Evidence](../Evidence/INDEX.md).

To run new Unity development verification yourself, first close this project
in the editor, then use `python3 -B Tools/check_navigation.py --integration` or
`python3 -B Tools/check.py --integration`. These commands launch Unity and create
new local runs; they were not executed as part of this publication preparation.
New CPU development runs use `python3 -B Tools/nav_benchmark.py --phase development`.
Published confirmation records are frozen; publication changes do not establish
a new confirmation or satisfy ECHO-5 closure. S6 remains later work.
