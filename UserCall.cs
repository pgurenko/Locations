using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

using Microsoft.Rtc.Signaling;
using Microsoft.Rtc.Collaboration;
using Microsoft.Rtc.Collaboration.ConferenceManagement;
using Microsoft.Rtc.Collaboration.Conferencing;
using Microsoft.Rtc.Collaboration.AudioVideo;

using Microsoft.Speech.Recognition;
using Microsoft.Speech.AudioFormat;

namespace Locations
{
    public enum UserCallTransferPath
    {
        Previous,
        Next
    }

    public class UserCall
    {
        string _uri;
        Location _location;
        BackToBackCall _b2bCall;
        AudioVideoCall _controlAVCall;
        SpeechRecognitionEngine _speechRecognitionEngine;
        UserCallTransferPath _userCallTransferPath = UserCallTransferPath.Next;

        public string Uri { get { return _uri; } }
        public Location Location { get { return _location; } }
        public UserCallTransferPath TransferPath { get { return _userCallTransferPath; } }
        
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

                        EstablishControlCall();
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


        // For some reason, m_callLocalIdentity field is hidden((
        ConversationParticipant GetLocalParticipant()
        {
            try
            {
                FieldInfo fi = typeof(Call).GetField("m_callLocalIdentity", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null)
                    return null;

                return (ConversationParticipant)fi.GetValue(_b2bCall.Call2);
            }
            catch (Exception)
            {
                return null;
            }
        }

        void EstablishControlCall()
        {
            AudioVideoCallEstablishOptions ceo = new AudioVideoCallEstablishOptions()
            {
                UseGeneratedIdentityForTrustedConference = true
            };

            _controlAVCall = new AudioVideoCall(_location.Conversation);

            _controlAVCall.AudioVideoFlowConfigurationRequested += new EventHandler<AudioVideoFlowConfigurationRequestedEventArgs>(_controlAVCall_AudioVideoFlowConfigurationRequested);
            _controlAVCall.BeginEstablish(ceo,
                ar =>
                {
                    try
                    {
                        _controlAVCall.EndEstablish(ar);

                        List<IncomingAudioRoute> routesin = new List<IncomingAudioRoute>();
                        List<OutgoingAudioRoute> routesout = new List<OutgoingAudioRoute>();

                        ParticipantEndpoint localEndpoint = GetLocalParticipant().GetEndpoints()[0];
                        ParticipantEndpoint remoteEndpoint = _controlAVCall.RemoteEndpoint;

                        routesin.Add(new IncomingAudioRoute(localEndpoint));
                        routesin.Add(new IncomingAudioRoute(remoteEndpoint));

                        routesout.Add(new OutgoingAudioRoute(localEndpoint));
                        routesout.Add(new OutgoingAudioRoute(remoteEndpoint));

                        _controlAVCall.AudioVideoMcuRouting.BeginUpdateAudioRoutes(routesout, routesin,
                            ar2 =>
                            {
                                try
                                {
                                    _controlAVCall.AudioVideoMcuRouting.EndUpdateAudioRoutes(ar2);
                                }
                                catch (Exception ex)
                                {
                                    Log(ex.ToString());
                                }
                            },
                            null);
                    }
                    catch (Exception ex)
                    {
                        Log(ex.ToString());
                    }
                },
                null);
        }

        void _controlAVCall_AudioVideoFlowConfigurationRequested(object sender, AudioVideoFlowConfigurationRequestedEventArgs e)
        {
            Log("ControlAVCall AudioVideoFlowConfigurationRequested");
            e.Flow.StateChanged += new EventHandler<MediaFlowStateChangedEventArgs>(Flow_StateChanged);
        }

        void Flow_StateChanged(object sender, MediaFlowStateChangedEventArgs e)
        {
            Log("ControlAVCall Flow_StateChanged PreviousState=" + e.PreviousState + " State=" + e.State);

            AudioVideoFlow avFlow = (AudioVideoFlow)sender;

            if (avFlow.State == MediaFlowState.Active)
            {
                SpeechRecognitionConnector speechRecognitionConnector = new SpeechRecognitionConnector();
                speechRecognitionConnector.AttachFlow(avFlow);
                
                SpeechRecognitionStream stream = speechRecognitionConnector.Start();

                _speechRecognitionEngine = new SpeechRecognitionEngine();
                _speechRecognitionEngine.SpeechRecognized += new EventHandler<SpeechRecognizedEventArgs>(_speechRecognitionEngine_SpeechRecognized);
                _speechRecognitionEngine.LoadGrammarCompleted += new EventHandler<LoadGrammarCompletedEventArgs>(_speechRecognitionEngine_LoadGrammarCompleted);

                Choices pathChoice = new Choices(new string[] { "previous", "next" });
                Grammar gr = new Grammar(new GrammarBuilder(pathChoice));
                _speechRecognitionEngine.LoadGrammarAsync(gr);

                SpeechAudioFormatInfo speechAudioFormatInfo = new SpeechAudioFormatInfo(8000, AudioBitsPerSample.Sixteen, Microsoft.Speech.AudioFormat.AudioChannel.Mono);
                _speechRecognitionEngine.SetInputToAudioStream(stream, speechAudioFormatInfo);
                _speechRecognitionEngine.RecognizeAsync(RecognizeMode.Multiple);
            }
            else 
            {
                if (avFlow.SpeechRecognitionConnector != null)
                {
                    avFlow.SpeechRecognitionConnector.DetachFlow();
                }
            }
        }

        void _speechRecognitionEngine_LoadGrammarCompleted(object sender, LoadGrammarCompletedEventArgs e)
        {
            Log("_speechRecognitionEngine_LoadGrammarCompleted");
        }

        void  _speechRecognitionEngine_SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Log("_speechRecognitionEngine_SpeechRecognized " + 
                "Confidence=" + e.Result.Confidence + " " +
                "Text=" + e.Result.Text);

            if (e.Result.Text == "next")
            {
                _userCallTransferPath = UserCallTransferPath.Next;
            }
            else if (e.Result.Text == "previous")
            {
                _userCallTransferPath = UserCallTransferPath.Previous;
            }

            // Performing a self-transfer
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
    }
}
