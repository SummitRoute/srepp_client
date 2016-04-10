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
        /// Do nothing
        /// </summary>
        /// <param name="serverMsg"></param>
        /// <returns></returns>
        public static dynamic NOP(dynamic serverMsg)
        {
            Log.Info("Command NOP");

            return null;
        }
    }
}
