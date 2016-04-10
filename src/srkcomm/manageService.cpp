////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

#include "stdafx.h"
#include "..\common\log.h"
#include "..\common\drivercomm.h"
#include "manageService.h"

#include <Shlwapi.h>

//
// Globals
//

// Handle to the service
SC_HANDLE g_QdScmHandle = NULL;
HANDLE g_QdDeviceHandle = INVALID_HANDLE_VALUE;
WCHAR g_QdDriverPath[MAX_PATH];

HMODULE g_hModule;


//
// Local helpers
//
BOOL GetModuleFullName(
	__in  HMODULE  hModule,
	__out LPWSTR   *pszBuffer,
	__out PDWORD    nNumChars);


///////////////////////////////////////////////////////////////////////////////
///
///  Init global vars (driver path and the handle to the service)
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdInitializeGlobals()
{
	WCHAR SysDir[MAX_PATH];
	BOOL ReturnValue = FALSE;

	// TODO IMPORTANT don't disable redirection
#if !defined (_WIN64)
	//
	// Disable wow64 
	//

	BOOL Result = FALSE;
	BOOL Wow64Process = FALSE;
	PVOID OldWowRedirectionValue = NULL;

	Result = IsWow64Process(
		GetCurrentProcess(),
		&Wow64Process
		);
	if (Result == FALSE)
	{
		LOG_ERROR(L"IsWow64Process failed, last error 0x%x", GetLastError());
		goto Exit;
	}

	if (Wow64Process == TRUE)
	{
		//
		// Disable FS redirection to make sure a 32 bit test process will
		// copy our (64 bit) driver to system32\drivers rather than syswow64\drivers.
		//

		Result = Wow64DisableWow64FsRedirection(&OldWowRedirectionValue);
		if (Result == FALSE)
		{
			LOG_ERROR(L"Wow64DisableWow64FsRedirection failed, last error 0x%x", GetLastError());
			goto Exit;
		}
	}
#endif

	//
	// Open the service control manager if not already open
	//
	if (g_QdScmHandle == NULL) 
	{
		g_QdScmHandle = OpenSCManager(
			NULL,
			NULL,
			SC_MANAGER_ALL_ACCESS
			);

		if (g_QdScmHandle == NULL)
		{
			LOG_ERROR(L"OpenSCManager failed, last error 0x%x", GetLastError());
			goto Exit;
		}
	}

	//
	// Construct driver path g_TcDriverPath
	//
	UINT Size = GetSystemDirectory(SysDir, ARRAYSIZE(SysDir));
	if (Size == 0)
	{
		LOG_ERROR(L"GetSystemDirectory failed, last error 0x%x", GetLastError());
		goto Exit;
	}

	HRESULT hr = StringCchPrintf(
		g_QdDriverPath,
		ARRAYSIZE(g_QdDriverPath),
		L"%ls\\drivers\\%ls.sys",
		SysDir,
		QD_DRIVER_NAME
		);
	if (FAILED(hr))
	{
		LOG_ERROR(L"StringCchPrintf failed, hr 0x%08x", hr);
		goto Exit;
	}

	ReturnValue = TRUE;

Exit:
	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Close the service handle
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdCleanupSCM()
{
	if (g_QdScmHandle != NULL) 
	{
		CloseServiceHandle(g_QdScmHandle);
		g_QdScmHandle = NULL;
	}

	return TRUE;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Install and load the driver
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdLoadDriver()
{
	BOOL ReturnValue = FALSE;

	LOG_TRACE(L"Entering");

	//
	// Uninstall and unload any existing driver.
	//
	ReturnValue = QdUnloadDriver();
	if (ReturnValue != TRUE)
	{
		LOG_ERROR(L"QdUnloadDriver failed");
		goto Exit;
	}

	//
	// Set current working directory to the path this DLL is running from
	// 
	LPWSTR path = NULL;
	DWORD nNumChars = 0;
	if (!GetModuleFullName(g_hModule, &path, &nNumChars))
	{
		LOG_ERROR(L"GetModuleFullName failed");
		goto Exit;
	}

	// Get just the parent path
	PathRemoveFileSpec(path);

	// Set the current working directory
	if (!SetCurrentDirectory(path))
	{
		LOG_ERROR(L"SetCurrentDirectory failed");
		goto Exit;
	}


	//
	// Copy the driver to system32\drivers
	//
	ReturnValue = CopyFile(QD_DRIVER_NAME_WITH_EXT, g_QdDriverPath, FALSE);
	if (ReturnValue == FALSE)
	{
		LOG_ERROR(
			L"CopyFile(%ls, %ls) failed, last error 0x%x",
			QD_DRIVER_NAME_WITH_EXT, g_QdDriverPath, GetLastError()
			);

		goto Exit;
	}

	//
	// Install the driver.
	//
	ReturnValue = QdCreateService();
	if (ReturnValue == FALSE)
	{
		LOG_ERROR(L"QdCreateService failed");
		goto Exit;
	}

	//
	// Load the driver.
	//
	ReturnValue = QdStartService();
	if (ReturnValue == FALSE)
	{
		LOG_ERROR(L"QdStartService failed");
		goto Exit;
	}


	ReturnValue = TRUE;

Exit:
	LOG_TRACE(L"Exiting");
	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Unload the driver and delete the service
///
///////////////////////////////////////////////////////////////////////////////
BOOL QdUnloadDriver()
{
	BOOL ReturnValue = FALSE;

	LOG_TRACE(L"Entering");

	//
	// Unload the driver.
	//
	ReturnValue = QdStopService();
	if (ReturnValue == FALSE)
	{
		LOG_ERROR(L"TcStopService failed");
		goto Exit;
	}

	//
	// Delete the service.
	//
	ReturnValue = QdDeleteService();
	if (ReturnValue == FALSE)
	{
		LOG_ERROR(L"QdDeleteService failed");
		goto Exit;
	}

	ReturnValue = TRUE;

Exit:
	LOG_TRACE(L"Exiting");
	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Get service state
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdGetServiceState(
_In_ SC_HANDLE ServiceHandle,
_Out_ DWORD* State
)
{
	SERVICE_STATUS_PROCESS ServiceStatus;
	DWORD BytesNeeded;
	BOOL Result;

	*State = 0;

	Result = QueryServiceStatusEx(
		ServiceHandle,
		SC_STATUS_PROCESS_INFO,
		(LPBYTE)&ServiceStatus,
		sizeof(ServiceStatus),
		&BytesNeeded
		);
	if (Result == FALSE)
	{
		LOG_ERROR(L"QueryServiceStatusEx failed, last error 0x%x", GetLastError());
		return FALSE;
	}

	*State = ServiceStatus.dwCurrentState;

	return TRUE;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Wait for service to enter specified state.
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdWaitForServiceState(
_In_ SC_HANDLE ServiceHandle,
_In_ DWORD State
)
{
	DWORD ServiceState;
	BOOL Result;

	for (;;)
	{
		LOG_INFO(L"Waiting for service to enter state %u...", State);

		Result = QdGetServiceState(ServiceHandle, &ServiceState);
		if (Result == FALSE)
		{
			return FALSE;
		}

		if (ServiceState == State)
		{
			break;
		}

		Sleep(1000);
	}

	return TRUE;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Create the service
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdCreateService()
{
	BOOL ReturnValue = FALSE;

	LOG_TRACE(L"Entering");

	//
	// Create the service
	//
	SC_HANDLE ServiceHandle = CreateService(
		g_QdScmHandle,          // handle to SC manager
		QD_DRIVER_NAME,         // name of service
		QD_DRIVER_NAME,         // display name
		SERVICE_ALL_ACCESS,     // access mask
		SERVICE_KERNEL_DRIVER,  // service type
		SERVICE_DEMAND_START,   // start type
		SERVICE_ERROR_NORMAL,   // error control
		g_QdDriverPath,         // full path to driver
		NULL,                   // load ordering
		NULL,                   // tag id
		NULL,                   // dependency
		NULL,                   // account name
		NULL                    // password
		);
	DWORD LastError = GetLastError();
	if (ServiceHandle == NULL && LastError != ERROR_SERVICE_EXISTS)
	{
		LOG_ERROR(L"CreateService failed, last error 0x%x", LastError);
		goto Exit;
	}

	ReturnValue = TRUE;

Exit:
	if (ServiceHandle)
	{
		CloseServiceHandle(ServiceHandle);
	}

	LOG_TRACE(L"Exiting");
	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Start the service
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdStartService()
{
	BOOL ReturnValue = FALSE;

	//
	// Open the service. The function assumes that
	// TdCreateService has been called before this one
	// and the service is already installed.
	//
	SC_HANDLE ServiceHandle = OpenService(
		g_QdScmHandle,
		QD_DRIVER_NAME,
		SERVICE_ALL_ACCESS
		);
	if (ServiceHandle == NULL)
	{
		LOG_ERROR(L"OpenService failed, last error 0x%x", GetLastError());
		goto Exit;
	}

	//
	// Start the service
	//
	if (FALSE == StartService(ServiceHandle, 0, NULL))
	{
		if (GetLastError() != ERROR_SERVICE_ALREADY_RUNNING)
		{
			LOG_ERROR(L"TcStartService: StartService failed, last error 0x%x", GetLastError());
			goto Exit;
		}
	}

	//
	// Wait for the service to enter state SERVICE_RUNNING
	//
	if (FALSE == QdWaitForServiceState(ServiceHandle, SERVICE_RUNNING))
	{
		goto Exit;
	}

	ReturnValue = TRUE;

Exit:
	if (ServiceHandle)
	{
		CloseServiceHandle(ServiceHandle);
	}

	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Stop the service
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdStopService()
{
	BOOL ReturnValue = FALSE;

	LOG_TRACE(L"Entering");

	//
	// Open the service so we can stop it
	//
	SC_HANDLE ServiceHandle = OpenService(
		g_QdScmHandle,
		QD_DRIVER_NAME,
		SERVICE_ALL_ACCESS
		);

	DWORD LastError = GetLastError();
	if (ServiceHandle == NULL)
	{
		if (LastError == ERROR_SERVICE_DOES_NOT_EXIST)
		{
			ReturnValue = TRUE;
		}
		else
		{
			LOG_ERROR(L"OpenService failed, last error 0x%x", LastError);
		}

		goto Exit;
	}

	//
	// Stop the service
	//
	SERVICE_STATUS ServiceStatus;
	if (FALSE == ControlService(ServiceHandle, SERVICE_CONTROL_STOP, &ServiceStatus))
	{
		LastError = GetLastError();

		if (LastError != ERROR_SERVICE_NOT_ACTIVE)
		{
			LOG_ERROR(L"ControlService failed, last error 0x%x", LastError);
			goto Exit;
		}
	}

	if (FALSE == QdWaitForServiceState(ServiceHandle, SERVICE_STOPPED))
	{
		goto Exit;
	}

	ReturnValue = TRUE;

Exit:
	if (ServiceHandle)
	{
		CloseServiceHandle(ServiceHandle);
	}

	LOG_TRACE(L"Exiting");
	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Delete the service
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdDeleteService()
{
	BOOL ReturnValue = FALSE;

	LOG_TRACE(L"Entering");

	//
	// Open the service so we can delete it
	//
	SC_HANDLE ServiceHandle = OpenService(
		g_QdScmHandle,
		QD_DRIVER_NAME,
		SERVICE_ALL_ACCESS
		);

	DWORD LastError = GetLastError();

	if (ServiceHandle == NULL)
	{
		if (LastError == ERROR_SERVICE_DOES_NOT_EXIST)
		{
			ReturnValue = TRUE;
		}
		else
		{
			LOG_ERROR(L"OpenService failed, last error 0x%x", LastError);
		}

		goto Exit;
	}

	//
	// Delete the service
	//
	if (FALSE == DeleteService(ServiceHandle))
	{
		LastError = GetLastError();

		if (LastError != ERROR_SERVICE_MARKED_FOR_DELETE)
		{
			LOG_ERROR(L"DeleteService failed, last error 0x%x", LastError);
			goto Exit;
		}
	}

	ReturnValue = TRUE;

Exit:
	if (ServiceHandle)
	{
		CloseServiceHandle(ServiceHandle);
	}

	LOG_TRACE(L"Exiting");
	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
/// Open the device. Sets g_TcDeviceHandle
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdOpenDevice()
{
	BOOL ReturnValue = FALSE;

	//
	// Open the device if not already opened
	//
	if (g_QdDeviceHandle == INVALID_HANDLE_VALUE) 
	{
		g_QdDeviceHandle = CreateFile(
			QD_WIN32_DEVICE_NAME,
			GENERIC_READ | GENERIC_WRITE,
			0,
			NULL,
			OPEN_EXISTING,
			FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED,
			NULL
			);

		if (g_QdDeviceHandle == INVALID_HANDLE_VALUE)
		{
			LOG_ERROR(L"CreateFile(%ls) failed, last error 0x%x", QD_WIN32_DEVICE_NAME, GetLastError());
			goto Exit;
		}
	}

	ReturnValue = TRUE;

Exit:

	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
/// Close the device
///
///////////////////////////////////////////////////////////////////////////////
BOOL
QdCloseDevice()
{
	BOOL ReturnValue = FALSE;

	//
	// Close our handle to the device.
	//
	if (g_QdDeviceHandle != INVALID_HANDLE_VALUE)
	{
		CloseHandle(g_QdDeviceHandle);
		g_QdDeviceHandle = INVALID_HANDLE_VALUE;
	}

	ReturnValue = TRUE;

	return ReturnValue;
}



///////////////////////////////////////////////////////////////////////////////
///
/// Helper function, get's the filepath for this DLL
///
/// @param hModule
///			From the DLL initialization
/// @param pszBuffer
///			Pointer to buffer to hold path.  Must be free'd with LocalFree.
/// @param nNumChars
///			Number of characters in the path
///
///////////////////////////////////////////////////////////////////////////////
BOOL GetModuleFullName(
	__in  HMODULE  hModule,
	__out LPWSTR   *pszBuffer,
	__out PDWORD    nNumChars)
{
	DWORD nMaxChars = 256;

	for (;;) {
		if (*nNumChars >= 32768U)
		{
			// Buffer has grown too large, something bad is happening
			LOG_ERROR(L"GetModuleFullName unknown failure 0x%x", GetLastError());
			return FALSE;
		}

		*pszBuffer = (LPWSTR)LocalAlloc(LMEM_ZEROINIT, nMaxChars * sizeof(WCHAR));
		if (pszBuffer == NULL) {
			LOG_ERROR(L"GetModuleFullName mem alloc failed");
			return FALSE;
		}

		*nNumChars = GetModuleFileNameW(hModule, *pszBuffer, nMaxChars);
		if (nNumChars == 0) {
			LOG_ERROR(L"GetModuleFullName unknown failure 0x%x", GetLastError());
			return FALSE;
		}

		if (*nNumChars == nMaxChars) {
			// Buffer too small, free it, double the size, and try again
			LocalFree(*pszBuffer);
			nMaxChars *= 2;
		}
		else {
			break;
		}
	}

	return TRUE;
}