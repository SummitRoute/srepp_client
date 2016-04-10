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
        [DataContract]
        internal class UploadFilePost : Event.EventPost
        {
            [DataMember]
            internal string Sha256 = "";

            [DataMember]
            internal string FileType = "";
        }

        /// <summary>
        /// This function is wrapped by other calls in order to upload files
        /// </summary>
        /// <param name="Sha256ByteArray"></param>
        /// <param name="FilePath"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static dynamic UploadFile(byte[] Sha256ByteArray, string FilePath, string type)
        {
            UploadFilePost eventPost = new UploadFilePost();
            string sha256HexString = Helpers.ByteArrayToHexString(Sha256ByteArray);
            eventPost.Sha256 = sha256HexString;
            eventPost.FileType = type;

            string postMessage = Helpers.SerializeToJson(eventPost, typeof(UploadFilePost));

            // Read file data
            // TODO Need to be smarter about avoiding reading the entire file into memory at once
            FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] data = new byte[fs.Length];
            fs.Read(data, 0, data.Length);
            fs.Close();

            // Generate post objects
            Dictionary<string, object> postParameters = new Dictionary<string, object>();
            postParameters.Add("event_data", postMessage);
            postParameters.Add("file", new Beacon.FileParameter(data, sha256HexString, "application/octet-stream"));

            string response = Beacon.MultipartFormDataPost("/api/v1/UploadFile", postParameters);

            return Json.Decode(response);
        }

        public static dynamic GetFileByHash(dynamic serverMsg)
        {
            dynamic response = null;

            // Test with: insert into tasks (systemid, creationdate, deployedtoagentdate, command) values (1, 1415308642, 0, '{"Command":"GetFileByHash","Arguments":{"Sha256":"0a8ce026714e03e72c619307bd598add5f9b639cfd91437cb8d9c847bf9f6894"}}');
            string sha256 = serverMsg.Arguments.Sha256;
            Log.Info("Command GetFileByHash: Hash {0}", sha256);

            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                var exes = session.QueryOver<Executable>()
                    .Where(e => e.Sha256 == Helpers.HexStringToByteArray(sha256))
                    .List<Executable>();
                if (exes.Count() != 0)
                {
                    Executable exe = exes[0];
                    Log.Info("Found file {0}", exe.Path);

                    response = UploadFile(exe.Sha256, exe.Path, "exe");
                }
                else
                {
                    // TODO Need to throw error
                    Log.Error("GetFileByHash requested unknown hash: {0}", sha256);
                }
            }

            return response;
        }
    }
}
