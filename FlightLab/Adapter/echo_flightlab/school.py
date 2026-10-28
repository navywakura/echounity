"""Reconstruct the declared inspection memory from recorded development flights."""
import argparse
import hashlib
import json
from pathlib import Path

from echoai.echo58.memx.memory import Memory
from echoai.echo58.wspfiles.memory import export_memory, load_memory


def reconstruct(manifest, target):
    root=Path(__file__).resolve().parents[2]
    source=json.loads(Path(manifest).read_text())
    target=Path(target)
    if target.exists(): raise ValueError('preserve existing memory; choose a new output')
    combined=Memory()
    for item in source['inputs']:
        path=root/item['path']
        if hashlib.sha256(path.read_bytes()).hexdigest()!=item['sha256']:
            raise ValueError('training source changed: '+item['path'])
        memory=load_memory(path)
        if len(memory.records)!=item['records']: raise ValueError('training record count')
        for r in memory.records:
            combined.observe(r['i'],r['s'],r['a'],r['h'],r['o'])
    provenance=source.get('provenance',{
        'source':'actual-development-inspections','scope':'same-warehouse-spatial-sectors',
        'inputs':source['inputs']})
    export_memory(combined,target,provenance=provenance)
    digest=hashlib.sha256(target.read_bytes()).hexdigest()
    if digest!=source['sha256']: raise ValueError('reconstruction differs from frozen training memory')
    return dict(status='IDENTICAL_TRAINING_MEMORY',sha256=digest,records=len(combined.records),rules=combined.rules())


if __name__=='__main__':
    p=argparse.ArgumentParser(); p.add_argument('manifest'); p.add_argument('output')
    a=p.parse_args(); print(json.dumps(reconstruct(a.manifest,a.output),indent=2))
