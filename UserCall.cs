using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Rtc.Signaling;
using Microsoft.Rtc.Collaboration;
using Microsoft.Rtc.Collaboration.ConferenceManagement;
using Microsoft.Rtc.Collaboration.Conferencing;
using Microsoft.Rtc.Collaboration.AudioVideo;

namespace Locations
{
    public class UserCall
    {
        AudioVideoCall _avCall;
        Location _location;

        public Location Location { get { return _location; } }
        public AudioVideoCall Call { get { return _avCall; } }

        public UserCall(AudioVideoCall avCall, Location location)
        {
            _avCall = avCall;
            _location = location;
        }

        public void JoinLocation(Location location)
        {
        }
    }
}
