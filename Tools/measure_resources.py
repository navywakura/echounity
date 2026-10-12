"""External psutil sampler: Python RSS/CPU, kept separate from Unity rendering.

Run with system Python (psutil). This process never imports the agent.
"""
import argparse
import json
from pathlib import Path
import statistics
import time
import psutil


def summary(samples, identity):
    active=[s for s in samples if s['phase']=='active'];idle=[s for s in samples if s['phase']=='idle']
    return dict(schema='echo.resource.measurement.v1', **identity, sample_count=len(samples),
        rss_peak_sampled_bytes=max((s['rss_bytes'] for s in samples), default=0),
        rss_limit_bytes=60_000_000,
        below_60mb=bool(active) and max(s['rss_bytes'] for s in samples)<60_000_000,
        idle_cpu_percent_mean=statistics.mean(s['cpu_percent'] for s in idle) if idle else None,
        active_cpu_percent_mean=statistics.mean(s['cpu_percent'] for s in active) if active else None,
        samples=samples, scope='Python brain PID only; CPU percent = one logical core at 100%; no energy claim')


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pid',type=int,required=True);p.add_argument('--output',type=Path,required=True)
    p.add_argument('--phase',type=Path,required=True);p.add_argument('--stop',type=Path,required=True)
    p.add_argument('--interval',type=float,default=.05)
    args=p.parse_args();proc=psutil.Process(args.pid)
    identity=dict(pid=args.pid,create_time=proc.create_time(),executable=proc.exe(),
                  command=proc.cmdline(),sample_interval_seconds=args.interval)
    proc.cpu_percent();samples=[];start=time.monotonic()
    try:
        while not args.stop.exists() and proc.is_running() and proc.create_time()==identity['create_time']:
            with proc.oneshot():
                samples.append(dict(seconds=time.monotonic()-start,rss_bytes=proc.memory_info().rss,
                                    cpu_percent=proc.cpu_percent(),
                                    phase=args.phase.read_text().strip() if args.phase.exists() else 'idle'))
            time.sleep(args.interval)
    except (psutil.NoSuchProcess,psutil.ZombieProcess):
        pass
    finally:
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(summary(samples,identity),indent=2)+'\n')


if __name__=='__main__':main()
