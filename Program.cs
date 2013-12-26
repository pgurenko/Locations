/*
 *
 * Application uses UCMA 3.0 API to handle incoming user calls and 
 * place them into "rooms" with sound playing
 * 
 * You need to set up the trusted application with <ApplicationID> first
 * to try it out.
 * 
 * Sample configuration commands:
 * New-CsTrustedApplication -ApplicationId locations -TrustedApplicationPoolFqdn apps.domain.com -Port 6005
 * New-CsTrustedApplicationEndpoint -SipAddress "sip:locations@domain.com" -DisplayName "Locations" -TrustedApplicationPoolFqdn apps.domain.com -ApplicationId "locations"
 * Enable-CsTopology
 * 
 * Made by Pavel Gurenko (http://pavelgurenko.com)
 *
 */

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Net;
using System.Net.Mime;
using System.Threading;

using Microsoft.Rtc.Collaboration;
using Microsoft.Rtc.Collaboration.ConferenceManagement;
using Microsoft.Rtc.Collaboration.AudioVideo;
using Microsoft.Rtc.Internal.Collaboration.Conferencing;
using Microsoft.Rtc.Signaling;
using Microsoft.Rtc.Collaboration.Presence;

namespace Locations
{
    public class Program
    {
        #region Locals
        static CollaborationPlatform _collabPlatform;
        static ApplicationEndpoint _appEndpoint;
        static LocalEndpoint _currentEndpoint;
        static List<UserCall> _userCalls = new List<UserCall>();
        static List<Location> _locations = new List<Location>();
        #endregion

        #region Methods

        public static void Main(string[] args)
        {
            try
            {
                string appId = ConfigurationManager.AppSettings["ApplicationID"];

                Console.WriteLine("Creating CollaborationPlatform for the provisioned application with "
                    + "ID \'{0}\' using ProvisionedApplicationPlatformSettings.", appId);
                ProvisionedApplicationPlatformSettings settings
                    = new ProvisionedApplicationPlatformSettings("UCMASampleApp", appId);

                settings.DefaultAudioVideoProviderEnabled = true;

                _collabPlatform = new CollaborationPlatform(settings);

                _collabPlatform.InstantMessagingSettings.SupportedFormats = InstantMessagingFormat.All;

                // Wire up a handler for the 
                // ApplicationEndpointOwnerDiscovered event.
                _collabPlatform.RegisterForApplicationEndpointSettings(
                    Platform_ApplicationEndpointOwnerDiscovered);
                
                // Initalize and startup the platform.
                _collabPlatform.BeginStartup(EndPlatformStartup, _collabPlatform);

                Console.WriteLine("Please hit Esc key to end the sample.");
                
                ConsoleKeyInfo info = Console.ReadKey();
                while (info.Key != ConsoleKey.Escape)
                {
                    info = Console.ReadKey();
                }
            }
            catch (InvalidOperationException iOpEx)
            {
                Console.WriteLine("Invalid Operation Exception: " + iOpEx.ToString());
            }
            finally
            {
                Console.WriteLine("Shutting down the platform.");
                ShutdownPlatform();
            }
        }

        // Method to shutdown the CollaborationPlatform.
        static void ShutdownPlatform()
        {
            _collabPlatform.BeginShutdown(ar =>
            {
                CollaborationPlatform collabPlatform = ar.AsyncState as CollaborationPlatform;
                try
                {
                    collabPlatform.EndShutdown(ar);
                    Console.WriteLine("The platform is now shut down.");
                }
                catch (RealTimeException realTimeEx)
                {
                    Console.WriteLine("RealTimeException: " + realTimeEx.ToString());
                }
            },
            _collabPlatform);
        }

        // Registered event handler for the ApplicationEndpointOwnerDiscovered event on the
        // CollaborationPlatform for the provisioned application.
        static void Platform_ApplicationEndpointOwnerDiscovered(object sender,
            ApplicationEndpointSettingsDiscoveredEventArgs e)
        {
            Console.WriteLine("ApplicationEndpointOwnerDiscovered event was raised during startup of the CollaborationPlatform.");
            Console.WriteLine("The ApplicationEndpointOwnerConfiguration that corresponds to the provisioned application are: ");  
            Console.WriteLine("Owner display name is: " + e.ApplicationEndpointSettings.OwnerDisplayName);
            Console.WriteLine("Owner URI is: " + e.ApplicationEndpointSettings.OwnerUri);
            Console.WriteLine("Now retrieving the ApplicationEndpointSettings from the ApplicationEndpointSettingsDiscoveredEventArgs.");

            ApplicationEndpointSettings settings = e.ApplicationEndpointSettings;
            settings.AutomaticPresencePublicationEnabled = true;
            settings.SupportedMimePartContentTypes = new ContentType[] { new ContentType("text/plain") };
            settings.Presence.Description = "AlwaysOnlineBot";

            PreferredServiceCapabilities capabilities = settings.Presence.PreferredServiceCapabilities;

            capabilities.ApplicationSharingSupport = CapabilitySupport.Supported;
            capabilities.AudioSupport = CapabilitySupport.Supported;
            capabilities.InstantMessagingSupport = CapabilitySupport.Supported;
            capabilities.VideoSupport = CapabilitySupport.Supported;

            Console.WriteLine("Initializing the ApplicationEndpoint that corresponds to the provisioned application.");
                    
            // Initalize the endpoint using the settings retrieved above.
            _appEndpoint = new ApplicationEndpoint(_collabPlatform, settings);
            // Wire up the StateChanged event.
            _appEndpoint.StateChanged += Endpoint_StateChanged;
            // Wire up the ApplicationEndpointOwnerPropertiesChanged event.
            _appEndpoint.OwnerPropertiesChanged += Endpoint_ApplicationEndpointOwnerPropertiesChanged;

            try
            {
                // Establish the endpoint.
                _appEndpoint.BeginEstablish(EndEndpointEstablish, _appEndpoint);
            }
            catch (InvalidOperationException iOpEx)
            {
                Console.WriteLine("Invalid Operation Exception: " + iOpEx.ToString());
            }
        }

        // Record the endpoint state transitions to the console.
        static void Endpoint_StateChanged(object endpoint, LocalEndpointStateChangedEventArgs e)
        {
            // When the endpoint is terminated because of a contact being deleted,
            // the application receives Terminating and Terminated state changes.
            Console.WriteLine("Endpoint has changed state. The previous endpoint state was: " + e.PreviousState + " and the current state is: " + e.State);
        }

        // Record the ApplicationEndpoint's owner changes to the console.
        static void Endpoint_ApplicationEndpointOwnerPropertiesChanged(object endpoint, 
            ApplicationEndpointOwnerPropertiesChangedEventArgs e)
        {
            // When provisioning data for the endpoint changes, the 
            // ProvisioningDataChanged event is raised.
            Console.WriteLine("ApplicationEndpoint's owner properties have changed");
            Console.WriteLine("The set of changed properties for the provisioned application are: ");
            foreach (var property in e.ChangedPropertyNames)
            {
                Console.WriteLine(property);
            }
        }

        // Callback for CollaborationPlatform's BeginStartup().
        static void EndPlatformStartup(IAsyncResult ar)
        {
            CollaborationPlatform collabPlatform = ar.AsyncState as CollaborationPlatform;
            try
            {
                // The CollaborationPlatform should now be started.
                collabPlatform.EndStartup(ar);
                Console.WriteLine("Collaboration platform associated with the provisioned application has been started");
            }
            catch (ProvisioningFailureException pfEx)
            {
                Console.WriteLine("ProvisioningFailure Exception: " + pfEx.ToString());
                Console.WriteLine("The FailureReason for the ProvisioningFailure Exception: "
                    + pfEx.FailureReason.ToString());
            }
            catch (OperationFailureException opFailEx)
            {
                Console.WriteLine("OperationFailure Exception: " + opFailEx.ToString());
            }
            catch (ConnectionFailureException connFailEx)
            {
                Console.WriteLine("ConnectionFailure Exception: " + connFailEx.ToString());
            }
            catch (RealTimeException realTimeEx)
            {
                Console.WriteLine("RealTimeException : " + realTimeEx.ToString());
            }
        }

        // Callback for ApplicationEndpoint's BeginEstablish().
        static void EndEndpointEstablish(IAsyncResult ar)
        {
            LocalEndpoint currentEndpoint = ar.AsyncState as LocalEndpoint;
            try
            {
                currentEndpoint.EndEstablish(ar);

                _currentEndpoint = currentEndpoint;

                // Here, we have the endpoint fully established
                // So let's get started
                _currentEndpoint.RegisterForIncomingCall<AudioVideoCall>(IncomingAVCallReceived);

                _locations.Add(new Location(0, "Airport", ConfigurationManager.AppSettings["AirportRoomMusic"], _currentEndpoint));
                _locations.Add(new Location(1, "Bus", ConfigurationManager.AppSettings["BusRoomMusic"], _currentEndpoint));
                _locations.Add(new Location(2, "Outdoor", ConfigurationManager.AppSettings["OutdoorRoomMusic"], _currentEndpoint));
            }
            catch (ConnectionFailureException connFailEx)
            {
                Console.WriteLine("ConnectionFailure Exception: " + connFailEx.ToString());
            }
            catch (RealTimeException realTimeEx)
            {
                Console.WriteLine("RealTimeException : " + realTimeEx.ToString());
            }
            catch (Exception Ex)
            {
                Console.WriteLine("Exception : " + Ex.ToString());
            }
        }

        static void IncomingAVCallReceived(object sender, CallReceivedEventArgs<AudioVideoCall> e)
        {
            ApplicationEndpoint applicationEndpoint = (ApplicationEndpoint)sender;

            bool callReplaced = false;
            if (e.CallToBeReplaced != null)
            {
                callReplaced = true;
            }

            Console.WriteLine("IncomingAVCallReceived for endpoint " + applicationEndpoint.OwnerUri +
                " RemoteEndpointUri=" + e.Call.RemoteEndpoint.Uri +
                " CallReplaced=" + callReplaced);

            if (!callReplaced)
            { 
                // Incoming call. Placing it into the random location
                Random rnd = new Random();

                int index = rnd.Next(0, _locations.Count - 1);
                if (index > 0)
                { 
                    // Place user to location
                    _userCalls.Add(new UserCall(e.Call, _locations[index]));
                }
            }
            else
            {
                // Transfer user to the next location
                // We are getting here after BeginTransfer was called
                foreach (UserCall userCall in _userCalls)
                {
                    if (e.CallToBeReplaced.ApplicationContext == userCall)
                    {
                        int newLocationId = userCall.Location.Id + 1;

                        if (newLocationId >= _locations.Count)
                        {
                            newLocationId = 0;
                        }

                        userCall.JoinLocation(_locations[newLocationId]);

                        break;
                    }
                }
            }
        }

        #endregion
    }
}
