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
using System.Security.Cryptography;

namespace srsvc
{
    class Helpers
    {
        /// <summary>
        /// Given a hex string, converts it to a byte array (ex. "4142" -> 0x41, 0x42)
        /// </summary>
        /// <param name="hexString"></param>
        /// <returns></returns>
        public static byte[] HexStringToByteArray(string hexString)
        {
            byte[] HexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < HexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                HexAsBytes[index] = byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            }
            return HexAsBytes;
        }

        /// <summary>
        /// Given a byte array, converts it to a hex string
        /// </summary>
        /// <param name="byteArray"></param>
        /// <returns></returns>
        public static string ByteArrayToHexString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        /// <summary>
        /// Given a file path, compute the file hashes.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="md5Hash"></param>
        /// <param name="sha1Hash"></param>
        /// <param name="sha256Hash"></param>
        public static void ComputeHashes(string filePath, out byte[] md5Hash, out byte[] sha1Hash, out byte[] sha256Hash)
        {
            using (var md5 = MD5Cng.Create())
            using (var sha1 = SHA1Cng.Create())
            using (var sha256 = SHA256Cng.Create())
            using (var input = File.OpenRead(filePath))
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    md5.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    sha1.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                    sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                }
                // We have to call TransformFinalBlock, but we don't have any
                // more data - just provide 0 bytes.
                md5.TransformFinalBlock(buffer, 0, 0);
                sha1.TransformFinalBlock(buffer, 0, 0);
                sha256.TransformFinalBlock(buffer, 0, 0);

                md5Hash = md5.Hash;
                sha1Hash = sha1.Hash;
                sha256Hash = sha256.Hash;
            }
        }

        /// <summary>
        /// Given a DateTime, converts it to seconds since the epoch
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public static Int32 ConvertToUnixTime(DateTime dt)
        {
            return (Int32)(dt.Subtract(new DateTime(1970, 1, 1).ToUniversalTime())).TotalSeconds;
        }

        /// <summary>
        /// Generates a stream object from the string, for JSON conversions
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static Stream GenerateStreamFromString(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        /// <summary>
        /// Converts the object of the given type to a JSON string
        /// </summary>
        /// <param name="o"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string SerializeToJson(object o, System.Type type)
        {
            MemoryStream memoryStream = new MemoryStream();
            DataContractJsonSerializer ser = new DataContractJsonSerializer(type);
            ser.WriteObject(memoryStream, o);

            memoryStream.Position = 0;
            StreamReader sr = new StreamReader(memoryStream);
            return sr.ReadToEnd();
        }

        /// <summary>
        /// Converts the JSON string to an object of the given type
        /// </summary>
        /// <param name="json"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static object DeserializeFromJson(string json, System.Type type)
        {
            object obj = Activator.CreateInstance(type);

            using (Stream s = GenerateStreamFromString(json))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(type);
                obj = ser.ReadObject(s);
            }

            return obj;
        }


        /// <summary>
        /// Prints the hex bytes for a long string to logger output as INFO
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static byte[] PrintBytes(string str)
        {
            byte[] bytes = new byte[str.Length * sizeof(char)];
            System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
            string line = "";
            Log.Info(str);
            for (int i = 0; i < bytes.Length; i++)
            {
                line += String.Format("{0:X2} ", bytes[i]);
                if (i == 0)
                {
                    // nop
                }
                else if ((i + 1) % 16 == 0)
                {
                    Log.Info(line);
                    line = "";
                }
                else if ((i + 1) % 8 == 0)
                {
                    line += " ";
                }
            }
            Log.Info(line);
            return bytes;
        }


        /// <summary>
        /// Search through byte array haystack for the needle and return it's index
        /// </summary>
        /// <param name="haystack"></param>
        /// <param name="needle"></param>
        /// <returns></returns>
        public static int Locate(byte[] haystack, byte[] needle)
        {
            int matchLocation = -1;
            for (int i = 0; i < haystack.Length - needle.Length; i++)
            {
                matchLocation = i;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        matchLocation = -1;
                        break;
                    }
                }
                if (matchLocation != -1) return matchLocation;
            }

            return -1;
        }


        /// <summary>
        /// Returns true if the byte arrays are equal 
        /// </summary>
        /// <param name="a1"></param>
        /// <param name="a2"></param>
        /// <returns></returns>
        public static bool ByteArrayAreEqual(byte[] a1, byte[] a2)
        {
            if (a1.Length != a2.Length)
                return false;

            for (int i = 0; i < a1.Length; i++)
                if (a1[i] != a2[i])
                    return false;

            return true;
        }
    }
}
