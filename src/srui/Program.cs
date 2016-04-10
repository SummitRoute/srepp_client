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
using System.Windows.Forms;
using System.Threading;

namespace srui
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Log.Debug("Starting");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TrayIcon trayIcon = new TrayIcon();

            // Run thread for notifcations from the service
            Log.Debug("Checking for notifications");
            Notifications notifications = new Notifications(trayIcon);
            Thread notificationsThread = new Thread(new ThreadStart(notifications.Run));
            notificationsThread.Name = "NotificationsThread";
            notificationsThread.IsBackground = true;

            notificationsThread.Start();

            // Run the UI
            Application.Run(trayIcon);
        }
    }
}
