////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

#pragma once

#pragma warning(disable:4214) // bit field types other than int
#pragma warning(disable:4201) // nameless struct/union

//
// TD_ASSERT
//
// This macro is identical to NT_ASSERT but works in fre builds as well.
//
// It is used for error checking in the driver in cases where
// we can't easily report the error to the user mode app, or the
// error is so severe that we should break in immediately to
// investigate.
//
// It's better than DbgBreakPoint because it provides additional info
// that can be dumped with .exr -1, and individual asserts can be disabled
// from kd using 'ahi' command.
//

//
// Driver and device names
//

#define QD_DRIVER_NAME             L"srepp"
#define QD_DRIVER_NAME_WITH_EXT    L"srepp.sys"

#define QD_NT_DEVICE_NAME          L"\\Device\\SummitRouteEPP"
#define QD_DOS_DEVICES_LINK_NAME   L"\\DosDevices\\SummitRouteEPP"
#define QD_WIN32_DEVICE_NAME       L"\\\\.\\SummitRouteEPP"

//
// Driver and User communication
//
typedef struct _COMM_CONTROL_REQUEST {
	// The request ID is used to match up the response to the original request
	ULONG RequestID;

	// The request type indicates the operation to be performed
	ULONG RequestType;

	// This specifies the size of the request buffer
	ULONG RequestBufferLength;

	// The data buffer allows the application to receive arbitrary data
	// Note that this is done OUT OF BOUNDS from the IOCTL.  Thus, the driver
	// is responsible for managing this.
	PVOID RequestBuffer;
} COMM_CONTROL_REQUEST, *PCOMM_CONTROL_REQUEST;


typedef struct _Comm_Request {
	COMM_CONTROL_REQUEST    CommControlRequest;
	CHAR                    CommRequestBuffer[5120];
} COMM_REQUEST, *PCOMM_REQUEST;


typedef struct _COMM_CREATE_PROC {
	SIZE_T     Size;  // 4 + 4 + 4 + 4 + 4 + 2 + 4 + 1024(2) + 2 + 4 + 1024(2) = 4128
	union {
		ULONG  Flags;
		struct {
			ULONG ImageFileNameIsAccurate: 1;  // If FileOpenNameAvailable is TRUE or not
			ULONG Reserved : 31;
		};
	};
	ULONG		pid; // Process ID

	ULONG       ppid; // Parent Process ID
	ULONG       ptid; // Parent Thread ID (the creating thread)

	USHORT		ImageFileNameLength;
	ULONG		ImageFileNameFullLength;
	WCHAR		ImageFileNameBuf[1024];
	
	USHORT		CommandLineLength;
	ULONG		CommandLineFullLength;
	WCHAR		CommandLineBuf[1024];

	USHORT		ProcIndex;  // When the userland controller wants to respond, it needs this so it can tell the driver what process it is deciding on
	USHORT		IntegrityCheck;  // Tell the userland controller a value that we'll check when it responds to ensure we got our request from the correct place
} COMM_CREATE_PROC, *PCOMM_CREATE_PROC;



// Used for communicating with the userland controller so it can decide on a process
typedef struct _COMM_CONTROL_PROC {
	USHORT		ProcIndex;		// When the user controller responds, we need to know where to
	USHORT		Decision;		// Allow (1) or Deny (2)
	USHORT		IntegrityCheck;
} COMM_CONTROL_PROC, *PCOMM_CONTROL_PROC;


// DeviceType is an arbitrary value between 32768 and 65535 
#define QD_CTL_CODE_DEVICE_TYPE 33333
// Function must be between 2048 and 4095
#define QD_CTL_CODE_FUNCTION 3333

//
// Service IOCTLs to driver
//
#define QD_IOCTL_GET_NEW_PROCESSES				(DWORD)CTL_CODE(QD_CTL_CODE_DEVICE_TYPE, QD_CTL_CODE_FUNCTION, METHOD_BUFFERED, FILE_READ_ACCESS|FILE_WRITE_ACCESS)
#define QD_IOCTL_CONTROLLER_PROCESS_DECISION	(DWORD)CTL_CODE(QD_CTL_CODE_DEVICE_TYPE, QD_CTL_CODE_FUNCTION+1, METHOD_BUFFERED, FILE_READ_ACCESS|FILE_WRITE_ACCESS)

#define QD_COMM_READ_REQUEST 0x10
#define QD_COMM_WRITE_REQUEST 0x20


#define TD_ASSERT(_exp) \
    ((!(_exp)) ? \
        (__annotation(L"Debug", L"AssertFail", L#_exp), \
         DbgRaiseAssertionFailure(), FALSE) : \
        TRUE)

//
// Responses for controller
// 
#define CONTROLLER_RESPONSE_NO_RESPONSE 0
#define CONTROLLER_RESPONSE_ALLOW 1
#define CONTROLLER_RESPONSE_DENY 2