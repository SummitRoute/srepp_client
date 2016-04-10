////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "..\common\log.h"
#include "..\common\drivercomm.h"
#include "..\common\srkcomm.h"

///////////////////////////////////////////////////////////////////////////////
///
/// PrintUsage
///
///////////////////////////////////////////////////////////////////////////////
void TcPrintUsage()
{
	puts("Usage:");
	puts("");
	puts("control.exe -install -uninstall -monitor [-?]");
	puts("     -install        install driver");
	puts("     -uninstall      uninstall driver");
	puts("     -monitor        reecieves info about processes being loaded");
}

DWORD TcProcessMonitorCallback(PCOMM_CREATE_PROC pCreateProcStruct) {
	_tprintf(_T("New Process\n"));
	_tprintf(_T("  Image: %ls\n"), pCreateProcStruct->ImageFileNameBuf);
	_tprintf(_T("  Cmd: %ls\n"), pCreateProcStruct->CommandLineBuf);
	_tprintf(_T("  ppid: %lu\n"), pCreateProcStruct->ppid);

	// Decide
	
    /*
    // Here's an example of denying calc
	USHORT decision = CONTROLLER_RESPONSE_ALLOW;
	if (wcsstr(pCreateProcStruct->ImageFileNameBuf, L"calc") != 0) {
		puts("Deny calc from running");
		decision = CONTROLLER_RESPONSE_DENY;
	}
    */
    
	QdControl(decision, pCreateProcStruct->IntegrityCheck);
	
	return 0;
}


///////////////////////////////////////////////////////////////////////////////
///
/// wmain()
///
///////////////////////////////////////////////////////////////////////////////
int _cdecl
wmain(
_In_ int argc,
_In_reads_(argc) LPCWSTR argv[]
)
{
	int ExitCode = ERROR_SUCCESS;

	// Ensure we have a command
	if (argc <= 1)
	{
		TcPrintUsage();
		goto Exit;
	}

	const wchar_t * arg = argv[1];

	// Initialize globals and logging
	if (!QdInitialize()) 
	{
		puts("Initialization failed - program exiting");
		ExitCode = ERROR_FUNCTION_FAILED;
		goto Exit;
	}

	if (0 == wcscmp(arg, L"-install")) 
	{
		QdInstallDriver();
	}
	else if (0 == wcscmp(arg, L"-uninstall")) 
	{
		QdUninstallDriver();
	}
	else if (0 == wcscmp(arg, L"-monitor")) 
	{
		// Loop as long as the device exists
		while (QdMonitor(TcProcessMonitorCallback)) {
			continue; 
		}
	}
	else
	{
		puts("Unknown command!");
		TcPrintUsage();
	}


Exit:

	if (!QdUnInitialize()) 
	{
		puts("UnInitialization failed");
		ExitCode = ERROR_FUNCTION_FAILED;
	}

	return ExitCode;
}

