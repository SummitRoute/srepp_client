////////////////////////////////////////////////////////////////////////////
//
// Summit Route End Point Protection
//
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree.
//
/////////////////////////////////////////////////////////////////////////////

#include "pch.h"
#include "driver.h"

//
// Process notify routines.
//
BOOLEAN gProcessNotifyRoutine_isSet = FALSE;

// Reference to device object struct that maintains globals
PDEVICE_OBJECT g_CommDeviceObject;


//
// Function declarations
//
DRIVER_INITIALIZE  DriverEntry;

_Dispatch_type_(IRP_MJ_CREATE) DRIVER_DISPATCH TdDeviceCreate;
_Dispatch_type_(IRP_MJ_CLOSE) DRIVER_DISPATCH TdDeviceClose;
_Dispatch_type_(IRP_MJ_CLEANUP) DRIVER_DISPATCH TdDeviceCleanup;
_Dispatch_type_(IRP_MJ_DEVICE_CONTROL) DRIVER_DISPATCH TdDeviceControl;

DRIVER_UNLOAD   TdDeviceUnload;


///////////////////////////////////////////////////////////////////////////////
///
/// Driver entry
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS
DriverEntry(
_In_ PDRIVER_OBJECT  DriverObject,
_In_ PUNICODE_STRING RegistryPath
)
{
	NTSTATUS Status;
	UNICODE_STRING NtDeviceName = RTL_CONSTANT_STRING(QD_NT_DEVICE_NAME);
	UNICODE_STRING DosDevicesLinkName = RTL_CONSTANT_STRING(QD_DOS_DEVICES_LINK_NAME);
	BOOLEAN SymLinkCreated = FALSE;
	PQD_COMM_CONTROL_DEVICE_EXTENSION controlExt;

	UNREFERENCED_PARAMETER(RegistryPath);

	// Request NX Non-Paged Pool when available
	ExInitializeDriverRuntime(DrvRtPoolNxOptIn);

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: DriverEntry: Driver loaded\n");

	//
	// Initialize globals.
	//

	//
	// Create our device object.
	//
	// TODO Consider using IoCreateDeviceSecure 
	Status = IoCreateDevice(
		DriverObject,                 // pointer to driver object
		sizeof(QD_COMM_CONTROL_DEVICE_EXTENSION),  // device extension size
		&NtDeviceName,                // device name
		FILE_DEVICE_UNKNOWN,          // device type
		0,                            // device characteristics
		FALSE,                        // not exclusive
		&g_CommDeviceObject);         // returned device object pointer
	if (!NT_SUCCESS(Status))
	{
		goto Exit;
	}

	TD_ASSERT(g_CommDeviceObject == DriverObject->DeviceObject);

	//
	// Set dispatch routines.
	//
	DriverObject->MajorFunction[IRP_MJ_CREATE] = TdDeviceCreate;
	DriverObject->MajorFunction[IRP_MJ_CLOSE] = TdDeviceClose;
	DriverObject->MajorFunction[IRP_MJ_CLEANUP] = TdDeviceCleanup;
	DriverObject->MajorFunction[IRP_MJ_DEVICE_CONTROL] = TdDeviceControl;
	DriverObject->DriverUnload = TdDeviceUnload;


	//
	// Set up the device extension
	//
	controlExt = (PQD_COMM_CONTROL_DEVICE_EXTENSION)g_CommDeviceObject->DeviceExtension;
	controlExt->MagicNumber = QD_COMM_CONTROL_EXTENSION_MAGIC_NUMBER;
	InitializeListHead(&controlExt->ProcessQueue);
	ExInitializeFastMutex(&controlExt->ProcessQueueLock);
	InitializeListHead(&controlExt->RequestQueue);
	ExInitializeFastMutex(&controlExt->RequestQueueLock);
	ExInitializeFastMutex(&controlExt->DecisionDataLock);

	controlExt->DecisionData = (PCONTROL_PROC_INTERNAL)ExAllocatePoolWithTag(NonPagedPool, sizeof(CONTROL_PROC_INTERNAL)*QD_MAX_PROCS, 'SRdd');
	RtlZeroMemory(controlExt->DecisionData, sizeof(CONTROL_PROC_INTERNAL)*QD_MAX_PROCS);

	// Init DecisionData
	for (USHORT i = 0; i < QD_MAX_PROCS; i++) {
		PCONTROL_PROC_INTERNAL control_proc = &(controlExt->DecisionData[i]);
		control_proc->StartTime.QuadPart = 0;
		KeInitializeEvent(&(control_proc->DecisionEvent), NotificationEvent, FALSE);
	}

	//
	// Create a link in the Win32 namespace.
	//
	Status = IoCreateSymbolicLink(&DosDevicesLinkName, &NtDeviceName);
	if (!NT_SUCCESS(Status))
	{
		goto Exit;
	}

	SymLinkCreated = TRUE;

	//
	// Set process create routines.
	//
	Status = PsSetCreateProcessNotifyRoutineEx(
		MyCreateProcessNotifyRoutine,
		FALSE
		);
	if (!NT_SUCCESS(Status))
	{
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: DriverEntry: PsSetCreateProcessNotifyRoutineEx returned 0x%x\n", Status);
		goto Exit;
	}

	gProcessNotifyRoutine_isSet = TRUE;


Exit:

	if (!NT_SUCCESS(Status))
	{
		if (gProcessNotifyRoutine_isSet == TRUE)
		{
			Status = PsSetCreateProcessNotifyRoutineEx(
				MyCreateProcessNotifyRoutine,
				TRUE
				);
			TD_ASSERT(Status == STATUS_SUCCESS);

			gProcessNotifyRoutine_isSet = FALSE;
		}

		if (SymLinkCreated == TRUE)
		{
			IoDeleteSymbolicLink(&DosDevicesLinkName);
		}

		if (g_CommDeviceObject != NULL)
		{
			IoDeleteDevice(g_CommDeviceObject);
		}
	}

	return Status;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Handle driver unloading. All this driver needs to do 
///  is to delete the device object and the symbolic link between our 
///  device name and the Win32 visible name.
///
///////////////////////////////////////////////////////////////////////////////
VOID
TdDeviceUnload(
_In_ PDRIVER_OBJECT DriverObject
)
{
	NTSTATUS Status = STATUS_SUCCESS;
	UNICODE_STRING DosDevicesLinkName = RTL_CONSTANT_STRING(QD_DOS_DEVICES_LINK_NAME);

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: TdDeviceUnload\n");

	// Unregister process notify routines.
	if (gProcessNotifyRoutine_isSet == TRUE)
	{
		Status = PsSetCreateProcessNotifyRoutineEx(
			MyCreateProcessNotifyRoutine,
			TRUE
			);

		TD_ASSERT(Status == STATUS_SUCCESS);

		gProcessNotifyRoutine_isSet = FALSE;
	}

	// TODO Need to clean up lists and locks

	// Free allocated mem
	PQD_COMM_CONTROL_DEVICE_EXTENSION controlExt = (PQD_COMM_CONTROL_DEVICE_EXTENSION)g_CommDeviceObject->DeviceExtension;
	ExFreePoolWithTag(controlExt->DecisionData, 'SRdd');

	// Delete the link from our device name to a name in the Win32 namespace.
	Status = IoDeleteSymbolicLink(&DosDevicesLinkName);
	if (Status != STATUS_INSUFFICIENT_RESOURCES) 
	{
		// IoDeleteSymbolicLink can fail with STATUS_INSUFFICIENT_RESOURCES.
		TD_ASSERT(NT_SUCCESS(Status));
	}

	// Delete our device object.
	IoDeleteDevice(DriverObject->DeviceObject);
}


///////////////////////////////////////////////////////////////////////////////
///
///  This function handles the 'create' irp.
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS
TdDeviceCreate(
IN PDEVICE_OBJECT  DeviceObject,
IN PIRP  Irp
)
{
	UNREFERENCED_PARAMETER(DeviceObject);

	Irp->IoStatus.Status = STATUS_SUCCESS;
	Irp->IoStatus.Information = 0;
	IoCompleteRequest(Irp, IO_NO_INCREMENT);

	return STATUS_SUCCESS;
}


///////////////////////////////////////////////////////////////////////////////
///
///  Handles the 'close' irp.
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS
TdDeviceClose(
IN PDEVICE_OBJECT  DeviceObject,
IN PIRP  Irp
)
{
	UNREFERENCED_PARAMETER(DeviceObject);

	Irp->IoStatus.Status = STATUS_SUCCESS;
	Irp->IoStatus.Information = 0;
	IoCompleteRequest(Irp, IO_NO_INCREMENT);

	return STATUS_SUCCESS;
}

///////////////////////////////////////////////////////////////////////////////
///
///  Handles the 'cleanup' irp.
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS
TdDeviceCleanup(
IN PDEVICE_OBJECT  DeviceObject,
IN PIRP  Irp
)
{
	UNREFERENCED_PARAMETER(DeviceObject);

	Irp->IoStatus.Status = STATUS_SUCCESS;
	Irp->IoStatus.Information = 0;
	IoCompleteRequest(Irp, IO_NO_INCREMENT);

	return STATUS_SUCCESS;
}


///////////////////////////////////////////////////////////////////////////////
///
/// This function handles 'control' irp.
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS
TdDeviceControl(
IN PDEVICE_OBJECT  DeviceObject,
IN PIRP  Irp
)
{
	PIO_STACK_LOCATION IrpStack;
	ULONG Ioctl;
	NTSTATUS Status;

	UNREFERENCED_PARAMETER(DeviceObject);


	Status = STATUS_SUCCESS;

	IrpStack = IoGetCurrentIrpStackLocation(Irp);
	Ioctl = IrpStack->Parameters.DeviceIoControl.IoControlCode;

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "TdDeviceControl: entering - ioctl code 0x%x\n", Ioctl);

	// Sanity check: Check the size of the request
	if (IrpStack->Parameters.DeviceIoControl.OutputBufferLength < sizeof(COMM_REQUEST)) {
		// Wrong size
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "TdDeviceControl: Wrong size request\n");
		Irp->IoStatus.Status = STATUS_BUFFER_OVERFLOW;
		Irp->IoStatus.Information = sizeof(COMM_REQUEST);
		IoCompleteRequest(Irp, IO_NO_INCREMENT);
		return STATUS_BUFFER_OVERFLOW;
	}

	switch (Ioctl)
	{
	case QD_IOCTL_GET_NEW_PROCESSES:
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "TdDeviceControl: recieved QD_IOCTL_GET_NEW_PROCESSES\n");

		// Process it
		Status = ProcessIoctl_GetNewProcesses(Irp);

		//
		// Complete the irp and return.
		//
		Irp->IoStatus.Status = Status;
		if (STATUS_PENDING != Status) {
			IoCompleteRequest(Irp, IO_NO_INCREMENT);
		}

		break;
	case QD_IOCTL_CONTROLLER_PROCESS_DECISION:
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "TdDeviceControl: recieved QD_IOCTL_CONTROLLER_PROCESS_DECISION\n");

		// Process it
		Status = ProcessIoctl_ControllerProcessDecision(Irp);

		//
		// Complete the irp and return.
		//
		Irp->IoStatus.Status = Status;
		IoCompleteRequest(Irp, IO_NO_INCREMENT);

		break;
	default:
		//
		// Invalid request operation
		//
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "TdDeviceControl: unrecognized ioctl code 0x%x\n", Ioctl);

		Status = STATUS_INVALID_DEVICE_REQUEST;
		Irp->IoStatus.Status = Status;
		Irp->IoStatus.Information = 0;
		IoCompleteRequest(Irp, IO_NO_INCREMENT);
	}

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "TdDeviceControl leaving - status 0x%x\n", Status);
	return Status;
}