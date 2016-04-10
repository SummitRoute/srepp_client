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
using System.Diagnostics;

namespace srui
{
    class Log
    {
        enum Level { DEBUG, INFO, WARN, ERROR, CRITICAL };
        static string[] LevelName = new string[] { "DEBUG", "INFO", "WARN", "ERROR", "CRIT" };
        const string APP_NAME = "SRUI";

        public static void Debug(string format, params object[] args)
        {
            LogMessage((int)Level.DEBUG, format, args);
        }

        public static void Info(string format, params object[] args)
        {
            LogMessage((int)Level.INFO, format, args);
        }

        public static void Warn(string format, params object[] args)
        {
            LogMessage((int)Level.WARN, format, args);
        }

        public static void Error(string format, params object[] args)
        {
            LogMessage((int)Level.ERROR, format, args);
        }

        public static void Exception(Exception e, string format, params object[] args)
        {
            LogMessage((int)Level.ERROR, String.Format("{0}: {1}", String.Format(format, args), e.ToString()), null);
        }

        public static void Critical(string format, params object[] args)
        {
            LogMessage((int)Level.CRITICAL, format, args);
        }

        private static void LogMessage(int level, string format, params object[] args)
        {
            System.Diagnostics.Debug.WriteLine(String.Format("[{0} ({1})] {2}", LevelName[level], APP_NAME, String.Format(format, args)));
        }
    }
}
