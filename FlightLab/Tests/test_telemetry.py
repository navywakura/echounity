import unittest
from echo_flightlab import telemetry


class TelemetryTests(unittest.TestCase):
    def test_observed_ground_offset_is_used_in_both_directions(self):
        frame=telemetry.LocalFrame((200,-300,1600))
        self.assertEqual(frame.relative((200,-300,1600)),(0,0,0))
        self.assertEqual(frame.relative((200,-300,3100)),(0,0,1500))
        self.assertEqual(frame.absolute((0,0,1500)),(200,-300,3100))

    def test_duplicate_timestamp_does_not_refresh_or_accept_payload(self):
        c=telemetry.SourceClock()
        self.assertTrue(c.update(1,9000))
        self.assertFalse(c.update(1,19000))
        self.assertEqual(c.stamp_ms,9000)

    def test_reordered_timestamp_is_ignored(self):
        c=telemetry.SourceClock(); c.update(200,1000)
        self.assertFalse(c.update(100,2000)); self.assertEqual(c.stamp_ms,1000)

    def test_delayed_queue_does_not_become_fresh(self):
        c=telemetry.SourceClock(); c.update(100,1000)
        c.update(150,5000)
        self.assertEqual(c.stamp_ms,1050)

    def test_wrap_and_no_future_dates(self):
        c=telemetry.SourceClock(); c.update(2**32-100,1000)
        self.assertTrue(c.update(100,1200)); self.assertEqual(c.stamp_ms,1200)
        c.update(500,1250); self.assertLessEqual(c.stamp_ms,1250)

    def test_contingency_is_not_mission_success(self):
        self.assertEqual(telemetry.flight_status(True,False,4,4),'CONTINGENCY_LANDED')
        self.assertEqual(telemetry.flight_status(True,True,2,4),'RETURNED_PARTIAL')
        self.assertEqual(telemetry.flight_status(True,True,4,4),'MISSION_COMPLETE')
        self.assertEqual(telemetry.flight_status(False,False,4,4),'LANDING_INCOMPLETE')

    def test_ground_motion_is_not_a_completed_flight(self):
        self.assertEqual(telemetry.flight_status(True,True,4,4,airborne=False),'TAKEOFF_UNCONFIRMED')
