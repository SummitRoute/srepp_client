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
using System.Runtime.Serialization;

namespace srsvc
{
    partial class Event
    {
        /// <summary>
        /// Data sent to the server when a new process is created
        /// </summary>
        [DataContract]
        internal class ProcessEventPost : EventPost
        {
            [DataMember]
            internal Int64 TimeOfEvent = 0;

            [DataMember]
            internal Int32 Type = 0;

            [DataMember]
            internal Int64 PID = 0;

            [DataMember]
            internal Int64 PPID = 0;

            [DataMember]
            internal string Path = "";

            [DataMember]
            internal string CommandLine = "";

            [DataMember]
            internal string Md5 = "";

            [DataMember]
            internal string Sha1 = "";

            [DataMember]
            internal string Sha256 = "";

            [DataMember]
            internal Int32 Size = 0;

            [DataMember]
            internal bool IsSigned = false;
        }

        /// <summary>
        /// Response after sending a process event
        /// </summary>
        [DataContract]
        internal class ProcessEventResponse
        {
            // nop
        }


        /// <summary>
        /// Sends info about a process event to the server, returns true on having successful informed the server
        /// </summary>
        /// <param name="processEvent"></param>
        /// <returns></returns>
        public static bool PostProcessEvent(ProcessEvent processEvent, Executable executable)
        {
            ProcessEventPost processEventPost = new ProcessEventPost();
            processEventPost.TimeOfEvent = Helpers.ConvertToUnixTime(processEvent.EventTime);
            processEventPost.Type = (Int32)processEvent.State;
            processEventPost.PID = processEvent.Pid;
            processEventPost.PPID = processEvent.Ppid;
            processEventPost.Path = executable.Path;
            processEventPost.CommandLine = processEvent.CommandLine;
            processEventPost.Md5 = Helpers.ByteArrayToHexString(executable.Md5);
            processEventPost.Sha1 = Helpers.ByteArrayToHexString(executable.Sha1);
            processEventPost.Sha256 = Helpers.ByteArrayToHexString(executable.Sha256);
            processEventPost.Size = (int)(new System.IO.FileInfo(executable.Path).Length);
            processEventPost.IsSigned = executable.Signed;

            string postMessage = Helpers.SerializeToJson(processEventPost, typeof(ProcessEventPost));
            string response = Beacon.PostToServer(postMessage, "/api/v1/ProcessEvent");
            if (response == "")
            {
                // Remote server could not be reached or encountered an error
                return false;
            }
            ProcessEventResponse processEventResponse = (ProcessEventResponse)Helpers.DeserializeFromJson(response, typeof(ProcessEventResponse));

            return true;
        }
    }
}
