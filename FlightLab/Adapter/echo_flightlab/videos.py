"""Record Unity replays, verify their source frames, then encode local MP4s."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

HERE=Path(__file__).resolve().parent
FLIGHTLAB=HERE.parents[1]
ROOT=FLIGHTLAB.parent
PLAYER=FLIGHTLAB/'Unity/Build/EchoFlightLab.x86_64'
CASES=(('01-memoria','memx',4,'MEMORIA-X',True),
       ('02-sin-memoria','empty',4,'SIN MEMORIA',True),
       ('03-cache','cache',4,'CACHÉ SIMPLE',True),
       ('04-obstaculo-nuevo','changed',4,'OBSTÁCULO NUEVO',True),
       ('05-perdida-lidar','sensor-loss',2,'PÉRDIDA DE LiDAR',False))


def digest(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def bounded(argv,log,ram='1536M'):
    unit='echo-flightlab-video-'+str(time.time_ns())
    cmd=['systemd-run','--user','--wait','--pipe','--collect','--unit='+unit,
         '--setenv=DISPLAY='+os.environ.get('DISPLAY',':0'),
         '--property=CPUQuota=100%','--property=MemoryMax='+ram,
         '--property=MemorySwapMax=0','--property=TasksMax=96','--property=Nice=10',
         '--property=RuntimeMaxSec=900','/usr/bin/flock','-n','/tmp/echo58-heavy.lock',*map(str,argv)]
    with Path(log).open('x') as f:
        subprocess.run(cmd,stdout=f,stderr=subprocess.STDOUT,check=True)


def probe(path):
    path=Path(path)
    report=path.with_suffix('.probe.json')
    log=path.with_suffix('.probe.log')
    if report.exists() or log.exists():
        raise FileExistsError('preserve previous probe evidence; choose a new output directory')
    bounded(['ffprobe','-v','error','-threads','1','-count_frames','-show_entries',
        'stream=codec_name,width,height,r_frame_rate,nb_read_frames,duration',
        '-of','json','-o',report,path],log,'1G')
    return json.loads(report.read_text())['streams'][0]


def verify_recording(record,source):
    r=json.loads((record/'recording.json').read_text())
    rows=[json.loads(x) for x in (record/'frames.jsonl').open()]
    known=[]
    for line in (source/'frames.jsonl').open():
        f=json.loads(line); known.append((f['time'],f['phase']))
    if r['status']!='COMPLETE' or r['frames']!=r['expectedFrames'] or len(rows)!=r['frames']:
        raise ValueError('incomplete recording')
    if len(list(record.glob('frame-*.png')))!=r['frames']: raise ValueError('missing PNG frame')
    previous=-1
    for i,row in enumerate(rows):
        index=row['sourceFrame']
        if row['frame']!=i or index<previous or not 0<=index<len(known): raise ValueError('frame order')
        stamp,phase=known[index]
        if abs(row['simulatedTime']-stamp)>.002 or row['phase']!=phase: raise ValueError('source frame mismatch')
        previous=index
    if rows[-1]['sourceFrame']!=len(known)-1: raise ValueError('recording omits the flight ending')
    return r


def main():
    p=argparse.ArgumentParser(); p.add_argument('directory',type=Path)
    p.add_argument('--flights',type=Path,default=FLIGHTLAB/'Recordings')
    a=p.parse_args(); out=a.directory.resolve(); base=a.flights.resolve()
    if out.exists(): p.error('preserve previous videos; choose a new directory')
    lock=open('/tmp/echo58-heavy.lock','a'); fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
    # Each bounded child owns the lock for its entire real lifetime, even if
    # this orchestrator is interrupted. A competing job causes a stop, no retry.
    fcntl.flock(lock,fcntl.LOCK_UN); lock.close()
    available=int(next(x.split()[1] for x in Path('/proc/meminfo').read_text().splitlines() if x.startswith('MemAvailable:')))*1024
    if available<2500*1024**2 or shutil.disk_usage(ROOT).free<3*1024**3:
        raise SystemExit('Need 2.5 GiB available RAM and 3 GiB free disk')
    if not PLAYER.exists() or not shutil.which('ffmpeg') or not shutil.which('ffprobe'):
        raise SystemExit('Build Unity and provide local ffmpeg/ffprobe first')
    out.mkdir(parents=True)
    plan=dict(fps=24,width=1280,height=800,codec='libopenh264',cases=CASES,
              player_sha256=digest(PLAYER),api_calls=0,source='recorded Gazebo/PX4 development flights')
    (out/'plan.json').write_text(json.dumps(plan,indent=2,ensure_ascii=False)+'\n')
    manifest=[]
    for name,case,speed,label,tour in CASES:
        source=base/case
        if json.loads((source/'audit.json').read_text())['status']!='PASS_IDENTICAL_REPLAY': raise ValueError('decision replay not verified')
        physical=json.loads((source/'physical-audit.json').read_text())
        if not physical['takeoff_verified'] or not physical['contact_channel_verified']: raise ValueError('physical evidence missing')
        result=json.loads((source/'result.json').read_text())
        config=json.loads((source/'configuration.json').read_text())
        states={'RETURNED_PARTIAL':'regreso parcial','MISSION_COMPLETE':'misión completa',
                'CONTINGENCY_LANDED':'aterrizaje de contingencia','LANDING_INCOMPLETE':'aterrizaje incompleto'}
        caption=label+' · '+str(result['visited'])+'/'+str(config['inspections'])+' puntos · '+states.get(result['status'],result['status'])
        record=out/(name+'-frames'); video=out/(name+'.mp4')
        print('RECORD '+name,flush=True)
        argv=[PLAYER,'-screen-fullscreen','0','-screen-width','1280','-screen-height','800',
              '-force-glcore','-job-worker-count','1','--flight-dir',source,'--record-dir',record,
              '--record-fps','24','--speed',str(speed),'--caption',caption,
              '-logFile',out/(name+'-unity.log')]
        argv+=['--tour'] if tour else ['--camera','follow']
        bounded(argv,out/(name+'-render.log'))
        receipt=verify_recording(record,source)
        print('ENCODE '+name+' '+str(receipt['frames'])+' frames',flush=True)
        bounded(['ffmpeg','-hide_banner','-loglevel','warning','-threads','1','-framerate','24',
            '-i',record/'frame-%06d.png','-c:v','libopenh264','-b:v','4500k','-pix_fmt','yuv420p',
            '-threads','1','-filter_threads','1','-g','48','-movflags','+faststart',
            '-metadata','title=ECHO Flight Lab - '+caption,video],out/(name+'-encode.log'),'1G')
        info=probe(video)
        if info['codec_name']!='h264' or (info['width'],info['height'])!=(1280,800) or int(info['nb_read_frames'])!=receipt['frames']:
            raise ValueError('encoded video differs from recording')
        bounded(['ffmpeg','-v','error','-threads','1','-i',video,'-f','null','-'],out/(name+'-decode.log'),'1G')
        row=dict(video=video.name,source=str(source.relative_to(ROOT)),speed=speed,
                 source_sha256=digest(source/'frames.jsonl'),sha256=digest(video),
                 recording=receipt,probe=info)
        manifest.append(row)
        (out/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False)+'\n')
        print('VERIFIED '+name,flush=True)
    # Same codec, rate and dimensions: concatenate the verified clips without
    # inventing frames or changing their recorded playback speeds.
    listing=out/'concat.txt'
    listing.write_text(''.join("file '"+str((out/r['video']).resolve())+"'\n" for r in manifest))
    combined=out/'ECHO-FLIGHTLAB-DEMO.mp4'
    bounded(['ffmpeg','-hide_banner','-loglevel','warning','-f','concat','-safe','0','-i',listing,
        '-c','copy','-movflags','+faststart','-metadata','title=ECHO Flight Lab - verified development replays',combined],out/'combined-encode.log','1G')
    info=probe(combined)
    if int(info['nb_read_frames'])!=sum(r['recording']['frames'] for r in manifest): raise ValueError('combined frame count')
    bounded(['ffmpeg','-v','error','-threads','1','-i',combined,'-f','null','-'],out/'combined-decode.log','1G')
    (out/'COMPLETE.json').write_text(json.dumps(dict(status='VERIFIED',combined=combined.name,
        sha256=digest(combined),probe=info,clips=len(manifest),api_calls=0),indent=2)+'\n')
    print('COMPLETE '+str(combined),flush=True)


if __name__=='__main__': main()
