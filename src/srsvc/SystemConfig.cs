////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.Reflection;
using System.Diagnostics;

namespace srsvc
{
    /// <summary>
    /// Struct to store the system config as pulled from the registry (or MSI)
    /// </summary>
    public class SystemConfig
    {
        private const string ConfigurationRegistryPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Summit Route\\SREPP\\";

        /// <summary>
        /// Agent version number
        /// </summary>
        private string _version;
        public string Version
        {
            get { return _version; }
            private set {
                setConfigValue("version", value);
                _version = value;
            }
        }

        /// <summary>
        /// Unique Group ID (customers control groups of agents)
        /// </summary>
        private string _groupUUID;
        public string GroupUUID
        {
            get { return _groupUUID; }
            private set {
                _groupUUID = value; 
                setConfigValue("groupUUID", value);
            }
        }

        /// <summary>
        /// Unique Agent ID, retrieved from server to ensure uniqueness
        /// </summary>
        private string _systemUUID;
        public string SystemUUID
        {
            get { return _systemUUID; }
            set
            {
                this._systemUUID = value;
                setConfigValue("systemUUID", value);
            }
        }

        /// <summary>
        /// Seconds between attempts to contact the server
        /// </summary>
        private int _beaconInterval;
        public int BeaconInterval
        {
            get { return _beaconInterval; }
            set
            {
                setConfigValue("beaconInterval", value.ToString());
                this._beaconInterval = value;
            }
        }


        /// <summary>
        /// 
        /// </summary>
        private string _beaconServer;
        public string BeaconServer
        {
            get { return _beaconServer; }
            set
            {
                setConfigValue("beaconServer", value);
                this._beaconServer = value;
            }
        }

        /// <summary>
        /// Initializer
        /// </summary>
        public SystemConfig()
        {
            if (!getConfigFromRegistry()) { 
                setDefaults();
            }

            debugPrintConfig();
        }

        /// <summary>
        /// Debug prints
        /// </summary>
        private void debugPrintConfig()
        {
            Log.Info("--- Configuration ---");
            Log.Info("  Group Guid: {0}", this.GroupUUID);
            Log.Info("  System Guid: {0}", this.SystemUUID);
            Log.Info("  Beacon interval: {0} seconds", this.BeaconInterval);
            Log.Info("  Beacon server: {0}", this.BeaconServer);
        }

        private string getConfigValue(string name, string defaultValue)
        {
            return (string)Registry.GetValue(ConfigurationRegistryPath, name, defaultValue);
        }

        private void setConfigValue(string name, string value)
        {
            Registry.SetValue(ConfigurationRegistryPath, name, value);
        }

        /// <summary>
        /// Read config values from the registry, or return false if we haven't registered yet
        /// </summary>
        /// <returns></returns>
        private bool getConfigFromRegistry()
        {
            // Reading values one by one.  Alternatively could store all as an encrypted blob.
            this._version = getConfigValue("version", "");
            if (this._version == "")
            {
                return false;
            }

            this._groupUUID = getConfigValue("groupUUID", "");
            this._systemUUID = getConfigValue("systemUUID", "");
            
            string beaconIntervalStr = getConfigValue("beaconInterval", "60");
            this._beaconInterval = Convert.ToInt32(beaconIntervalStr);

            this._beaconServer = getConfigValue("beaconServer", "");

            return true;
        }

        private void saveConfigToRegistry()
        {
            setConfigValue("version", this._version);
            setConfigValue("groupUUID", this._groupUUID);
            setConfigValue("systemUUID", this._systemUUID);
            setConfigValue("beaconInterval", this._beaconInterval.ToString());
            setConfigValue("beaconServer", this._beaconServer);
        }

        /// <summary>
        /// Has this system beaconed to the callback server and registered itself?
        /// </summary>
        /// <returns></returns>
        public bool HasRegistered()
        {
            if (this.SystemUUID == "") return false;
            return true;
        }

        
        /// <summary>
        /// Called on install in order to extract the data from the MSI
        /// </summary>
        private void setDefaults()
        {
            Log.Info("SREPP being installed for the first time, fill in default values");

            //
            // Get the groupUUID
            //
            string msiPath = (string)Registry.GetValue(ConfigurationRegistryPath, "installer_path", "");
            if (msiPath == "" || !File.Exists(msiPath))
            {
                Log.Info("Fresh install but no msi can be found... this is bad");
                throw new Exception("Unable to find installer");
            }
            byte[] configBytes = GetConfigurationFromMsi(msiPath);
            if (configBytes == null) {
                throw new Exception("Unable to extract config from MSI");
            }
            InstallerConfig installerConfig = ParseConfig(configBytes);
            // Sanity check
            if (installerConfig.groupUUID == "00000000-0000-0000-0000-000000000000")
            {
                throw new Exception("GroupUUID is null");
            }


            // Write extracted data to the registry
            this._groupUUID = installerConfig.groupUUID;
            this._systemUUID = ""; // Set the systemUUID to "" so we know we still need to register

            this._version = FileVersionInfo.GetVersionInfo(getCurrentFilePath()).FileVersion;

#if DEBUG
            this._beaconInterval = 5; // 5 seconds
            this._beaconServer = "http://192.168.106.129:8080";
#else
            this._beaconInterval = 5*60; // 5 minutes
            this._beaconServer = "https://beacon.summitroute.com";
#endif

            saveConfigToRegistry();


            // Set some default rules
            // TODO MUST Remove this rule
            /*
            var attrs = new List<RuleAttribute>();
            attrs.Add(new RuleAttribute
            {
                AttributeType = "Issuer",
                Attribute = "C=US, S=California, L=Mountain View, O=Google Inc, OU=Digital ID Class 3 - Java Object Signing, OU=Digital ID Class 3 - Java Object Signing, CN=Google Inc"
            });

            AddRuleToDB(new Rule
            {
                Allow = false,
                Comment = "Block Google",
                Attrs = attrs,
            });
             */
        }

        /// <summary>
        /// Return the file path for the current executable
        /// </summary>
        /// <returns></returns>
        private string getCurrentFilePath()
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            return Uri.UnescapeDataString(uri.Path);
            
        }

        /// <summary>
        /// Struct for pulling info from the MSI 
        /// </summary>
        private struct InstallerConfig
        {
            public string groupUUID;
        }

        /// <summary>
        /// Given a 16 byte array, return the UUID string it represents.
        /// </summary>
        /// <param name="uuidBytes"></param>
        /// <returns></returns>
        private string ConvertUUIDBytesToString(byte[] uuidBytes)
        {
            // I have to convert this on my own, because C#'s GUID class messes up byte ordering

            return String.Format("{0:x}{1:x}{2:x}{3:x}-{4:x}{5:x}-{6:x}{7:x}-{8:x}{9:x}-{10:x}{11:x}{12:x}{13:x}{14:x}{15:x}", 
                uuidBytes[0], uuidBytes[1], uuidBytes[2], uuidBytes[3],
                uuidBytes[4], uuidBytes[5],
                uuidBytes[6], uuidBytes[7],
                uuidBytes[8], uuidBytes[9],
                uuidBytes[10], uuidBytes[11], uuidBytes[12], uuidBytes[13], uuidBytes[14], uuidBytes[15]);
        }

        /// <summary>
        /// Given a blob of data extracted from the MSI, parse out the pieces
        /// </summary>
        /// <param name="configBytes"></param>
        /// <returns></returns>
        private InstallerConfig ParseConfig(byte[] configBytes)
        {
            // TODO Should use public key decryption on this
            InstallerConfig conf = new InstallerConfig();

            //
            // Unmarshal data
            //

            // Get UUID
            byte[] groupUUID = new byte[16];
            Array.Copy(configBytes, 0, groupUUID, 0, 16);

            conf.groupUUID = ConvertUUIDBytesToString(groupUUID);
            return conf;
        }

        /// <summary>
        /// Helper function to convert 4 bytes of an array to an int
        /// </summary>
        /// <param name="array"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private uint getInt(byte[] array, uint offset)
        {
            // Not bothering to sanity check because if someone messes with us, things will throw an exception anyway
            return (uint)((uint)array[offset] + ((uint)array[offset + 1] << 8) + ((uint)array[offset + 2] << 16) + ((uint)array[offset + 3] << 24));
        }

        /// <summary>
        /// Search through the MSI file for our configuration data
        /// </summary>
        /// <param name="msiPath"></param>
        /// <returns></returns>
        private byte[] GetConfigurationFromMsi(string msiPath)
        {
            // TODO MUST Parse the file properly
            using (BinaryReader br = new BinaryReader(File.Open(msiPath, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                int chunk_size = 8192;
                byte[] chunk = new byte[chunk_size];

                const uint BLOB_SIZE = 1024;

                // Tags to look for
                string caps = "---";  // Don't write out the whole string so we can avoid matching on this file
                byte[] start_string = System.Text.Encoding.ASCII.GetBytes(caps + "BEGIN_BLOB" + caps);
                byte[] end_string = System.Text.Encoding.ASCII.GetBytes(caps + "END_BLOB" + caps);

                // Read it
                chunk = br.ReadBytes(chunk_size);

                // Parse PE header to find certificate table
                uint image_nt_header_offset = getInt(chunk, 0x3c);
                uint cert_table_offset = getInt(chunk, image_nt_header_offset + 0x98); // I'm only dealing with 32-bit PE files, and I know the offset
                uint cert_table_size = getInt(chunk, image_nt_header_offset + 0x98 + 4);

                // Sanity check
                if (cert_table_size > 1024 * 1024)
                {
                    throw new Exception("Certificate table size is gigantic.  Something is going wrong.");
                }

                br.BaseStream.Seek(cert_table_offset, SeekOrigin.Begin);
                chunk = br.ReadBytes((int)cert_table_size);

                int matchLocation = Helpers.Locate(chunk, start_string);
                if (matchLocation != -1)
                {
                    // Start of blob found
                    int end = Helpers.Locate(chunk, end_string);
                    if (end == -1) {
                        throw new Exception("Unable to find our config data");
                    }

                    // Ensure the data is of the correct size
                    int end_tag_length = 15;
                    if ((end + end_tag_length - matchLocation) == BLOB_SIZE)
                    {
                        // Match found
                        int start_tag_length = 16;
                        byte[] confifByteArray = new byte[BLOB_SIZE - start_tag_length - end_tag_length];
                        Array.Copy(chunk, matchLocation + start_tag_length, confifByteArray, 0, confifByteArray.Length);

                        return confifByteArray;
                    }
                    else
                    {
                        throw new Exception("Blob size unexpected");
                    }
                }
                else
                {
                    throw new Exception("Configuration data now found");
                }

            }

            // We shouldn't get here
            throw new Exception("Configuration data now found");
        }
    }
}
