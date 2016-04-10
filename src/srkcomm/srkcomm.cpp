////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

// srsvc.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "..\common\log.h"
#include "..\common\srkcomm.h"
#include "..\common\drivercomm.h"
#include "manageService.h"

static HANDLE CommRequestEvent = NULL;
static bool bRunning = true;

///////////////////////////////////////////////////////////////////////////////
///
///  Retrieve info about new processes being created from the driver
///
///////////////////////////////////////////////////////////////////////////////
__declspec(dllexport)
BOOL
QdMonitor(t_processMonitorCallback processMonitorCallback)
{
	BOOL ReturnValue = FALSE;
	OVERLAPPED CommRequestOverlapped = { 0 };
	COMM_REQUEST CommRequest = { 0 };

	// Open a handle to the device.
	ReturnValue = QdOpenDevice();
	if (ReturnValue != TRUE)
	{
		LOG_ERROR(_T("QdOpenDevice failed"));
		goto Exit;
	}

	//
	// Init overlapped structured
	//
	CommRequestOverlapped.hEvent = CommRequestEvent;
	ResetEvent(CommRequestEvent);

	CommRequest.CommControlRequest.RequestBuffer = &CommRequest.CommRequestBuffer[0];
	// TODO Set CommRequest.CommControlRequest.RequestID
	CommRequest.CommControlRequest.RequestBufferLength = sizeof(CommRequest.CommRequestBuffer);

	BOOL    status;
	DWORD   bytesReturned;
	DWORD	dwLastError;

	status = DeviceIoControl(g_QdDeviceHandle, QD_IOCTL_GET_NEW_PROCESSES,
		&(CommRequest), sizeof(COMM_REQUEST),
		&(CommRequest), sizeof(COMM_REQUEST),
		&bytesReturned,
		&CommRequestOverlapped);
	dwLastError = GetLastError();

	//
	// Ensure the request is either completed or pending
	//
	if (status) {
		// Completed
		LOG_INFO(_T("DeviceIoControl returned success immediately"));
	}
	else if (dwLastError == ERROR_IO_PENDING) {
		// Pending
		LOG_INFO(_T("DeviceIoControl Pending"));
	}
	else {
		// An error occurred
		LOG_ERROR(_T("DeviceIoControl failed - Status %x"), GetLastError());

		// TODO handle failure
		ReturnValue = FALSE;
		goto Exit;
	}

	// GetOverlappedResult waits infinitely.
	// Alternatively, could use HasOverlappedIoCompleted and Sleep in a loop
	// so I could terminate more easily
	// TODO EVENTUALLY Use GetOverlappedResultEx for Win 8 which allows timeouts

	DWORD   bytesTransferred;
	status = GetOverlappedResult(
		g_QdDeviceHandle,
		&CommRequestOverlapped,
		&bytesTransferred,
		TRUE); // bWait
	if (!status) {
		LOG_ERROR(_T("GetOverlappedResult failed - Status %x"), GetLastError());
		// Handle failure
		ReturnValue = FALSE;
		goto Exit;
	}

	if (!bRunning) {
		LOG_INFO(_T("We've been signalled to stop running"));
		ReturnValue = TRUE;
		goto Exit;
	}

	LOG_INFO(_T("GetOverlappedResult completed: Id= %08x, Type = %08x, CommRequest RequestBuffer %08x, CommRequestBuffer %08x, Length %x"),
		CommRequest.CommControlRequest.RequestID,
		CommRequest.CommControlRequest.RequestType,
		CommRequest.CommControlRequest.RequestBuffer,
		CommRequest.CommRequestBuffer,
		CommRequest.CommControlRequest.RequestBufferLength);

	//
	// Call the callback
	//
	PCOMM_CREATE_PROC pCreateProcStruct = (PCOMM_CREATE_PROC)CommRequest.CommRequestBuffer;
	processMonitorCallback(pCreateProcStruct);
	

	ResetEvent(CommRequestOverlapped.hEvent);
Exit:
	// Close our handle to the device.
	if (QdCloseDevice() != TRUE)
	{
		LOG_ERROR(_T("TcCloseDevice failed"));
	}

	return ReturnValue;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Retrieve info about new processes being created from the driver
///
///////////////////////////////////////////////////////////////////////////////
__declspec(dllexport)
BOOL
QdControl(USHORT decision, USHORT integrityCheck)
{
	BOOL ReturnValue = FALSE;
	OVERLAPPED CommRequestOverlapped = { 0 };
	COMM_REQUEST CommRequest = { 0 };

	LOG_INFO(_T("Sending control decision of %d"), decision);

	// Open a handle to the device.
	ReturnValue = QdOpenDevice();
	if (ReturnValue != TRUE)
	{
		LOG_ERROR(_T("QdOpenDevice failed"));
		goto Exit;
	}

	//
	// Init overlapped structured
	//
	CommRequestOverlapped.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
	if (CommRequestOverlapped.hEvent == INVALID_HANDLE_VALUE) {
		LOG_ERROR(_T("Unable to create overlapped event"));
		// TODO handle failure
	}

	CommRequest.CommControlRequest.RequestBuffer = &CommRequest.CommRequestBuffer[0];
	// TODO Set CommRequest.CommControlRequest.RequestID
	CommRequest.CommControlRequest.RequestBufferLength = sizeof(CommRequest.CommRequestBuffer);

	PCOMM_CONTROL_PROC pCommControlProc = (PCOMM_CONTROL_PROC)CommRequest.CommRequestBuffer;
	pCommControlProc->Decision = decision;
	pCommControlProc->IntegrityCheck = integrityCheck;

	BOOL    status;
	DWORD   bytesReturned;
	DWORD	dwLastError;

	status = DeviceIoControl(g_QdDeviceHandle, QD_IOCTL_CONTROLLER_PROCESS_DECISION,
		&(CommRequest), sizeof(COMM_REQUEST),
		&(CommRequest), sizeof(COMM_REQUEST),
		&bytesReturned,
		&CommRequestOverlapped);
	dwLastError = GetLastError();

	//
	// Ensure the request is either completed
	//
	if (status) {
		// Completed
		LOG_INFO(_T("DeviceIoControl returned success immediately"));
	}
	else {
		// An error occurred
		LOG_ERROR(_T("DeviceIoControl failed - Status %x"), GetLastError());

		// TODO handle failure
		CloseHandle(CommRequestOverlapped.hEvent);
		ReturnValue = FALSE;
		goto Exit;
	}

	ResetEvent(CommRequestOverlapped.hEvent);
	CloseHandle(CommRequestOverlapped.hEvent);

Exit:
	// Close our handle to the device.
	if (QdCloseDevice() != TRUE)
	{
		LOG_ERROR(_T("TcCloseDevice failed"));
	}

	LOG_TRACE(_T("Exiting"));
	return ReturnValue;
}





///////////////////////////////////////////////////////////////////////////////
///
///  Installs the kernel driver
///
///////////////////////////////////////////////////////////////////////////////
__declspec(dllexport)
BOOL
QdInstallDriver()
{
	BOOL bRC = TRUE;

	LOG_TRACE(L"Entering");
	BOOL Result = QdLoadDriver();
	if (Result != TRUE)
	{
		LOG_ERROR(L"QdLoadDriver failed, exiting");
		bRC = FALSE;
		goto Exit;
	}

Exit:
	LOG_TRACE(L"Exiting");
	return bRC;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Uninstalls the kernel driver
///
///////////////////////////////////////////////////////////////////////////////
__declspec(dllexport)
BOOL QdUninstallDriver()
{
	BOOL bRC = TRUE;

	LOG_TRACE(L"Entering");
	BOOL Result = QdUnloadDriver();

	if (Result != TRUE)
	{
		LOG_ERROR(L"QdUnloadDriver failed, exiting");
		bRC = FALSE;
		goto Exit;
	}

Exit:
	LOG_TRACE(L"Exiting");
	return bRC;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Initialize globals
///
///////////////////////////////////////////////////////////////////////////////
__declspec(dllexport)
BOOL QdInitialize()
{
	CommRequestEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
	if (CommRequestEvent == INVALID_HANDLE_VALUE) {
		LOG_ERROR(_T("Unable to create overlapped event"));
		return FALSE;
	}

	BOOL Result = QdInitializeGlobals();
	if (Result != TRUE)
	{
		LOG_ERROR(L"QdInitializeGlobals failed, exiting");
		return FALSE;
	}
	return TRUE;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Cleanup
///
///////////////////////////////////////////////////////////////////////////////
__declspec(dllexport)
BOOL QdUnInitialize()
{
	if (bRunning) {
		bRunning = false;
		// Set the monitor's event so it wakes up so we can tell it to stop running
		SetEvent(CommRequestEvent);

		if (QdCleanupSCM() == FALSE)
		{
			LOG_ERROR(L"QdUnInitialize failed cleanup of SCM");
		}
	}
	else {
		LOG_INFO(L"Already uninitialized once");
		// Clean up event
		if (CommRequestEvent != NULL) {
			CloseHandle(CommRequestEvent);
			CommRequestEvent = NULL;
		}
	}
	return TRUE;
}
