"""Trusted SITL adapter. Policy inputs are built explicitly from sensors/EKF.

Runs only in the new network-isolated container. Gazebo truth is exported to
the passive viewer/judge and never passed to Agent.step.
"""
import argparse
from dataclasses import asdict
import json
import math
import os
from pathlib import Path
import subprocess
import statistics
import threading
import time

from echoai.nexus0.pattern1r_v4.assets.flightrunner_survey import (
    Backend, Driver, PX4_BIN, PX4_ETC, PX4_MODELS, mavutil)
from gz.msgs10.laserscan_pb2 import LaserScan
from gz.msgs10.contacts_pb2 import Contacts
from echo_flightlab.core import Agent, Observation, ned_to_enu_mm
from echo_flightlab.telemetry import LocalFrame, SourceClock, flight_status
from echo_flightlab.physics import evidence


def atomic(path, value):
    path=Path(path); temp=path.with_suffix('.tmp')
    temp.write_text(json.dumps(value,allow_nan=False,separators=(',',':'))+'\n')
    os.replace(temp,path)


class SensorTap:
    def __init__(self, backend):
        self.backend=backend; self.scan=None; self.lock=threading.Lock(); self.collisions=0
        self.received=0; self.contact_messages=0; self.floor_contacts=0
        self.contact_sources={}
        if not backend.node.subscribe(LaserScan,'/flightlab/lidar',self.on_scan):
            raise RuntimeError('lidar subscription failed')
        if not backend.node.subscribe(Contacts,'/flightlab/contacts',self.on_contact):
            raise RuntimeError('contact subscription failed; cannot report zero contacts')

    def on_scan(self,msg):
        ms=int(msg.header.stamp.sec)*1000+msg.header.stamp.nsec//1000000
        with self.lock:
            self.scan=(ms,float(msg.angle_min),float(msg.angle_step),list(msg.ranges),float(msg.range_max))
            self.received += 1

    def on_contact(self,msg):
        # Ground contact is expected on takeoff/landing; obstacle contact isn't.
        self.contact_messages += 1
        for c in msg.contact:
            names=str(c.collision1)+str(c.collision2)
            if 'floor' not in names:
                self.collisions += 1
                self.contact_sources[names]=self.contact_sources.get(names,0)+1
            else: self.floor_contacts += 1

    def snapshot(self):
        with self.lock: return self.scan


class Flight:
    def __init__(self,args):
        self.args=args; self.out=Path('/out'); self.children=[]; self.logs=[]
        self.trace=(self.out/'decisions.jsonl').open('x',buffering=1)
        self.frames=(self.out/'frames.jsonl').open('x',buffering=1)
        self.events=(self.out/'events.jsonl').open('x',buffering=1)
        self.backend=Backend('echo_flightlab'); self.backend.model_name='flightlab_0'
        self.driver=Driver({}); self.pose=None; self.pose_ms=-1; self.yaw=None; self.attitude_ms=-1
        self.armed=None; self.px4_battery=None
        self.local_frame=LocalFrame(); self.raw_pose=None
        self.pose_clock=SourceClock(); self.attitude_clock=SourceClock()
        self.agent=Agent(args.arm,args.memory,budget_ms=args.seconds*1000,max_inspections=args.inspections)
        self.target=(0,0,1500); self.last_reason=None; self.travel_mm=0; self.last_truth=None
        self.judge_frames=[]

    def event(self,kind,**data):
        r=dict(event=kind,wall_s=round(time.monotonic(),3),**data)
        line=json.dumps(r,allow_nan=False); self.events.write(line+'\n'); print(line,flush=True)

    def start_process(self,argv,env,name,cwd=None):
        f=(self.out/(name+'.log')).open('xb'); self.logs.append(f)
        p=subprocess.Popen(argv,stdout=f,stderr=subprocess.STDOUT,env=env,cwd=cwd)
        self.children.append(p); return p

    def launch(self):
        env=dict(os.environ,GZ_SIM_RESOURCE_PATH='/scenario/models:'+PX4_MODELS,GZ_FUEL_STATIC_ONLY='1',
                 LIBGL_ALWAYS_SOFTWARE='1',GALLIUM_DRIVER='llvmpipe',LP_NUM_THREADS='1',
                 OMP_NUM_THREADS='1',OPENBLAS_NUM_THREADS='1')
        self.gz=self.start_process(['gz','sim','-s','-r','--headless-rendering','/scenario/world.sdf'],env,'gazebo')
        self.backend.start(); self.tap=SensorTap(self.backend)
        deadline=time.monotonic()+45
        while self.backend.now_ns()==0:
            if time.monotonic()>deadline or self.gz.poll() is not None: raise RuntimeError('Gazebo startup')
            time.sleep(.1)
        wd=self.out/'px4'; wd.mkdir()
        # New run-specific startup hook; source the stock hook, preserve defaults.
        parameters={'EKF2_GPS_DELAY':0,'EKF2_GPS_P_NOISE':.3,'EKF2_GPS_V_NOISE':.158,
                    'MPC_XY_CRUISE':1,'MPC_XY_VEL_MAX':1,'MIS_TAKEOFF_ALT':1.5,
                    'COM_OF_LOSS_T':1,'COM_RC_IN_MODE':4}
        (wd/'px4-rc.params').write_text('. "'+PX4_ETC+'/init.d-posix/px4-rc.params"\n'+
            ''.join('param set '+k+' '+str(v)+'\n' for k,v in parameters.items()))
        pxenv=dict(env,PX4_SYS_AUTOSTART='4001',PX4_SIM_MODEL='gz_flightlab',PX4_GZ_STANDALONE='1',
                   PX4_GZ_MODEL='flightlab',PX4_GZ_WORLD='echo_flightlab',PX4_GZ_MODEL_POSE='0,0,0,0,0,0')
        pxenv['PATH']=str(wd)+':'+env['PATH']
        self.px4=self.start_process([PX4_BIN,PX4_ETC,'-s','etc/init.d-posix/rcS','-i','0','-d','-w',str(wd)],pxenv,'px4',str(wd))
        if not self.driver.connect(45) or not self.driver.wait_ready(60): raise RuntimeError('PX4 readiness')
        # Positive end-to-end check while the landing gear rests on the floor.
        # A successful subscription alone does not prove there is a publisher.
        deadline=time.monotonic()+5
        while self.tap.floor_contacts==0 and time.monotonic()<deadline: time.sleep(.05)
        if self.tap.floor_contacts==0:
            raise RuntimeError('contact channel unverified on ground; flight not armed')
        self.event('contact_channel_verified',messages=self.tap.contact_messages,
                   floor_contacts=self.tap.floor_contacts)
        for msgid in (32,30,245,147):
            self.driver._cmd(mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL,msgid,50000)
            self.driver._ack(mavutil.mavlink.MAV_CMD_SET_MESSAGE_INTERVAL,2)
        # PX4's estimated local origin need not coincide with the ground.
        # Observe a stable window before arming and use it in observations AND
        # outgoing setpoints, rather than mistaking an offset for takeoff.
        samples=[]; previous=-1; deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            self.poll()
            if self.raw_pose is not None and self.pose_ms!=previous:
                previous=self.pose_ms; samples.append(self.raw_pose); samples=samples[-20:]
                spread=[max(p[i] for p in samples)-min(p[i] for p in samples) for i in range(3)]
                if len(samples)==20 and max(spread)<=400:
                    origin=tuple(round(statistics.median(p[i] for p in samples)) for i in range(3))
                    self.local_frame=LocalFrame(origin); self.pose=self.local_frame.relative(self.raw_pose)
                    self.event('local_origin_observed',origin_mm=origin,spread_mm=spread,samples_mm=samples)
                    break
            time.sleep(.05)
        else: raise RuntimeError('no stable observed ground reference; flight not armed')
        if not self.driver.takeoff(10) or not self.driver.arm(20): raise RuntimeError('takeoff/arm rejected')
        self.event('takeoff_accepted',parameters=parameters)
        deadline=time.monotonic()+60
        while True:
            self.poll(); self.publish('TAKEOFF',None)
            scan=self.tap.snapshot()
            if self.pose and 1200<self.pose[2]<1900 and self.yaw is not None and scan: break
            if time.monotonic()>deadline:
                self.event('takeoff_diagnostic',pose_mm=self.pose,lidar_frames=self.tap.received,
                           truth_received=self.backend.body_seen,attitude_received=self.yaw is not None)
                raise RuntimeError('takeoff altitude/sensors timeout')
            time.sleep(.05)
        for _ in range(30): self.poll(); self.setpoint(); time.sleep(.05)
        self.driver._cmd(mavutil.mavlink.MAV_CMD_DO_SET_MODE,1,6,0)
        ack=self.driver._ack(mavutil.mavlink.MAV_CMD_DO_SET_MODE,4)
        if ack != mavutil.mavlink.MAV_RESULT_ACCEPTED: raise RuntimeError('offboard rejected')
        self.event('offboard_accepted')

    def poll(self):
        for _ in range(200):
            m=self.driver.conn.recv_match(blocking=False)
            if m is None: break
            now=self.backend.now_ns()//1000000
            k=m.get_type()
            if k=='LOCAL_POSITION_NED' and self.pose_clock.update(m.time_boot_ms,now):
                self.raw_pose=ned_to_enu_mm(m.x,m.y,m.z)
                self.pose=self.local_frame.relative(self.raw_pose); self.pose_ms=self.pose_clock.stamp_ms
            elif k=='ATTITUDE' and math.isfinite(m.yaw) and self.attitude_clock.update(m.time_boot_ms,now):
                self.yaw=math.pi/2-m.yaw; self.attitude_ms=self.attitude_clock.stamp_ms
            elif k=='HEARTBEAT': self.armed=bool(m.base_mode & 128)
            elif k=='BATTERY_STATUS' and m.battery_remaining>=0: self.px4_battery=int(m.battery_remaining)

    def observation(self,drop_scan=False):
        scan=self.tap.snapshot(); now=self.backend.now_ns()//1000000
        if not self.pose or scan is None or self.yaw is None: raise RuntimeError('missing observation')
        stamp, amin, astep, ranges, maximum=scan
        if drop_scan: return Observation(now,self.pose,[],self.drop_stamp,min(self.pose_ms,self.attitude_ms))
        rays=[]
        for i,r in enumerate(ranges):
            if math.isnan(r) or r < .15: continue
            hit=math.isfinite(r) and r < maximum-.03
            r=min(r,maximum)
            a=self.yaw+amin+i*astep
            rays.append((self.pose[0]+round(1000*r*math.cos(a)),self.pose[1]+round(1000*r*math.sin(a)),hit))
        return Observation(now,self.pose,rays,stamp,min(self.pose_ms,self.attitude_ms))

    def setpoint(self):
        x,y,z=self.local_frame.absolute(self.target)
        self.driver.conn.mav.set_position_target_local_ned_send(
            int(self.backend.now_ns()//1000000)&0xffffffff,self.driver.conn.target_system,
            self.driver.conn.target_component,mavutil.mavlink.MAV_FRAME_LOCAL_NED,
            0b100111111000,y/1000,x/1000,-z/1000,0,0,0,0,0,0,0,0)

    def publish(self,phase,decision,obs=None):
        body,sim,_=self.backend.snapshot()
        if body is None: return
        pos=[round(body[i],4) for i in (1,2,3)]
        if self.last_truth: self.travel_mm+=round(math.dist(pos,self.last_truth)*1000)
        self.last_truth=pos
        grid=self.agent.grid
        f=dict(schema=1,time=round(sim/1e9,3),phase=phase,arm=self.args.arm,
               position=pos,rotation=[round(body[i],5) for i in (4,5,6)],
               estimated=[v/1000 for v in self.pose] if self.pose else None,
               target=[v/1000 for v in self.target],reason=decision['reason'] if decision else phase,
               known=[{'x':c[0]*.5,'y':c[1]*.5,'occupied':v==1} for c,v in sorted(grid.cells.items())],
               rays=[{'x':x/1000,'y':y/1000,'hit':bool(h)} for x,y,h in obs.rays[::3]] if obs else [],
               inspections=self.agent.inspections,skipped=self.agent.skips,visited=len(self.agent.visited),
               travel_m=round(self.travel_mm/1000,2),contacts=self.tap.collisions if self.tap.floor_contacts else -1,
               budget_remaining_s=max(0, self.args.seconds-(sim//1000000-(self.agent.started_ms or sim//1000000))//1000),
               px4_battery=self.px4_battery if self.px4_battery is not None else -1,
               source='Gazebo/PX4 SITL',wall_unix=time.time())
        self.judge_frames.append(dict(time=f['time'],position=pos))
        self.frames.write(json.dumps(f,allow_nan=False,separators=(',',':'))+'\n')
        atomic(self.out/'live.json',f)

    def run(self):
        self.launch(); start=self.backend.now_ns(); deadline=time.monotonic()+self.args.seconds*4+150
        last_decision=last_publish=-1; decision=None; end_reason=''
        while True:
            if time.monotonic()>deadline: raise RuntimeError('mission wall timeout')
            if self.px4.poll() is not None or self.gz.poll() is not None: raise RuntimeError('simulator exited')
            self.poll(); now=self.backend.now_ns()//1000000
            elapsed=(self.backend.now_ns()-start)/1e9
            drop=self.args.fault=='sensor_stale' and elapsed>12
            if drop and not hasattr(self,'drop_stamp'):
                self.drop_stamp=self.tap.snapshot()[0]; self.event('injected_sensor_loss')
            obs=self.observation(drop)
            if now-last_decision>=250:
                decision=self.agent.step(obs); last_decision=now
                self.trace.write(json.dumps(dict(observation=asdict(obs),decision=decision),separators=(',',':'))+'\n')
                self.target=tuple(decision['target_mm'])
                if decision['reason']!=self.last_reason:
                    self.event('decision',**decision); self.last_reason=decision['reason']
                if decision['mode']=='LAND': end_reason=decision['reason']; break
            self.setpoint()
            if now-last_publish>=200: self.publish('RETURN' if self.agent.returning else 'EXPLORE',decision,obs); last_publish=now
            time.sleep(.04)
        self.event('landing_requested',reason=end_reason)
        if not self.driver.land(5): raise RuntimeError('landing rejected')
        deadline=time.monotonic()+45
        while time.monotonic()<deadline:
            self.poll(); self.publish('LANDING',decision)
            if self.armed is False: break
            time.sleep(.2)
        body,sim,_=self.backend.snapshot()
        landed=self.armed is False and body is not None and body[3]<.5
        returned=landed and math.hypot(body[1],body[2])<.9
        self.agent.memory.save(self.out/'memory.wsp')
        physical=evidence(self.judge_frames,self.tap.contact_messages,self.tap.floor_contacts)
        status=flight_status(landed,returned,len(self.agent.visited),self.args.inspections,
                             airborne=physical['takeoff_verified'])
        result=dict(status=status,arm=self.args.arm,
                    returned_home=returned,landed=landed,reason=end_reason,
                    inspections=self.agent.inspections,skipped_inspections=self.agent.skips,
                    visited=len(self.agent.visited),known_cells=len(self.agent.grid.cells),
                    contacts=self.tap.collisions,travel_m=round(self.travel_mm/1000,3),
                    contact_messages=self.tap.contact_messages,floor_contacts=self.tap.floor_contacts,
                    contact_sources=dict(self.tap.contact_sources),
                    mission_sim_s=round((sim-start)/1e9,3),lidar_frames=self.tap.received,
                    energy='simulated time/distance only; no joules measured',api_usd=0,**physical)
        atomic(self.out/'result.json',result); self.publish(status,decision)
        self.event('result',**result)
        return 0 if landed and physical['takeoff_verified'] else 2

    def close(self):
        for p in reversed(self.children):
            if p.poll() is None:
                p.terminate()
                try: p.wait(6)
                except subprocess.TimeoutExpired: p.kill(); p.wait()
        for f in (self.trace,self.frames,self.events,*self.logs): f.close()


def main():
    p=argparse.ArgumentParser(); p.add_argument('--arm',default='memx',choices=['memx','empty','cache'])
    p.add_argument('--seconds',type=int,default=120); p.add_argument('--inspections',type=int,default=8)
    p.add_argument('--memory'); p.add_argument('--fault',choices=['none','sensor_stale'],default='none')
    f=Flight(p.parse_args())
    try: return f.run()
    except Exception as exc:
        result=dict(status='STOP_TECHNICAL',error=type(exc).__name__,detail=str(exc))
        atomic('/out/result.json',result); f.event('failure',**result); return 3
    finally: f.close()


if __name__=='__main__': raise SystemExit(main())
