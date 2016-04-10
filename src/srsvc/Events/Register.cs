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
using System.Management;
using System.Web.Helpers;

namespace srsvc
{
    partial class Event
    {

        /// <summary>
        /// Data to send to the server when registering.  Tells the server about this client and this is used to request a system UUID
        /// </summary>
        [DataContract]
        internal class RegistrationEventPost
        {
            [DataMember]
            internal string CustomerUUID;

            [DataMember]
            internal string AgentVersion;

            [DataMember]
            internal string OSHumanName;

            [DataMember]
            internal string OSVersion;

            [DataMember]
            internal string Manufacturer;

            [DataMember]
            internal string Model;

            [DataMember]
            internal string Arch;

            [DataMember]
            internal string MachineName;

            [DataMember]
            internal string MachineGUID;
        }

        /// <summary>
        /// Retrieves the OS name as something like "Microsoft Windows 7 Enterprise"
        /// </summary>
        /// <returns></returns>
        private static string GetOSFriendlyName()
        {
            string result = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
            foreach (ManagementObject os in searcher.Get())
            {
                result = os["Caption"].ToString();
                break;
            }
            return result.Trim();
        }

        /// <summary>
        /// Gets the value from HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Cryptography
        /// </summary>
        /// <returns></returns>
        private static string GetWindowsMachineGUID()
        {
            string result = string.Empty;
            ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_OperatingSystem");
            foreach (ManagementObject os in searcher.Get())
            {
                result = os["SerialNumber"].ToString();
                break;
            }
            return result.Trim();
        }


        private struct ManufacturerData
        {
            public string Manufacturer;
            public string Model;
        }

        private static ManufacturerData GetManufacturerData()
        {
            ManufacturerData md = new ManufacturerData();

            // create management class object
            ManagementClass mc = new ManagementClass("Win32_ComputerSystem");
            //collection to store all management objects
            ManagementObjectCollection moc = mc.GetInstances();
            if (moc.Count != 0)
            {
                foreach (ManagementObject mo in mc.GetInstances())
                {
                    // display general system information
                    md.Manufacturer = mo["Manufacturer"].ToString();
                    md.Model = mo["Model"].ToString();
                    break;
                }
            }
            return md;
        }


        /// <summary>
        /// Register this agent with the server.  Receives new System UUID.
        /// </summary>
        /// <returns>True when we've received a new System UUID</returns>
        public static bool RegisterWithServer()
        {
            Log.Info("Registering with the server");

            // Register yourself in order to get a System GUID
            RegistrationEventPost registration = new RegistrationEventPost();
            registration.CustomerUUID = SRSvc.conf.GroupUUID;
            registration.AgentVersion = SRSvc.conf.Version;

            registration.OSHumanName = GetOSFriendlyName();
            registration.OSVersion = Environment.OSVersion.ToString();
            registration.Arch = (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
            registration.MachineName = Environment.MachineName;

            ManufacturerData md = GetManufacturerData();
            registration.Manufacturer = md.Manufacturer;
            registration.Model = md.Model;
            registration.MachineGUID = GetWindowsMachineGUID();

            string postMessage = Helpers.SerializeToJson(registration, typeof(RegistrationEventPost));

            string response = Beacon.PostToServer(postMessage, "/api/v1/Register");
            if (response == "")
            {
                // Likely we were unable to contact the server
                return false;
            }

            dynamic serverMsg = Json.Decode(response);

            if (serverMsg.Command == "SetSystemUUID")
            {
                Command.SetSystemUUID(serverMsg);
            }
            else
            {
                throw new Exception("Registration response command not in correct format");
            }

            return true;
        }
    }
}
