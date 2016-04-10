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
        public static dynamic GetCatalogFileByHash(dynamic serverMsg)
        {
            dynamic response = null;

            string sha256 = serverMsg.Arguments.Sha256;
            Log.Info("Command GetCatalogFileByHash: Hash {0}", sha256);

            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                var catalogs = session.QueryOver<CatalogFile>()
                    .Where(e => e.Sha256 == Helpers.HexStringToByteArray(sha256))
                    .List<CatalogFile>();
                if (catalogs.Count() != 0)
                {
                    CatalogFile catalog = catalogs[0];
                    Log.Info("Found file {0}", catalog.FilePath);

                    response = UploadFile(catalog.Sha256, catalog.FilePath, "catalog");
                }
                else
                {
                    // TODO Need to throw error
                    Log.Error("GetCatalogFileByHash requested unknown hash {0}", sha256);
                }
            }

            return response;
        }
    }
}
