using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Microsoft.Rtc.Signaling;
using Microsoft.Rtc.Collaboration;
using Microsoft.Rtc.Collaboration.ConferenceManagement;
using Microsoft.Rtc.Collaboration.Conferencing;
using Microsoft.Rtc.Collaboration.AudioVideo;

namespace Locations
{
    public class Location
    {
        string _Name;
        string _FileName;
        LocalEndpoint _Endpoint;

        Conference _conference;
        AudioVideoCall _avCall;

        void Log(string data)
        {
            Console.WriteLine("Location \"{0}\" {1}", _Name, data);
        }

        public Location(string name, string fileName, LocalEndpoint endpoint)
        {
            if (!File.Exists(fileName))
                throw new FileNotFoundException(fileName);

            _Name = name;
            _FileName = fileName;
            _Endpoint = endpoint;

            ConferenceScheduleInformation csi = new ConferenceScheduleInformation()
            {
                AccessLevel = ConferenceAccessLevel.Everyone,
                Description = _Name,
                ExpiryTime = DateTime.Now.AddYears(5),
                AutomaticLeaderAssignment = AutomaticLeaderAssignment.Everyone
            };

            csi.Mcus.Add(new ConferenceMcuInformation(McuType.AudioVideo));

            _Endpoint.ConferenceServices.BeginScheduleConference(csi,
                ar =>
                {
                    try
                    {
                        _conference = _Endpoint.ConferenceServices.EndScheduleConference(ar);

                        Log("Conference " + _conference.ConferenceId + " scheduled. Starting music...");

                        Log(_conference.ConferenceUri);

                        ConversationSettings cs = new ConversationSettings()
                        {
                            Subject = _Name
                        };

                        Conversation conversation = new Conversation(_Endpoint, cs);

                        ConferenceJoinOptions cjo = new ConferenceJoinOptions()
                        {
                            JoinMode = JoinMode.TrustedParticipant
                        };

                        conversation.ConferenceSession.BeginJoin(_conference.ConferenceUri,
                            cjo,
                            ar1 =>
                            {
                                try
                                {
                                    conversation.ConferenceSession.EndJoin(ar1);

                                    _avCall = new AudioVideoCall(conversation);
                                    _avCall.AudioVideoFlowConfigurationRequested +=
                                        new EventHandler<AudioVideoFlowConfigurationRequestedEventArgs>(_avCall_AudioVideoFlowConfigurationRequested);

                                    AudioVideoCallEstablishOptions options = new AudioVideoCallEstablishOptions()
                                    {
                                        UseGeneratedIdentityForTrustedConference = true,
                                        SupportsReplaces = CapabilitySupport.Supported
                                    };

                                    _avCall.BeginEstablish(
                                        options,
                                        ar2 =>
                                        {
                                            try
                                            {
                                                _avCall.EndEstablish(ar2);
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
                    catch (Exception ex)
                    {
                        Log(ex.ToString());
                    }
                },
                null);
        }

        void _avCall_AudioVideoFlowConfigurationRequested(object sender, AudioVideoFlowConfigurationRequestedEventArgs e)
        {
            Log("_avCall_AudioVideoFlowConfigurationRequested");
            e.Flow.StateChanged += new EventHandler<MediaFlowStateChangedEventArgs>(Flow_StateChanged);
        }

        void Flow_StateChanged(object sender, MediaFlowStateChangedEventArgs e)
        {
            Log("Flow_StateChanged PreviousState=" + e.PreviousState + " State=" + e.State);

            AudioVideoFlow avFlow = (AudioVideoFlow)sender;
            
            if (avFlow.State == MediaFlowState.Active)
            {
                Player player = new Player();
                player.AttachFlow(avFlow);

                WmaFileSource src = new WmaFileSource(_FileName);
                src.BeginPrepareSource(MediaSourceOpenMode.Buffered,
                    ar=>
                    {
                        try
                        {
                            src.EndPrepareSource(ar);
                            player.SetSource(src);
                            player.SetMode(PlayerMode.Automatic);
                            player.Start();

                            Log("Playing \"" + _FileName + "\"");
                        }
                        catch (Exception ex)
                        {
                            Log(ex.ToString());
                        }
                    },
                    null);
            }
            else if (avFlow.State == MediaFlowState.Terminated)
            {
                if (avFlow.Player != null)
                {
                    avFlow.Player.DetachFlow(avFlow);
                }
            }           
        }
    }
}
