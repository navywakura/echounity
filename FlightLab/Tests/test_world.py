import unittest
import tempfile
from pathlib import Path
import xml.etree.ElementTree as ET
from echo_flightlab.world import scene, sdf, prepare


class WorldTests(unittest.TestCase):
    def test_same_boxes_in_sdf(self):
        data=scene(); w=ET.fromstring(sdf(data)).find('world')
        self.assertEqual(len(w.findall('model')),len(data['boxes']))
        for a,b in zip(data['boxes'],w.findall('model')):
            self.assertEqual(a['name'],b.attrib['name'])
            self.assertEqual(a['size'],list(map(float,b.find('link/collision/geometry/box/size').text.split())))

    def test_variant_adds_physical_obstacle(self):
        self.assertEqual(len(scene('changed')['boxes']),len(scene()['boxes'])+1)

    def test_contacts_reference_actual_collisions_on_each_link(self):
        with tempfile.TemporaryDirectory() as tmp:
            prepare(tmp)
            m=ET.parse(Path(tmp)/'models/flightlab_base/model.sdf').getroot().find('model')
            count=0
            for link in m.findall('link'):
                names={c.get('name') for c in link.findall('collision')}
                for sensor in link.findall('sensor'):
                    if sensor.get('type')!='contact': continue
                    count+=1
                    self.assertEqual(sensor.find('always_on').text,'1')
                    # Harmonic's Contact system reads contact/topic, unlike LiDAR.
                    self.assertIsNotNone(sensor.find('contact/topic'))
                    self.assertEqual(sensor.find('contact/topic').text,'/flightlab/contacts')
                    self.assertTrue(all(c.text in names for c in sensor.findall('contact/collision')))
            self.assertEqual(count,5)
