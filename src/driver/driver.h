////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

#pragma once

#include "..\common\drivercomm.h"

//
// Internal use
//
#define QD_MAX_PROCS 16

// KeQuerySystemTime returns number of 100 nanoseconds, so the timeout is 3 seconds
#define QD_TIMEOUT (10000000 * 3)

// 1. Driver tells userland controller about the process being started.
// 2. Controller tells the driver it's decision via decision ioctl. 
// 3. Decision ioctl signals to process callback that a decision was made.
typedef struct _CONTROL_PROC_INTERNAL {
	LARGE_INTEGER StartTime;	// Keep track of when this occurred so we can clear it out if anything goes wrong
	KEVENT		DecisionEvent;	// For signalling within the driver
	USHORT		Decision;		// Allow (1) or Deny (2)
	USHORT		IntegrityCheck;	// Simple check to ensure something evil in userland isn't happening
} CONTROL_PROC_INTERNAL, *PCONTROL_PROC_INTERNAL;


//
// Device extension
//
typedef struct _QD_COMM_CONTROL_DEVICE_EXTENSION {
	// Data structure magic #
	ULONG MagicNumber;

	// Queue of new processes to be sent to userland
	LIST_ENTRY ProcessQueue;

	// Control Thread Service Queue Lock
	FAST_MUTEX ProcessQueueLock;

	// Control Request Queue - Userland requests for new info
	LIST_ENTRY RequestQueue;

	// Control Request Queue Lock
	FAST_MUTEX RequestQueueLock;

	// Control Decision Queue Lock
	FAST_MUTEX DecisionDataLock;

	// Pointer to array of stucts to store decision data when controller makes decisions for communicating around driver
	PCONTROL_PROC_INTERNAL DecisionData;

} QD_COMM_CONTROL_DEVICE_EXTENSION, *PQD_COMM_CONTROL_DEVICE_EXTENSION;

#define QD_COMM_CONTROL_EXTENSION_MAGIC_NUMBER 0x1d88f403

//
// Globals
//
extern PDEVICE_OBJECT g_CommDeviceObject;

//
// Function declarations
//
NTSTATUS 
ProcessIoctl_GetNewProcesses(
	_Inout_ PIRP Irp
	);

NTSTATUS
ProcessIoctl_ControllerProcessDecision(
	_Inout_ PIRP Irp
	);


VOID
MyCreateProcessNotifyRoutine(
_Inout_ PEPROCESS Process,
_In_ HANDLE ProcessId,
_In_opt_ PPS_CREATE_NOTIFY_INFO CreateInfo
);


