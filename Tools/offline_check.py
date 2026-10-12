"""Run a complete public-sensor mission in a private loopback-only namespace.

No host network settings are changed. Counters cover the entire namespace.
The environment is the independent CPU geometric simulator, not PhysX.
"""
import argparse
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
PYTHON=sys.executable


def counters():
    result={}
    for row in Path('/proc/net/dev').read_text().splitlines()[2:]:
        name,values=row.split(':');v=values.split()
        result[name.strip()]=dict(rx_packets=int(v[1]),tx_packets=int(v[9]))
    return result


def request(endpoint,payload=None):
    req=urllib.request.Request(endpoint, data=None if payload is None else json.dumps(payload).encode(),
                               headers={'Content-Type':'application/json'})
    with urllib.request.urlopen(req,timeout=15) as response:return json.load(response)


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--worker',action='store_true');args=p.parse_args();args.output=args.output.resolve()
    args.output.mkdir(parents=True,exist_ok=True)
    if not args.worker:
        # Fresh user+network namespace, without sudo, host firewall or veth pair.
        command=['unshare','-Urn',sys.executable,'-B',str(Path(__file__).resolve()),'--worker','--output',str(args.output)]
        return subprocess.call(command)
    subprocess.run(['ip','link','set','lo','up'],check=True)
    before=counters()
    if set(before)!={'lo'}:raise RuntimeError('offline namespace has external interfaces')
    namespace=os.readlink('/proc/self/ns/net')
    from nav_benchmark import NavigationEnvironment
    dataset=json.loads((ROOT/'Assets/StreamingAssets/Navigation/worlds-v2.json').read_bytes())
    case=next(c for c in dataset['cases'] if c['split']=='development')
    world=NavigationEnvironment(case)
    with (args.output/'bridge.log').open('w') as log:
        brain=subprocess.Popen([str(PYTHON),'-B',str(ROOT/'Bridge/arachnid_server.py'),'--port','8771',
                                '--artifacts',str(args.output/'brain'),'--no-persistence'],stdout=log,stderr=subprocess.STDOUT)
    timings=[];result={};error=None
    try:
        for _ in range(150):
            try:request('http://127.0.0.1:8771/health');break
            except OSError:time.sleep(.1)
        else:raise RuntimeError('brain unavailable offline')
        for seq in range(320):
            obs=world.observation('offline-complete-mission',seq,'cold')
            start=time.perf_counter_ns();d=request('http://127.0.0.1:8771/navigate',obs)
            timings.append((time.perf_counter_ns()-start)/1_000_000)
            if d['action'] in ('done','wait'):break
            world.apply(d)
        else:obs=world.observation('offline-complete-mission',320,'cold')
        result=request('http://127.0.0.1:8771/navigate/finish',obs)
    except Exception as exc:error=str(exc)
    finally:
        after=counters();brain.send_signal(signal.SIGINT);brain.wait(timeout=15)
    external=sum(v['tx_packets']-before.get(k,{}).get('tx_packets',0) for k,v in after.items() if k!='lo')
    report=dict(schema='echo.offline.mission.v1',valid=not error and result.get('completed',False) and external==0,
                namespace=namespace,interfaces=list(after),before=before,after=after,
                external_transmitted_packets=external,host_network_modified=False,
                scope='Whole CPU mission: Python brain and independent geometric simulator; no Unity rendering',
                finish=result,http_decision_latency_ms=timings,error=error)
    (args.output/'offline.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k not in ('http_decision_latency_ms','finish')},indent=2))
    return 0 if report['valid'] else 1


if __name__=='__main__':raise SystemExit(main())
