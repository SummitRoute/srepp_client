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
using System.Linq;
using System.Text;
using System.Net;
using System.IO;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using System.Web.Helpers;
using Microsoft.CSharp.RuntimeBinder;

namespace srsvc
{
    public partial class Command
    {
        /// <summary>
        /// Delay so we don't try contacting the server again for a while
        /// </summary>
        /// <param name="serverMsg"></param>
        /// <returns></returns>
        public static dynamic Stall(dynamic serverMsg)
        {
            Log.Info("Command Stall");

            int Delay = serverMsg.Arguments.DelayInSeconds;

            // Sanity check, don't allow us to delay for less than 5 minutes
            if (Delay < 60 * 5)
            {
                Delay = 60 * 5;
            }

            System.Threading.Thread.Sleep(Delay * 1000);

            return null;
        }
    }
}
