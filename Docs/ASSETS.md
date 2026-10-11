# Assets and optional local import

The default public project needs no downloaded art pack. Both scenes, the
procedural robot, authored materials, scripts, worlds and Unity `.meta` files
are included. `EchoStation.unity` ships with 17 amber cube placeholders. These
replace sprite visuals only; mission objects and their existing colliders are
preserved. `NavigationLab.unity` builds its original geometry at runtime.

The scene builder now creates the same placeholders whenever an optional sprite
is absent. Opening the saved scene therefore needs no import step. Rebuilding
with **ECHO → Crear estación isométrica** also works with an absent or partial
pack. This publication was checked statically for unresolved asset GUIDs and
imported/compiled with Unity in headless batch mode. No new Play-mode visual,
physics or integrated mission test was run.

Historical station captures may show **Adventure Forge — Sci-fi Spaceship**,
by **Endless Adventures Inc. / Adventure Forge**. Official sources:

- [Pack download and installation page](https://adventureforge.itch.io/adventureforge-scifi-spaceship-pack).
- [Adventure Forge terms, sections 5 and 7](https://adventureforge.games/terms-of-service).

Those pages were reviewed on 2026-10-11. Section 5 reserves rights not expressly
granted; section 7 restricts redistribution of the Services. No grant to
redistribute the raw pack assets was identified. The repository excludes all
downloaded PNG/FBX artwork, pack archives and imported third-party `.meta` files.
The GPL license for our code grants no rights to that content.

The official installation route is to download the pack from the linked page,
then use Adventure Forge's **My Forge → Content Packs** to open its `.AFPack`.
Add that pack to an Adventure using the editor's Content Packs menu. These are
the vendor's instructions for Adventure Forge, not permission for Unity use.

If you separately have permission for your intended Unity use, the existing
local-only importer accepts the original ZIP:

```sh
python3 -B Tools/import_pack.py '/path/to/Adventure Forge Content Pack - Sci-fi Spaceship.zip'
```

It writes artwork to ignored `Assets/Art/AdventureForge/` and import receipts to
ignored `Logs/asset-import/`. Then rebuild the station in Unity to use available
sprites. Rebuilding overwrites manual changes to that scene. Keep the public
placeholder scene in commits; imported art is never part of this publication.

`Docs/ASSET-INVENTORY.json` is the unchanged historical filename/hash inventory,
not a list of assets bundled here. No third-party license has been changed.
