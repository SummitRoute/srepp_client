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

        [DataContract]
        internal class HeartbeatEventPost : EventPost
        {
            // Nothing new
        }

        /// <summary>
        /// Posts a heartbeat to the server, just letting it know we're alive and giving the server a chance to task it
        /// </summary>
        /// <returns></returns>
        public static dynamic PostHeartbeatEvent()
        {
            HeartbeatEventPost eventPost = new HeartbeatEventPost();

            string postMessage = Helpers.SerializeToJson(eventPost, typeof(HeartbeatEventPost));
            string response = Beacon.PostToServer(postMessage, "/api/v1/Heartbeat");

            return Json.Decode(response);
        }
    }
}
