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
        string _uri;
        Location _location;
        BackToBackCall _b2bCall;

        public string Uri { get { return _uri; } }
        public Location Location { get { return _location; } }
        
        public event EventHandler Terminated;

        void Log(string data)
        {
            Console.WriteLine("Location \"{0}\" {1}", _uri, data);
        }

        public UserCall(string uri, AudioVideoCall avCall, Location location)
        {
            _uri = uri;
            JoinLocation(avCall, location);
        }

        public void Transfer()
        {
            AudioVideoCall avCall = (AudioVideoCall)_b2bCall.Call1;
            avCall.BeginTransfer(avCall,
                ar =>
                {
                    try
                    {
                        avCall.EndTransfer(ar);
                    }
                    catch (Exception ex)
                    {
                        Log(ex.ToString());
                    }
                },
                null);
        }

        public void JoinLocation(AudioVideoCall incomingCall, Location location)
        {
            _location = location;

            // Call Leg 1
            BackToBackCallSettings settings1 = new BackToBackCallSettings(incomingCall);
            // Call Leg 2
            BackToBackCallSettings settings2 = new BackToBackCallSettings(new AudioVideoCall(location.Conversation));

            settings2.CallEstablishOptions = new AudioVideoCallEstablishOptions()
            {
                UseGeneratedIdentityForTrustedConference = true,
                SupportsReplaces = CapabilitySupport.Supported
            };

            // Create and establish the back to back call.
            _b2bCall = new BackToBackCall(settings1, settings2);
            _b2bCall.StateChanged += new EventHandler<BackToBackCallStateChangedEventArgs>(_b2bCall_StateChanged);
            _b2bCall.BeginEstablish(
                ar =>
                {
                    try
                    {
                        _b2bCall.EndEstablish(ar);
                    }
                    catch (RealTimeException ex)
                    {
                        Log(ex.ToString());
                    }
                },
                null);
        }

        void _b2bCall_StateChanged(object sender, BackToBackCallStateChangedEventArgs e)
        {
            Log("B2BCall StateChanged. PreviousState=" + e.PreviousState + " State=" + e.State);

            if (e.State == BackToBackCallState.Terminated)
            {
                if (Terminated != null)
                {
                    Terminated(this, EventArgs.Empty);
                }
            }
        }
    }
}
