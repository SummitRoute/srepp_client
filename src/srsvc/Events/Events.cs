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
    partial class Event
    {

        /// <summary>
        /// Base class for events posted to the server.  All messages from the client to the server should contain at least this info
        /// </summary>
        [DataContract]
        internal class EventPost
        {
            [DataMember]
            internal string SystemUUID = "";

            [DataMember]
            internal string CustomerUUID = "";

            [DataMember]
            internal Int64 CurrentClientTime = 0;

            public EventPost()
            {
                SystemUUID = SRSvc.conf.SystemUUID;
                CustomerUUID = SRSvc.conf.GroupUUID;
                CurrentClientTime = Helpers.ConvertToUnixTime(DateTime.UtcNow);
            }
        }
    }
}
