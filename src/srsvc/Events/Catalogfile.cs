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

        [DataContract]
        internal class CatalogFilePost : EventPost
        {
            [DataMember]
            internal Int64 TimeOfEvent = 0;

            [DataMember]
            internal string Path = "";

            [DataMember]
            internal string Sha256 = "";

            [DataMember]
            internal Int32 Size = 0;
        }


        /// <summary>
        /// Response after sending a catalog file info
        /// </summary>
        [DataContract]
        internal class CatalogFileResponse
        {
            // nop
        }


        public static bool PostCatalogFile(CatalogFile catalogFile)
        {
            CatalogFilePost catalogFilePost = new CatalogFilePost();
            catalogFilePost.TimeOfEvent = Helpers.ConvertToUnixTime(catalogFile.FirstAccessTime);
            catalogFilePost.Path = catalogFile.FilePath;
            catalogFilePost.Size = catalogFile.Size;
            catalogFilePost.Sha256 = Helpers.ByteArrayToHexString(catalogFile.Sha256);

            string postMessage = Helpers.SerializeToJson(catalogFilePost, typeof(CatalogFilePost));
            string response = Beacon.PostToServer(postMessage, "/api/v1/CatalogFileEvent");
            if (response == "")
            {
                // Remote server could not be reached or encountered an error
                return false;
            }
            CatalogFileResponse processEventResponse = (CatalogFileResponse)Helpers.DeserializeFromJson(response, typeof(CatalogFileResponse));

            return true;
        }
    }
}
