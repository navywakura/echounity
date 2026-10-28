import unittest
from echo_flightlab.physics import evidence


class PhysicalTests(unittest.TestCase):
    def frames(self,height):
        return [dict(time=t,position=[0,0,0 if t==0 else height],estimated=[0,0,1.5]) for t in range(8)]

    def test_estimated_altitude_does_not_prove_takeoff(self):
        self.assertFalse(evidence(self.frames(.15),100,100)['takeoff_verified'])

    def test_physical_flight_and_positive_contact_channel(self):
        r=evidence(self.frames(1.5),100,100)
        self.assertTrue(r['takeoff_verified'])
        self.assertTrue(r['contact_channel_verified'])

    def test_zero_messages_is_unverified_not_zero_collisions(self):
        self.assertFalse(evidence(self.frames(1.5),0,0)['contact_channel_verified'])
