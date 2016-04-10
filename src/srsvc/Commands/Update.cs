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
using System.Diagnostics;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

using System.Web.Helpers;
using Microsoft.CSharp.RuntimeBinder;

namespace srsvc
{
    public partial class Command
    {

          [DataContract]
        internal class GetUpdatePost : Event.EventPost
        {
            [DataMember]
            internal string Version = "";

            public GetUpdatePost(string CurrentVersion)
            {
                Version = CurrentVersion;
            }
        }

            

        /// <summary>
        /// Update this agent
        /// </summary>
        /// <param name="serverMsg">Ignored, but provides what the new version will be</param>
        /// <returns></returns>
        public static dynamic Update(dynamic serverMsg)
        {
            // Tell the server what version we currently have and it will return an exe that will update this agent to the next version
            GetUpdatePost getUpdatePost = new GetUpdatePost(SRSvc.conf.Version);

            string postMessage = Helpers.SerializeToJson(getUpdatePost, typeof(GetUpdatePost));
            byte[] filedata = Beacon.PostForm(Encoding.UTF8.GetBytes(postMessage), "/api/v1/GetUpdate", "text/json");

            //string updateName = String.Format("srepp_update-{0}.exe", SRSvc.conf.Version);
            string updateFilePath = Path.GetTempFileName();
            try
            {
                using (FileStream fs = File.Create(updateFilePath))
                {
                    using (System.IO.BinaryWriter file = new System.IO.BinaryWriter(fs))
                    {
                        file.Write(filedata);
                    }
                }

                Log.Info("Update file written to: {0}", updateFilePath);

                // Check it
                List<Signer> signers = null;
                bool isVerified = WinTrustVerify.WinTrust.Verify(updateFilePath, out signers);
                if (!isVerified)
                {
                    Log.Error("Update file could not be verified");
                    return null;
                }

                // File is signed and can be verified, but make sure it's signed by me
                string SignerName = "";
                byte[] SerialNumber = null;
                if (signers != null && signers.Count >= 1 && signers[0] != null)
                {
                    SignerName = signers[0].Name;
                    SerialNumber = signers[0].SigningCert.SerialNumber;
                }
                else
                {
                    Log.Error("Could not extract signer info from file");
                    return null;
                }

                bool isSignedByMe = false;
                // TODO Should also check the chain (the root) and some other info, in case a rogue CA granted someone a cert with my info

#if DEBUG
                byte[] TestSerialNumber = new byte[] { 0x4c, 0x18, 0xd9, 0xaa, 0x26, 0x83, 0x36, 0x95, 0x4c, 0xc4, 0x35, 0x6d, 0xaa, 0xbe, 0x82, 0xc9 };
                if (SignerName == "SR_Test" && Helpers.ByteArrayAreEqual(SerialNumber, TestSerialNumber))
                {
                    isSignedByMe = true;
                }
#endif

                // TODO MUST Check Relase build requirements
                byte[] ReleaseSerialNumber = new byte[] { 0x4c, 0x18, 0xd9, 0xaa, 0x26, 0x83, 0x36, 0x95, 0x4c, 0xc4, 0x35, 0x6d, 0xaa, 0xbe, 0x82, 0xc9 };
                if (SignerName == "SR_Test" && Helpers.ByteArrayAreEqual(SerialNumber, ReleaseSerialNumber))
                {
                    isSignedByMe = true;
                }

                if (!isSignedByMe)
                {
                    Log.Error("Update file was not signed by the correct issuer!  Bailing on update. Was signed by {0}, with serial number {1}", SignerName, Helpers.ByteArrayToHexString(SerialNumber));
                    return null;
                }

                // TODO Check the new file is a greater version than this file, or at least the date the file was signed is more recent

                // Execute it
                Process myProcess = new Process();
                try
                {
                    myProcess.StartInfo.UseShellExecute = false;
                    myProcess.StartInfo.FileName = updateFilePath;
                    myProcess.StartInfo.CreateNoWindow = true;
                    myProcess.Start();

                    // TODO Should I kill this parent process so the child isn't trying kill it?
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            finally
            {
                // Delete file, especially in case of errors.  In theory an update should occcur.
                File.Delete(updateFilePath);
            }

            return null;
        }

    }
}
