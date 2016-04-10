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
#include "manageService.h"


void qdlog(DWORD level, TCHAR * format, ...)
{
	const SIZE_T numChars = 256;
	TCHAR buffer[numChars];
	va_list args;
	va_start(args, format);
	_vstprintf(buffer, numChars, format, args);
	OutputDebugString(buffer);
	va_end(args);
}

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
					 )
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		g_hModule = hModule;
		OutputDebugString(_T("qdsvc: Loaded"));  // TODO make log msg
		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

