"""Import the user's local Adventure Forge ZIP; do not distribute its contents."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
import zipfile


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('archive',type=Path)
    args=parser.parse_args()
    root=Path(__file__).resolve().parents[1]
    with zipfile.ZipFile(args.archive) as outer:
        packs=[n for n in outer.namelist() if n.endswith('.AFPack')]
        if len(packs)!=1: raise ValueError('expected one AFPack')
        data=outer.read(packs[0])
        # This pack is a ZIP64 archive padded to 128 MiB. Trim only after its
        # actual end-of-central-directory and comment, preserving original entries.
        marker=data.rfind(b'PK\x05\x06')
        if marker<0: raise ValueError('no standard ZIP central directory')
        end=marker+22+struct.unpack_from('<H',data,marker+20)[0]
        records=[]
        with zipfile.ZipFile(io.BytesIO(data[:end])) as pack:
            if sum(i.file_size for i in pack.infolist())>1_000_000_000: raise ValueError('pack size limit')
            for info in pack.infolist():
                path=Path(info.filename)
                if not info.filename.startswith('Adventure Assets/') or path.suffix.lower() not in ('.png','.fbx'): continue
                relative=Path(*path.parts[1:])
                if relative.is_absolute() or '..' in relative.parts: raise ValueError('invalid asset path')
                destination=root/'Assets/Art/AdventureForge'/relative
                content=pack.read(info)
                if destination.exists() and destination.read_bytes()!=content: raise ValueError('existing asset differs: '+str(destination))
                destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(content)
                records.append(dict(archive_path=info.filename,local=str(destination.relative_to(root)),sha256=hashlib.sha256(content).hexdigest(),bytes=len(content)))
        receipts=root/'Logs/asset-import'
        receipts.mkdir(parents=True,exist_ok=True)
        (receipts/'ASSET-INVENTORY.json').write_text(json.dumps(dict(source_zip_sha256=hashlib.sha256(args.archive.read_bytes()).hexdigest(),source_url='https://adventureforge.itch.io/adventureforge-scifi-spaceship-pack',archive_padding_ignored=len(data)-end,assets=records),indent=2)+'\n')
        for name in outer.namelist():
            if name.endswith('README.txt'): (receipts/'ASSET-PACK-README.txt').write_bytes(outer.read(name))
    print(f'{len(records)} PNG/FBX imported and hashed')


if __name__=='__main__': main()
