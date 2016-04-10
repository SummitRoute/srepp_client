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
    class Beacon
    {
        bool bRunning = true;

        private static readonly Encoding encoding = Encoding.UTF8;

        /// <summary>
        /// Sends HTTP POST message to the server
        /// </summary>
        /// <param name="postMessage"></param>
        /// <param name="route"></param>
        /// <returns></returns>
        public static string PostToServer(string postMessage, string route, string contentType = "text/json")
        {
            return encoding.GetString(PostForm(encoding.GetBytes(postMessage), route, contentType));
        }

        /// <summary>
        /// Used by PostForm
        /// </summary>
        /// <param name="route"></param>
        /// <param name="postParameters"></param>
        /// <returns></returns>
        public static string MultipartFormDataPost(string route, Dictionary<string, object> postParameters)
        {
            string formDataBoundary = String.Format("----------{0:N}", Guid.NewGuid());
            string contentType = "multipart/form-data; boundary=" + formDataBoundary;

            byte[] formData = GetMultipartFormData(postParameters, formDataBoundary);

            return encoding.GetString(PostForm(formData, route, contentType));
        }

        /// <summary>
        /// Used by PostForm
        /// </summary>
        /// <param name="postParameters"></param>
        /// <param name="boundary"></param>
        /// <returns></returns>
        private static byte[] GetMultipartFormData(Dictionary<string, object> postParameters, string boundary)
        {
            Stream formDataStream = new System.IO.MemoryStream();
            bool needsCLRF = false;

            foreach (var param in postParameters)
            {
                // Thanks to feedback from commenters, add a CRLF to allow multiple parameters to be added.
                // Skip it on the first parameter, add it to subsequent parameters.
                if (needsCLRF)
                    formDataStream.Write(encoding.GetBytes("\r\n"), 0, encoding.GetByteCount("\r\n"));

                needsCLRF = true;

                if (param.Value is FileParameter)
                {
                    FileParameter fileToUpload = (FileParameter)param.Value;

                    // Add just the first part of this param, since we will write the file data directly to the Stream
                    string header = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"; filename=\"{2}\"\r\nContent-Type: {3}\r\n\r\n",
                        boundary,
                        param.Key,
                        fileToUpload.FileName ?? param.Key,
                        fileToUpload.ContentType ?? "application/octet-stream");

                    formDataStream.Write(encoding.GetBytes(header), 0, encoding.GetByteCount(header));

                    // Write the file data directly to the Stream, rather than serializing it to a string.
                    formDataStream.Write(fileToUpload.File, 0, fileToUpload.File.Length);
                }
                else
                {
                    string postData = string.Format("--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}",
                        boundary,
                        param.Key,
                        param.Value);
                    formDataStream.Write(encoding.GetBytes(postData), 0, encoding.GetByteCount(postData));
                }
            }

            // Add the end of the request.  Start with a newline
            string footer = "\r\n--" + boundary + "--\r\n";
            formDataStream.Write(encoding.GetBytes(footer), 0, encoding.GetByteCount(footer));

            // Dump the Stream into a byte[]
            formDataStream.Position = 0;
            byte[] formData = new byte[formDataStream.Length];
            formDataStream.Read(formData, 0, formData.Length);
            formDataStream.Close();

            return formData;
        }


        public class FileParameter
        {
            public byte[] File { get; set; }
            public string FileName { get; set; }
            public string ContentType { get; set; }
            public FileParameter(byte[] file) : this(file, null) { }
            public FileParameter(byte[] file, string filename) : this(file, filename, null) { }
            public FileParameter(byte[] file, string filename, string contenttype)
            {
                File = file;
                FileName = filename;
                ContentType = contenttype;
            }
        }


        /// <summary>
        /// PostForm sends data to the server.  This function and helpers were largely taken from http://www.briangrinstead.com/blog/multipart-form-post-in-c
        /// </summary>
        /// <param name="formData"></param>
        /// <param name="route"></param>
        /// <param name="contentType"></param>
        /// <returns></returns>
        public static byte[] PostForm(byte[] formData, string route, string contentType = "text/json")
        {
            // TODO Use proxy settings
            // TODO Check HTTPS thoroughly

            string sURL;
            byte[] result = null;
            sURL = SRSvc.conf.BeaconServer + route;

            try
            {
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(sURL);
                httpWebRequest.ContentType = contentType;
                httpWebRequest.Method = "POST";
                httpWebRequest.Proxy = null; // TODO REMOVE, this adds 7 seconds, so need to decide when it's needed
                httpWebRequest.ContentLength = formData.Length;

                using (Stream streamWriter = httpWebRequest.GetRequestStream())
                {
                    streamWriter.Write(formData, 0, formData.Length);
                    streamWriter.Flush();
                    streamWriter.Close();
                }

                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (BinaryReader br = new BinaryReader(httpResponse.GetResponseStream()))
                {
                    result = br.ReadBytes((int)httpResponse.ContentLength);
                    br.Close();
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Just swallow the exception if we're not able to connect
                Log.Error("Unable to contact the callback server");
            }
            catch (Exception e)
            {
                Log.Exception(e, "Exception in PostToServer");
            }

            return result;
        }


        /// <summary>
        /// Given a server response, it does what it is being asked, and tells the server, which sends back a server reponse, which is returned by this function.
        /// </summary>
        /// <param name="serverMsg"></param>
        /// <returns></returns>
        private dynamic HandleServerResponse(dynamic serverMsg)
        {
            dynamic response = null;

            try
            {
                string command = serverMsg.Command;

                if (command == "GetCatalogFileByHash")
                {
                    response = Command.GetCatalogFileByHash(serverMsg);
                }
                else if (command == "GetFileByHash")
                {
                    response = Command.GetFileByHash(serverMsg);
                }
                else if (command == "SetSystemUUID")
                {
                    response = Command.SetSystemUUID(serverMsg);
                }
                else if (command == "Update")
                {
                    response = Command.Update(serverMsg);
                }
                else if (command == "NOP")
                {
                    response = Command.NOP(serverMsg);
                }
                else if (command == "Stall")
                {
                    response = Command.Stall(serverMsg);
                }
                else
                {
                    Log.Error("Unknown command: {0}", command);
                }
            }
            catch (RuntimeBinderException e)
            {
                Log.Exception(e, "JSON did not include expected value");
                return false;
            }
            return response;
        }


        /// <summary>
        /// Contacts the server from our beacon loop
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public bool ContactServer(NHibernate.ISession session)
        {
            bool ContactedServer = false;

            // Get info about all the new process events
            var processEvents = session.QueryOver<ProcessEvent>()
                .Where(e => e.HasInformedServer == false)
                .List<ProcessEvent>();
            if (processEvents.Count() != 0)
            {
                foreach (var processEvent in processEvents)
                {
                    // Get info about the exe associated ith this process

                    Executable executable = null;
                    try
                    {
                        executable = session.QueryOver<Executable>()
                            .Where(e => e.Id == processEvent.ExecutableId)
                            .List<Executable>().First();
                    }
                    catch (Exception e)
                    {
                        // TODO Need to log this error
                        Log.Exception(e, "Unable to find an executable for process event {0}", processEvent.Id);
                        // I'll record in the DB that we already informed the server about this broken event
                    }

                    using (var transaction = session.BeginTransaction())
                    {
                        if (executable == null)
                        {
                            // The executable for this process event was not found, so something broke, so just ignore it
                            processEvent.HasInformedServer = true;
                            session.Save(processEvent);
                            transaction.Commit();
                        }
                        else
                        {
                            if (Event.PostProcessEvent(processEvent, executable))
                            {
                                // Record that we sent this data to the server so we don't try sending it again
                                processEvent.HasInformedServer = true;
                                session.Save(processEvent);
                                transaction.Commit();
                            }

                            ContactedServer = true;
                        }
                    }
                }
            }

            // Get info about all the new catalog files
            var catalogFiles = session.QueryOver<CatalogFile>()
                .Where(e => e.HasInformedServer == false)
                .List<CatalogFile>();
            if (catalogFiles.Count() != 0)
            {
                foreach (var catalogFile in catalogFiles)
                {
                    using (var transaction = session.BeginTransaction())
                    {
                        if (Event.PostCatalogFile(catalogFile))
                        {
                            // Record that we sent this data to the server so we don't try sending it again
                            catalogFile.HasInformedServer = true;
                            session.Save(catalogFile);
                            transaction.Commit();
                        }
                    }

                    ContactedServer = true;
                }
            }

            if (!ContactedServer)
            {
                Log.Info("Posting heartbeat");
                dynamic eventResponse = Event.PostHeartbeatEvent();
                int avoidInfiniteLoop = 100;
                while (eventResponse != null)
                {
                    // Sanity check, want to avoid infinite loop
                    avoidInfiniteLoop--;
                    if (avoidInfiniteLoop < 0)
                    {
                        break;
                    }

                    eventResponse = HandleServerResponse(eventResponse);
                }
            }

            return ContactedServer;
        }


        public void Stop()
        {
            bRunning = false;
        }


        /// <summary>
        /// Thread to contact the server periodically with any new information acquired
        /// </summary>
        public void Run()
        {
            Log.Debug("Beacon thread started");
            bool ContactedServer = false;

            // Loop and sleep for 60s, and sending anything new
            var sessionFactory = Database.getSessionFactory();
            using (var session = sessionFactory.OpenSession())
            {
                while (bRunning)
                {
                    try
                    {
                        Log.Debug("Start of loop");

                        // If we haven't contacted the server yet, then we try to post a heart beat
                        ContactedServer = false;

                        if (!SRSvc.conf.HasRegistered())
                        {
                            if (Event.RegisterWithServer())
                            {
                                ContactedServer = true;
                            }
                        }
                        else
                        {
                            ContactedServer = ContactServer(session);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Exception(e, "Exception in beacon loop");
                    }

                    System.Threading.Thread.Sleep(SRSvc.conf.BeaconInterval * 1000);
                }
            }
        }
    }
}
