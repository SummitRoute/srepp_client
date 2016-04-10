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
// Logging support macros.
//
// LOG_INFO
// LOG_INFO_FAILURE
// LOG_PASSED
// LOG_ERROR
//


#define LOG_LEVEL_TRACE		4
#define LOG_LEVEL_INFO		3
#define LOG_LEVEL_WARN		2
#define LOG_LEVEL_ERROR		1
#define LOG_LEVEL_CRITICAL	0

void qdlog(DWORD level, TCHAR * format, ...);

#ifdef _DEBUG
#define LOG_TRACE(fmt, ...)         \
    qdlog(LOG_LEVEL_TRACE, _T("%hs: ") fmt _T("\n"), __FUNCTION__, __VA_ARGS__);
#else
#define LOG_TRACE(FormatString, ...)
#endif

#define LOG_INFO(fmt, ...)         \
    qdlog(LOG_LEVEL_INFO, _T("%hs: ") fmt _T("\n"), __FUNCTION__, __VA_ARGS__);

#define LOG_WARN(fmt, ...)         \
    qdlog(LOG_LEVEL_WARN, _T("%hs: ") fmt _T("\n"), __FUNCTION__, __VA_ARGS__);

#define LOG_ERROR(fmt, ...)         \
    qdlog(LOG_LEVEL_ERROR, _T("%hs: ") fmt _T("\n"), __FUNCTION__, __VA_ARGS__);

#define LOG_CRITICAL(fmt, ...)         \
    qdlog(LOG_LEVEL_CRITICAL, _T("%hs: ") fmt _T("\n"), __FUNCTION__, __VA_ARGS__);


#define TD_ASSERT(_exp) \
    ((!(_exp)) ? \
        (__annotation(L"Debug", L"AssertFail", L#_exp), \
         DbgRaiseAssertionFailure(), FALSE) : \
        TRUE)



