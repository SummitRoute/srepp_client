////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

#pragma once

//
// Globals
//
extern HANDLE g_QdDeviceHandle;
extern HMODULE g_hModule;

//
// Commands
//
BOOL QdInstallDriver();
BOOL QdUninstallDriver();
BOOL QdMonitor();

//
// Utility functions
//
BOOL QdInitialize();
BOOL QdUnInitialize();
BOOL QdCleanupSCM();

BOOL QdInitializeGlobals();
BOOL QdLoadDriver();
BOOL QdUnloadDriver();

BOOL QdCreateService();
BOOL QdDeleteService();
BOOL QdStartService();
BOOL QdStopService();

BOOL QdOpenDevice();
BOOL QdCloseDevice();
