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
        /// Set the SystemUUID. Normally only set during registration, but just in case the user clones a VM, we'll allow this to be changed
        /// </summary>
        /// <param name="serverMsg"></param>
        /// <returns></returns>
        public static dynamic SetSystemUUID(dynamic serverMsg)
        {
            string newSystemUUID = serverMsg.Arguments.SystemUUID;
            Log.Info("Command SetSystemUUID setting UUID to: {0}", newSystemUUID);
            SRSvc.conf.SystemUUID = newSystemUUID;

            return null;
        }
    }
}
