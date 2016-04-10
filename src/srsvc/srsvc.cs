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
using System.Text;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Management;
using System.Diagnostics;

namespace srsvc
{
    /// <summary>
    /// Communicates with the driver
    /// </summary>
    public class SRSvc
    {
        static Thread beaconThread = null;
        static Beacon beacon = null;

        public static bool bRunning = true;

        public void Stop()
        {
            bRunning = false;
            QdUnInitialize();

            if (beaconThread != null)
            {
                beacon.Stop();
                while (beaconThread.IsAlive);
            }
        }

        public static SystemConfig conf;

        public struct PROCESS_INFO
        {
            public UInt32 pid;
            public UInt32 ppid;

            public string ImageFileName;
            public string CommandLine;

            public PROCESS_INFO(COMM_CREATE_PROC comm_Create_Proc)
            {
                pid = comm_Create_Proc.pid;
                ppid = comm_Create_Proc.ppid;
                CommandLine = new string(comm_Create_Proc.CommandLineBuf, 0, comm_Create_Proc.CommandLineLength / 2);
                ImageFileName = new string(comm_Create_Proc.ImageFileNameBuf, 0, comm_Create_Proc.ImageFileNameLength / 2);
            }

            public PROCESS_INFO(Process process)
            {
                pid = (UInt32)process.Id;
                ppid = 0;
                CommandLine = "";
                ImageFileName = process.MainModule.FileName;

                string query = String.Format("SELECT CommandLine, ParentProcessId FROM Win32_Process WHERE ProcessId = {0}", pid);
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject proc in searcher.Get())
                {
                    ppid = (UInt32)proc["ParentProcessId"];
                    CommandLine = (string)proc["CommandLine"].ToString();
                    break;
                }
            }
        }

        /// <summary>
        /// Struct passed from driver to userland to tell it what new process was created
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct COMM_CREATE_PROC
        {
            public UInt32 Size;

            // TODO Get bit flags
            public UInt32 Flags;

            public UInt32 pid;
            public UInt32 ppid;
            public UInt32 ptid;

            public UInt16 ImageFileNameLength;
            public UInt32 ImageFileNameFullLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
            public char[] ImageFileNameBuf;

            public UInt16 CommandLineLength; // Potentially truncated to 1024
            public UInt32 CommandLineFullLength; // Full length before truncation
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
            public char[] CommandLineBuf;

            public UInt16 ProcIndex;
            public UInt16 IntegrityCheck;
        }

        // 
        // srkcomm dll functions
        //

        // Initialize the dll
        [DllImport("srkcomm.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern Boolean QdInitialize();

        [DllImport("srkcomm.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern Boolean QdUnInitialize();

        // Installs the kernel driver
        [DllImport("srkcomm.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern Boolean QdInstallDriver();

        // Uninstalls the driver
        [DllImport("srkcomm.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern Boolean QdUninstallDriver();

        // Define callback for QdMonitor
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate UInt32 processMonitorCallbackDelegate(ref COMM_CREATE_PROC data);
        
	    //  Retrieve info about new processes being created from the driver
        [DllImport("srkcomm.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern Boolean QdMonitor(processMonitorCallbackDelegate cb);

	    // Tell the driver to allow or deny a process
	    [DllImport("srkcomm.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern Boolean QdControl(UInt16 decision, UInt16 integrityCheck);

        // Global
        static processMonitorCallbackDelegate processMonitorCallback; // Ensure it doesn't get garbage collected

        /// <summary>
        /// Tell the driver to not run the process.  Also tell the UI to inform the UI a process was blocked.
        /// </summary>
        /// <param name="d"></param>
        /// <param name="createProc"></param>
        /// <param name="filePath"></param>
        public static void CommunicateProcessDecision(Decision d, ref COMM_CREATE_PROC createProc, string filePath)
        {
            if (d == Decision.DENY)
            {
                MessagingInterfaces.UIComm.InformUI(string.Format("Stopping process from running: {0}", filePath));
            }
            QdControl((UInt16)d, createProc.IntegrityCheck);
        }

        /// <summary>
        /// Callback is called when a new process is created so we can log it and decide to block it.
        /// </summary>
        /// <param name="createProc"></param>
        /// <returns></returns>
        public static UInt32 ProcessMonitorCallback(ref COMM_CREATE_PROC createProc)
        {
            try
            {
                string imageFileName = new string(createProc.ImageFileNameBuf, 0, createProc.ImageFileNameLength/2);
                string cmdLine = new string(createProc.CommandLineBuf, 0, createProc.CommandLineLength/2);

                Log.Info("New process: {0}", imageFileName);
                Log.Info("  Cmd line: {0}", new string(createProc.CommandLineBuf));

                long ExecutableId;

                Decision decision = Arbiter.DecideOnProcess(imageFileName, out ExecutableId);
                PROCESS_INFO processInfo = new PROCESS_INFO(createProc);
                Database.LogProcessEvent(processInfo, ExecutableId, Database.ProcessState.Started);

                CommunicateProcessDecision(decision, ref createProc, imageFileName);
            }
            catch (Exception e)
            {
                Log.Exception(e, "Exception in ProcessMonitorCallback");
            }

            return 0;
        }


        /// <summary>
        /// Connect the kernel driver
        /// </summary>
        public SRSvc()
        {
            try
            {
                if (!QdInitialize())
                {
                    // TODO: Handle error better
                    Log.Error("Failed to initialize srkcomm");
                }
            }
            catch (Exception e)
            {
                Log.Exception(e, "Exception initializing srkcomm, likely srkcomm.dll is not available");
                System.Environment.Exit(1);
            }
        }


        /// <summary>
        /// Run when this service is first started.  Most useful for at install, or anything that starts before us on boot.
        /// Check the currently executing processes and records info about them and also checks them against our rules to note 
        /// anything that should not have been running already (TODO Need to alert about that)
        /// </summary>
        public void AnalyzeRunningProcesses()
        {
            foreach(Process process in Process.GetProcesses()) {
                try
                {
                    Log.Info("Process: {0} ID: {1}", process.ProcessName, process.Id);
                    string fileName = process.Modules[0].FileName;
                    Log.Info("File name: {0}", fileName);
                    long ExecutableId;
                    if (Arbiter.DecideOnProcess(fileName, out ExecutableId) == Decision.DENY)
                    {
                        Log.Warn("*** This file should not be running: {0}", fileName);
                        // File running that should not be.
                        // TODO Alert and/or Kill this process
                    }

                    PROCESS_INFO processInfo = new PROCESS_INFO(process);
                    Database.LogProcessEvent(processInfo, ExecutableId, Database.ProcessState.Exists);

                } catch (Exception e) {
                    if (process.Id == 0 || process.Id == 4)
                    {
                        // "System" (4) and "idle" (0) processes cannot have their modules enumerated,
                        // so ignore them
                    }
                    else if (process.HasExited)
                    {
                        // no-op, just an exception because the process already exited
                    }
                    else
                    {
                        Log.Exception(e, "Exception reading process: {0}", process.ProcessName);
                    }       
                }
            }
        }

        public void Run()
        {
            Log.Info("Starting infinite loop");
            try 
            {
                conf = new SystemConfig();
                MessagingInterfaces.UIComm.Init(); // Init static class
                AnalyzeRunningProcesses();
                processMonitorCallback = new processMonitorCallbackDelegate(ProcessMonitorCallback);

                // Start thread that communites with our server
                beacon = new Beacon();
                beaconThread = new Thread(new ThreadStart(beacon.Run));
                beaconThread.Name = "BeaconThread";
                beaconThread.IsBackground = true;
                beaconThread.Start();
                
                while (QdMonitor(processMonitorCallback))
                {
                    if (!bRunning) return;  // Using return instead of break so I can detect failures
                }

                Log.Info("Broke out of QdMonitor loop. This should not happen.");
            }
            catch (Exception e)
            {
                Log.Exception(e, "Exception in Service.Run");
                // This is really bad, so rethrow the exception so we exit
                throw e;
            }
        }

        ~SRSvc()
        {
            Log.Info("SRSvc: cleanup");
            if (!QdUnInitialize())
            {
                // TODO: Handle error better
                Log.Error("SRSvc: Failed to uninitialize srkcomm");
            }
        }
    }
}
