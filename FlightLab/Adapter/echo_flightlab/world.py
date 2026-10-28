"""Shared scene authoring for Gazebo and passive Unity visualization."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent


def scene(variant='warehouse'):
    if variant not in ('warehouse', 'changed', 'empty'): raise ValueError('world variant')
    boxes = []
    def box(name, pos, size, color, kind='obstacle'):
        boxes.append(dict(name=name, position=pos, size=size, color=color, kind=kind))
    box('floor', [0,4,-.15], [20,18,.3], [.24,.29,.31], 'floor')
    box('west', [-9,4,2], [.3,16,4], [.62,.68,.68], 'wall')
    box('east', [9,4,2], [.3,16,4], [.62,.68,.68], 'wall')
    box('south', [0,-4,2], [18,.3,4], [.62,.68,.68], 'wall')
    box('north', [0,12,2], [18,.3,4], [.62,.68,.68], 'wall')
    if variant != 'empty':
        box('rack_west', [-4.5,4,1.3], [1.5,5,2.6], [.17,.36,.42], 'rack')
        box('rack_east', [4.5,5,1.3], [1.5,4,2.6], [.17,.36,.42], 'rack')
        box('crate', [0,6,1.1], [1.6,1.6,2.2], [.68,.39,.16], 'crate')
        box('partition_left', [-5,9,1.6], [6,.25,3.2], [.45,.52,.52], 'wall')
        box('partition_right', [5,9,1.6], [6,.25,3.2], [.45,.52,.52], 'wall')
    if variant == 'changed':
        box('new_barrier', [0,3,1.1], [2.5,.5,2.2], [.85,.38,.13], 'changed')
    return dict(schema=1, name='ECHO Flight Lab', variant=variant, boxes=boxes,
                home=[0,0,0], bounds=[-8,-2.5,8,10.5], altitude=1.5,
                units='metres', coordinates='ENU', model='X500',
                truth_only=True, physics='Gazebo Harmonic / PX4 1.15.4')


def sdf(data):
    root = ET.Element('sdf', version='1.9')
    w = ET.SubElement(root, 'world', name='echo_flightlab')
    p = ET.SubElement(w, 'physics', name='physics', type='ignored')
    ET.SubElement(p,'max_step_size').text = '0.004'
    ET.SubElement(p,'real_time_factor').text = '1'
    for name, cls in [('physics','Physics'),('user-commands','UserCommands'),
                      ('scene-broadcaster','SceneBroadcaster'),('imu','Imu'),
                      ('air-pressure','AirPressure'),('navsat','NavSat'),('contact','Contact')]:
        ET.SubElement(w,'plugin', filename='gz-sim-'+name+'-system',name='gz::sim::systems::'+cls)
    sensors=ET.SubElement(w,'plugin',filename='gz-sim-sensors-system',name='gz::sim::systems::Sensors')
    ET.SubElement(sensors,'render_engine').text='ogre2'
    ET.SubElement(w,'magnetic_field').text='6e-06 2.3e-05 -4.2e-05'
    ET.SubElement(w,'gravity').text='0 0 -9.80665'
    sp=ET.SubElement(w,'spherical_coordinates')
    for k,v in dict(surface_model='EARTH_WGS84',world_frame_orientation='ENU',
                    latitude_deg='47.397971057728974',longitude_deg='8.546163739800146',elevation='0').items():
        ET.SubElement(sp,k).text=v
    light=ET.SubElement(w,'light',name='sun',type='directional')
    ET.SubElement(light,'pose').text='0 0 10 0 0 0'
    ET.SubElement(light,'diffuse').text='.8 .8 .8 1'
    ET.SubElement(light,'direction').text='-.3 .2 -1'
    for b in data['boxes']:
        m=ET.SubElement(w,'model',name=b['name']); ET.SubElement(m,'static').text='true'
        ET.SubElement(m,'pose').text=' '.join(map(str,b['position']))+' 0 0 0'
        l=ET.SubElement(m,'link',name='body')
        for tag in ('collision','visual'):
            v=ET.SubElement(l,tag,name=tag); g=ET.SubElement(v,'geometry')
            ET.SubElement(ET.SubElement(g,'box'),'size').text=' '.join(map(str,b['size']))
            if tag=='visual':
                mat=ET.SubElement(v,'material')
                for c in ('ambient','diffuse'): ET.SubElement(mat,c).text=' '.join(map(str,b['color']))+' 1'
    return ET.tostring(root, encoding='unicode')


SENSORS = '''
<sensor name="flightlab_lidar" type="gpu_lidar">
 <pose>0 0 .28 0 0 0</pose><topic>/flightlab/lidar</topic>
 <always_on>1</always_on><update_rate>8</update_rate>
 <lidar><scan><horizontal><samples>180</samples><resolution>1</resolution>
 <min_angle>-3.14159265</min_angle><max_angle>3.10668607</max_angle></horizontal></scan>
 <range><min>0.15</min><max>6</max><resolution>0.01</resolution></range>
 <noise><type>gaussian</type><mean>0</mean><stddev>0.01</stddev></noise></lidar>
</sensor>
<sensor name="flightlab_contact" type="contact">
 <contact><collision>base_link_collision_0</collision><collision>base_link_collision_1</collision><collision>base_link_collision_2</collision><collision>base_link_collision_3</collision><collision>base_link_collision_4</collision><topic>/flightlab/contacts</topic></contact>
 <always_on>1</always_on><update_rate>10</update_rate>
</sensor>
'''


def prepare(out, variant='warehouse'):
    out=Path(out); out.mkdir(parents=True,exist_ok=True)
    data=scene(variant)
    (out/'scene.json').write_text(json.dumps(data,indent=2)+'\n')
    (out/'world.sdf').write_text(sdf(data))
    stock=HERE/'assets/px4/models'
    for model in ('x500','x500_base'):
        dest=out/'models'/('flightlab' if model=='x500' else 'flightlab_base')
        dest.mkdir(parents=True,exist_ok=True)
        text=(stock/model/'model.sdf').read_text()
        if model=='x500':
            text=text.replace("<model name='x500'>", "<model name='flightlab'>").replace('model://x500_base</uri>','model://flightlab_base</uri>')
        else:
            text=text.replace("<model name='x500_base'>", "<model name='flightlab_base'>")
            close=text.index('    </link>'); text=text[:close]+SENSORS+text[close:]
            for i in range(4):
                start=text.index('<link name="rotor_'+str(i)+'">')
                close=text.index('    </link>',start)
                contact=('<sensor name="rotor_contact_'+str(i)+'" type="contact"><contact><collision>rotor_'+str(i)+
                         '_collision</collision><topic>/flightlab/contacts</topic></contact><always_on>1</always_on><update_rate>10</update_rate></sensor>')
                text=text[:close]+contact+text[close:]
        (dest/'model.sdf').write_text(text)
        (dest/'model.config').write_text('<model><name>'+dest.name+'</name><version>1</version><sdf version="1.9">model.sdf</sdf></model>')
    return data
