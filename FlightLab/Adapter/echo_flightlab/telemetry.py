"""Source time accounting, independent of MAVLink/Gazebo libraries."""


class LocalFrame:
    """Observed PX4 estimate at rest defines home; no simulator truth needed."""
    def __init__(self,origin=(0,0,0)):
        self.origin=tuple(origin)

    def relative(self,position):
        return tuple(v-o for v,o in zip(position,self.origin))

    def absolute(self,target):
        return tuple(v+o for v,o in zip(target,self.origin))


class SourceClock:
    """Anchor the first source sample once; later arrival cannot refresh it.

    Time_boot_ms is uint32. Out-of-order/repeated packets are rejected. This
    is freshness after initial alignment, not a measured clock-sync bound.
    """
    def __init__(self):
        self.previous=None; self.stamp_ms=-1; self.projected=-1

    def update(self,source_ms,arrival_ms):
        source_ms=int(source_ms)&0xffffffff
        if self.previous is None:
            self.previous=source_ms; self.projected=arrival_ms; self.stamp_ms=arrival_ms
            return True
        delta=(source_ms-self.previous)&0xffffffff
        if delta==0 or delta>=2**31: return False
        self.previous=source_ms; self.projected+=delta
        self.stamp_ms=min(arrival_ms,self.projected)
        return True


def flight_status(landed,returned,visited,requested,airborne=True):
    if not landed: return 'LANDING_INCOMPLETE'
    if not airborne: return 'TAKEOFF_UNCONFIRMED'
    if not returned: return 'CONTINGENCY_LANDED'
    if visited<requested: return 'RETURNED_PARTIAL'
    return 'MISSION_COMPLETE'
