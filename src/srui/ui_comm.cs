////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

using System;
using System.ServiceModel;


namespace srui
{
    /// <summary>
    /// Used to connect to the service
    /// </summary>
    [ServiceContract(CallbackContract = typeof(IMessageServiceCallback))]
    public interface IClientToServer
    {
        [OperationContract]
        void RegisterClient(string guid);
    }

    /// <summary>
    /// Used to receive messages from the service
    /// </summary>
    public interface IMessageServiceCallback
    {
        [OperationContract(IsOneWay = true)]
        void NotifyMsg(string msg);
    }

    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Reentrant)]
    public class ServiceCallback : IMessageServiceCallback
    {
        public void NotifyMsg(string msg)
        {
            Log.Info("Received msg: {0}", msg);
            UIDisplay.DisplayNotification(msg);

            return;
        }
    }

    public static class UIDisplay {
        public static TrayIcon trayIcon = null;

        public static void DisplayNotification(string msg)
        {
            try
            {
                if (trayIcon != null)
                {
                    trayIcon.DisplayNotification(msg);
                }
            }
            catch (Exception e)
            {
                Log.Exception(e, "Exception in DisplayNotification");
            }
        }
    }

    public class Notifications
    {
        public Notifications(TrayIcon _trayIcon)
        {
            UIDisplay.trayIcon = _trayIcon;
        }

        public void Run()
        {
            string guid = Guid.NewGuid().ToString();
            Log.Info("Start client: {0}", guid);

            IClientToServer clientRegistrar;

            // Register client
            while (true)
            {
                try
                {
                    var ctx = new InstanceContext(new ServiceCallback());
                    DuplexChannelFactory<IClientToServer> clientRegistrarFactory =
                        new DuplexChannelFactory<IClientToServer>(ctx,
                            new NetNamedPipeBinding(),
                            new EndpointAddress("net.pipe://localhost/SREPP_regc")
                            );

                    clientRegistrar = clientRegistrarFactory.CreateChannel();

                    clientRegistrar.RegisterClient(guid);
                    break;
                }
                catch (Exception e)
                {

                    if (e is System.ServiceModel.EndpointNotFoundException)
                    {
                        Log.Info("Problem connecting to server, will try again in 10 seconds");
                        System.Threading.Thread.Sleep(10000);  // Sleep 10s
                    }
                    else
                    {
                        // Unknown exception, just bail
                        Log.Exception(e, "Exception registering client");
                        break;
                    }
                }
            }

            Log.Debug("Connected to the server");

            // Sleep while we wait for new messages.  Everything happens on a callback, so we just sleep constantly
            // TODO MAYBE: I should check if the server still knows about me, and if not, register again
            while (true)
            {
                System.Threading.Thread.Sleep(60000); // Sleep 60 seconds
            }

        }
    }
}