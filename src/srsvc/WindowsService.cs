////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Text;
using System.ServiceProcess;
using System.Threading;
using System.IO;
using Microsoft.Win32;
using System.Reflection;

namespace srsvc
{
    [System.ComponentModel.DesignerCategory("")]
    class WindowsService : ServiceBase
    {
        public bool bRunning = true;
        static SRSvc srSvc = new SRSvc();
        static Thread srSvcThread = null;

        /// <summary>
        /// Stop the spawned threads so this can exit
        /// </summary>
        private void StopService()
        {
            bRunning = false;
            srSvc.Stop();
            // Wait for thread to stop
            while (srSvcThread.IsAlive);
        }

        /// <summary>
        /// Initialize the service (called after Main)
        /// </summary>
        public WindowsService()
        {
            Log.Info("Running WindowsService class");

            this.ServiceName = "SREPP";
            this.EventLog.Log = "Application";

            // These Flags set whether or not to handle that specific
            //  type of event. Set to true if you need it, false otherwise.
            this.CanHandlePowerEvent = false;
            this.CanHandleSessionChangeEvent = true;
            this.CanPauseAndContinue = true;
            this.CanShutdown = true;
            this.CanStop = true;
        }

        /// <summary>
        /// First code called when the service starts
        /// </summary>
        static void Main()
        {
            Log.Debug("WindowsService.Main Start");

            // Create a monitoring thread
            srSvcThread = new Thread(new ThreadStart(srSvc.Run));
            srSvcThread.Name = "SvcThread";
            srSvcThread.IsBackground = true;

            srSvcThread.Start();
            // Spin until the thread starts
            while (!srSvcThread.IsAlive);

            // Start service
            ServiceBase.Run(new WindowsService());
        }

        /// <summary>
        /// OnSessionChange
        /// </summary>
        /// <param name="changeDescription"></param>
        protected override void OnSessionChange(
                  SessionChangeDescription changeDescription)
        {
            Log.Info("OnSessionChange event");
            switch (changeDescription.Reason)
            {
                case SessionChangeReason.SessionLogon:
                    Log.Info("Session change: New user logged in");
                    break;
                case SessionChangeReason.RemoteConnect:
                    Log.Info("Session change: Remote connect");
                    break;
                case SessionChangeReason.ConsoleConnect:
                    Log.Info("Session change: Console connect");
                    break;
                default:
                    break;
            }

            base.OnSessionChange(changeDescription);
        }

        /// <summary>
        /// Dispose of objects that need it here.
        /// </summary>
        /// <param name="disposing">Whether
        ///    or not disposing is going on.</param>
        protected override void Dispose(bool disposing)
        {
            Log.Info("SREPP Dispose");
            base.Dispose(disposing);
        }

        /// <summary>
        /// OnStop(): Put your stop code here
        /// - Stop threads, set final data, etc.
        /// </summary>
        protected override void OnStop()
        {
            Log.Info("SREPP Stop");
            StopService();
            base.OnStop();
        }


        /// <summary>
        /// OnPause: Put your pause code here
        /// - Pause working threads, etc.
        /// </summary>
        protected override void OnPause()
        {
            Log.Info("SREPP Pause");
            base.OnPause();
        }

        /// <summary>
        /// OnContinue(): Put your continue code here
        /// - Un-pause working threads, etc.
        /// </summary>
        protected override void OnContinue()
        {
            Log.Info("SREPP Continue");
            base.OnContinue();
        }

        /// <summary>
        /// OnShutdown(): Called when the System is shutting down
        /// - Put code here when you need special handling
        ///   of code that deals with a system shutdown, such
        ///   as saving special data before shutdown.
        /// </summary>
        protected override void OnShutdown()
        {
            Log.Info("SREPP Shutdown");
            StopService();
            base.OnShutdown();
        }

        /// <summary>
        /// OnCustomCommand(): If you need to send a command to your
        ///   service without the need for Remoting or Sockets, use
        ///   this method to do custom methods.
        /// </summary>
        /// <param name="command">Arbitrary Integer between 128 & 256</param>
        protected override void OnCustomCommand(int command)
        {
            //  A custom command can be sent to a service by using this method:
            //#  int command = 128; //Some Arbitrary number between 128 & 256
            //#  ServiceController sc = new ServiceController("NameOfService");
            //#  sc.ExecuteCommand(command);
            Log.Info("SREPP Custom Command");

            base.OnCustomCommand(command);
        }
    }
}
