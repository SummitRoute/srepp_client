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
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace MessagingInterfaces
{
    public class ClientPipes
    {
        public static ConcurrentDictionary<string, IMessageServiceCallback> callbackList = new ConcurrentDictionary<string, IMessageServiceCallback>();
    }

    public interface IMessageServiceCallback
    {
        [OperationContract(IsOneWay = true)]
        void NotifyMsg(string msg);
    }

    [ServiceContract(CallbackContract = typeof(IMessageServiceCallback))]
    public interface IClientToServer
    {
        [OperationContract]
        void RegisterClient(string guid);
    }

    public class ClientRegistrar : IClientToServer
    {
        public void RegisterClient(string guid)
        {
            srsvc.Log.Info("Registering: {0}", guid);

            IMessageServiceCallback registeredUser =
                OperationContext.Current.GetCallbackChannel<IMessageServiceCallback>();

            ClientPipes.callbackList.AddOrUpdate(guid, registeredUser, (i, v) => registeredUser);
        }
    }

    public class CommunicateWithUI
    {
        private ServiceHost host;

        /// <summary>
        /// Initialize our server, allows for clients to register themselves
        /// </summary>
        public CommunicateWithUI()
        {
            host = new ServiceHost(
                typeof(ClientRegistrar),
                new Uri[] { new Uri("net.pipe://localhost") }
                );

            host.AddServiceEndpoint(
                typeof(IClientToServer),
                new NetNamedPipeBinding(),
                "SREPP_regc");
            host.Open();
        }

        ~CommunicateWithUI()
        {
            host.Close();
        }


        /// <summary>
        /// Sends message to every client that has registered itself
        /// </summary>
        /// <param name="msg"></param>
        public void InformUI(string msg)
        {
            srsvc.Log.Info("SVC: InformUI");
            List<string> removalList = new List<string>();

            // Contact each client and if any can't be contacted, then remove them from our list
            foreach (string guid in ClientPipes.callbackList.Keys) 
            {
                try
                {
                    ClientPipes.callbackList[guid].NotifyMsg(msg); 
                }
                catch (Exception e)
                {
                    // Problem with client, so removing it
                    srsvc.Log.Exception(e, "Exception sending to client");
                    removalList.Add(guid);
                }
            }

            // Clear out bad clients
            foreach (string guid in removalList)
            {
                IMessageServiceCallback ignore;
                ClientPipes.callbackList.TryRemove(guid, out ignore);
            }
            removalList.Clear();
        }
    }

    public static class UIComm
    {
        public static CommunicateWithUI uiComm = null;

        public static void Init()
        {
            uiComm = new CommunicateWithUI();
        }

        public static void InformUI(string msg)
        {
            uiComm.InformUI(msg);
        } 

    }
}