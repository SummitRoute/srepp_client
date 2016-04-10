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



///////////////////////////////////////////////////////////////////////////////
///
/// This is called everytime a process is created or terminated
///
///////////////////////////////////////////////////////////////////////////////
VOID
MyCreateProcessNotifyRoutine(
_Inout_ PEPROCESS Process,
_In_ HANDLE ProcessId,
_In_opt_ PPS_CREATE_NOTIFY_INFO CreateInfo
)
{
	PAGED_CODE();

	PQD_COMM_CONTROL_DEVICE_EXTENSION controlExt = 
		(PQD_COMM_CONTROL_DEVICE_EXTENSION)g_CommDeviceObject->DeviceExtension;
	PLIST_ENTRY pListEntry = NULL;
	USHORT ProcIndex = 0xffff;

	// Check if this is a new process starting
	if (CreateInfo != NULL)
	{
		// Print info about the process that started
		DbgPrintEx(
			DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL,
			"QuietDragon: MyCreateProcessNotifyRoutine: process %p (ID %lu) created, creator %lu:%lu\n"
			"    command line: %wZ\n"
			"    file name %wZ (FileOpenNameAvailable: %d)\n",
			Process,
			(ULONG)ProcessId,
			(ULONG)CreateInfo->CreatingThreadId.UniqueProcess,
			(ULONG)CreateInfo->CreatingThreadId.UniqueThread,
			CreateInfo->CommandLine,
			CreateInfo->ImageFileName,
			CreateInfo->FileOpenNameAvailable
			);

		//
		// Acquire locks
		//
		ExAcquireFastMutex(&controlExt->ProcessQueueLock);
		ExAcquireFastMutex(&controlExt->RequestQueueLock);
		{
			if (!IsListEmpty(&controlExt->RequestQueue)) {
				// Userland is waiting on info, so remove the first Irp from the requestQueue.
				DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Get request queue item\n");
				pListEntry = RemoveHeadList(&controlExt->RequestQueue);
			}
			else {
				DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Request queue is empty so ignore this for now\n");
				// TODO I should queue these
			}
		}
		// Release locks
		ExReleaseFastMutex(&controlExt->RequestQueueLock);
		ExReleaseFastMutex(&controlExt->ProcessQueueLock);


		if (pListEntry) {
			// We have a request so send this to it
			DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Getting Irp to talk to\n");

			PIRP Irp = CONTAINING_RECORD(pListEntry, IRP, Tail.Overlay.ListEntry);
			NTSTATUS status = STATUS_UNSUCCESSFUL;

			PCOMM_CREATE_PROC pCreateProcStruct;
			USHORT bufLength;

			PCOMM_REQUEST pCommRequest = (PCOMM_REQUEST)Irp->AssociatedIrp.SystemBuffer;
			if (NULL == pCommRequest) {
				DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Failed to get the userland buffer\n");
				status = STATUS_INSUFFICIENT_RESOURCES;
				// TODO ensure I'm failing correctly
				Irp->IoStatus.Information = 0;
				Irp->IoStatus.Status = status;
				IoCompleteRequest(Irp, IO_NO_INCREMENT);
			}

			pCreateProcStruct = (PCOMM_CREATE_PROC)pCommRequest->CommRequestBuffer;
			DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Filling in create process struct\n");

			//
			// Set struct members
			//
			pCreateProcStruct->ImageFileNameIsAccurate = CreateInfo->FileOpenNameAvailable;
			pCreateProcStruct->pid = (ULONG)ProcessId;
			pCreateProcStruct->ppid = (ULONG)CreateInfo->CreatingThreadId.UniqueProcess;
			pCreateProcStruct->ptid = (ULONG)CreateInfo->CreatingThreadId.UniqueThread;

			// Set ImageFileName
			pCreateProcStruct->ImageFileNameLength = CreateInfo->ImageFileName->Length;
			bufLength = sizeof(pCreateProcStruct->ImageFileNameBuf);
			if (bufLength > CreateInfo->ImageFileName->Length) {
				bufLength = CreateInfo->ImageFileName->Length;
			}
			RtlCopyMemory(pCreateProcStruct->ImageFileNameBuf, CreateInfo->ImageFileName->Buffer, bufLength);
			pCreateProcStruct->ImageFileNameLength = bufLength;

			// Set CommandLine
			pCreateProcStruct->CommandLineFullLength = CreateInfo->CommandLine->Length;
			bufLength = sizeof(pCreateProcStruct->CommandLineBuf);
			if (bufLength > CreateInfo->CommandLine->Length) {
				bufLength = CreateInfo->CommandLine->Length;
			}
			RtlCopyMemory(pCreateProcStruct->CommandLineBuf, CreateInfo->CommandLine->Buffer, bufLength);
			pCreateProcStruct->CommandLineLength = bufLength;
			
			// Set struct length
			pCommRequest->CommControlRequest.RequestBufferLength = sizeof(COMM_CREATE_PROC);

			//
			// Store info about this process in our array so when the controller decides on it, we can pick up that info
			//
			ExAcquireFastMutex(&controlExt->DecisionDataLock);
			{
				// First find a place to store it
				USHORT i = 0;
				LARGE_INTEGER now, stale;
				KeQuerySystemTime(&now);
				stale = now;
				stale.QuadPart -= QD_TIMEOUT;

				for (; i < QD_MAX_PROCS; i++) {
					PCONTROL_PROC_INTERNAL control_proc = &(controlExt->DecisionData[i]);
					if (control_proc->StartTime.QuadPart == 0 || control_proc->StartTime.QuadPart < stale.QuadPart) {
						// Found a slot to store our info
						ProcIndex = i;
						pCreateProcStruct->ProcIndex = i;
						
						// Set this struct to in use (as identified by a recent start time)
						control_proc->StartTime = now;

						// Set integrity check
						control_proc->IntegrityCheck = (now.QuadPart & 0xffff) ^ 0x3554;  // Simple munging to sort of obfuscate this
						pCreateProcStruct->IntegrityCheck = control_proc->IntegrityCheck;
						control_proc->Decision = CONTROLLER_RESPONSE_NO_RESPONSE;

						break;
					}
				}
				// Sanity check
				if (i >= QD_MAX_PROCS) {
					DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: No spots available so we can't retrieve the controllers info\n");
					// Use a bad ProcIndex to indicate we failed to find a slot
					pCreateProcStruct->ProcIndex = 0xffff;
				}
			}
			// Release lock
			ExReleaseFastMutex(&controlExt->DecisionDataLock);



			//
			// We've finished processing the request to this point.  Dispatch to the control application
			// for further processing.
			//
			Irp->IoStatus.Information = sizeof(COMM_REQUEST);
			status = STATUS_SUCCESS;
			Irp->IoStatus.Status = status;
			IoCompleteRequest(Irp, IO_NO_INCREMENT);

			// Sanity check
			if (ProcIndex < QD_MAX_PROCS) {
				//
				// Wait for response from the user-land controller if we should allow or deny this process
				//
				USHORT controllerResponse = CONTROLLER_RESPONSE_NO_RESPONSE;
				LARGE_INTEGER end, now;
				KeQuerySystemTime(&now);
				end = now;
				end.QuadPart += QD_TIMEOUT;
				BOOLEAN decisionFound = FALSE;
				while (now.QuadPart < end.QuadPart) {
					// Wait on an event that get's signaled when the controller sends an IOCTL with it's decision
					// This will time-out after 3 seconds.
					// I'm looping because potentially multiple processes could be attempted to start at the same time
					// so need to wait until our process of interest hits

					PCONTROL_PROC_INTERNAL pControlProcInternal = &(controlExt->DecisionData[ProcIndex]);
					NTSTATUS result = KeWaitForSingleObject(&(pControlProcInternal->DecisionEvent), Executive, UserMode, TRUE, &end);
					if (result == STATUS_SUCCESS) {
						DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Event result: STATUS_SUCCESS\n");

						// Check if this is our process of interest
						ExAcquireFastMutex(&controlExt->DecisionDataLock);
						{
							DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: Controller response found\n");
							
							// Get decision
							controllerResponse = pControlProcInternal->Decision;

							// Set things back
							pControlProcInternal->StartTime.QuadPart = 0;
							KeClearEvent(&(pControlProcInternal->DecisionEvent));
							decisionFound = TRUE;
						}
						// Release lock
						ExReleaseFastMutex(&controlExt->DecisionDataLock);
					}
					else if (result == STATUS_ALERTED) {
						DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: Event result: STATUS_ALERTED\n");
					}
					else if (result == STATUS_TIMEOUT) {
						DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: Event result: STATUS_TIMEOUT\n");
					}

					// Break out of the loop further if the decision has been made
					if (decisionFound) {
						break;
					}

					DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: Still waiting on response from the controller\n");
					KeQuerySystemTime(&now);
				}

				//
				// Act on decision from controller (allow or deny process)
				//
				if (controllerResponse == CONTROLLER_RESPONSE_NO_RESPONSE) {
					DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_WARNING_LEVEL, "QuietDragon: Controller never responded... so allowing (fail open)\n");
					// TODO log that the controller never responded
				}
				else if (controllerResponse == CONTROLLER_RESPONSE_DENY) {
					DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: Deny the process\n");
					CreateInfo->CreationStatus = STATUS_ACCESS_DENIED;
				}
			} // Assume we can find our proc index
		}
	}
	else {
		DbgPrintEx(
			DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: MyCreateProcessNotifyRoutine: process %p (ID 0x%p) destroyed\n",
			Process,
			(PVOID)ProcessId
			);
	}
}


///////////////////////////////////////////////////////////////////////////////
///
/// Satisfy the control request or enqueue it
///
/// Parameters
///   Irp - IRP that we are processing
///
/// Returns
///   STATUS_SUCCESS: There is data going back to the application
///   STATUS_PENDING: The IRP will block and wailt until data is available
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS ProcessIoctl_GetNewProcesses(PIRP Irp)
{
	NTSTATUS status = STATUS_UNSUCCESSFUL;

	PQD_COMM_CONTROL_DEVICE_EXTENSION controlExt =
		(PQD_COMM_CONTROL_DEVICE_EXTENSION)g_CommDeviceObject->DeviceExtension;
	PLIST_ENTRY listEntry = NULL;
	PIO_STACK_LOCATION irpSp;
	PVOID dataBuffer;
	SIZE_T numBytesToCopy;

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Started\n");

	PAGED_CODE();

	//
	// Acquire locks to the control queue before we do anything
	//
	ExAcquireFastMutex(&controlExt->ProcessQueueLock);
	ExAcquireFastMutex(&controlExt->RequestQueueLock);
	{

		//
		// Check the process queue
		//
		if (!IsListEmpty(&controlExt->ProcessQueue)) {
			// Process queue is not empty, so just pull the first item off the list
			DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Returning process item from head of list\n");
			listEntry = RemoveHeadList(&controlExt->ProcessQueue);
			status = STATUS_SUCCESS;
		}
		else {
			DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Process queue is empty\n");
			//
			// Request queue is empty, so queue this
			//

			// First check if it is malformed
			PCOMM_REQUEST commRequest = (PCOMM_REQUEST)Irp->AssociatedIrp.SystemBuffer;
			irpSp = IoGetCurrentIrpStackLocation(Irp);
			if (!commRequest || irpSp->Parameters.DeviceIoControl.OutputBufferLength < sizeof(COMM_REQUEST)) {
				// Request is malformed
				DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Request is invalid\n");
				status = STATUS_INVALID_PARAMETER;
			}
			else {
				// Request is good, so queue it
				IoMarkIrpPending(Irp);
				DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Queing my irp to the request queue\n.");
				InsertTailList(&controlExt->RequestQueue, &Irp->Tail.Overlay.ListEntry);
				status = STATUS_PENDING;
			}
		}

		//
		// Release locks
		//
		ExReleaseFastMutex(&controlExt->RequestQueueLock);
		ExReleaseFastMutex(&controlExt->ProcessQueueLock);
	}


	//
	// If we found an entry to process, return the information to the caller
	//
	if (listEntry) {
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: We have a list entry so return it\n");

		// TODO Returning constant data for now, need to return real data
		irpSp = IoGetCurrentIrpStackLocation(Irp);
		PCHAR   dataToSend = "This String is from Device Driver !!!";
		SIZE_T	dataToSendLength = strlen(dataToSend) + 1;  // Length of data including null
		SIZE_T	outBufLength = irpSp->Parameters.DeviceIoControl.OutputBufferLength;

		PCOMM_REQUEST pCommRequest = (PCOMM_REQUEST)Irp->AssociatedIrp.SystemBuffer;
		if (NULL == pCommRequest) {
			DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Failed to get the userland buffer\n");
			status = STATUS_INSUFFICIENT_RESOURCES;
			Irp->IoStatus.Information = 0;
			return status;
		}

		dataBuffer = pCommRequest->CommRequestBuffer;

		// Calculate the number of bytes to send
		numBytesToCopy = dataToSendLength < outBufLength ? dataToSendLength : outBufLength;

		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_GetNewProcesses: Copying data\n");
		RtlCopyMemory(dataBuffer, dataToSend, numBytesToCopy);
		pCommRequest->CommControlRequest.RequestBufferLength = (ULONG)numBytesToCopy;


		// We've finished processing the request to this point.  Dispatch to the control application
		// for further processing.
		Irp->IoStatus.Information = sizeof(COMM_CONTROL_REQUEST);
		status = STATUS_SUCCESS;
	}

	return status;
}


///////////////////////////////////////////////////////////////////////////////
///
/// Receive decision from controller when it's informed of a new process
///
/// Parameters
///   Irp - IRP that we are processing
///
/// Returns
///   STATUS_SUCCESS: Inform controller we received the message
///
///////////////////////////////////////////////////////////////////////////////
NTSTATUS ProcessIoctl_ControllerProcessDecision(PIRP Irp)
{
	PAGED_CODE();

	PQD_COMM_CONTROL_DEVICE_EXTENSION controlExt =
		(PQD_COMM_CONTROL_DEVICE_EXTENSION)g_CommDeviceObject->DeviceExtension;

	NTSTATUS status = STATUS_UNSUCCESSFUL;

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Started\n");

	PCOMM_REQUEST pCommRequest = (PCOMM_REQUEST)Irp->AssociatedIrp.SystemBuffer;
	if (NULL == pCommRequest) {
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Failed to get the userland buffer\n");
		status = STATUS_INSUFFICIENT_RESOURCES;
		// TODO ensure I'm failing correctly
		Irp->IoStatus.Information = 0;
		Irp->IoStatus.Status = status;
		return status;
	}

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Read info from controller\n");
	PCOMM_CONTROL_PROC pCommControlProc = (PCOMM_CONTROL_PROC)pCommRequest->CommRequestBuffer;
	
	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Decision: %lu\n", 
		pCommControlProc->Decision);

	// Sanity check
	if (pCommControlProc->ProcIndex > QD_MAX_PROCS) {
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Controller proc index was out of bounds\n");
		status = STATUS_INSUFFICIENT_RESOURCES;
		// TODO ensure I'm failing correctly
		Irp->IoStatus.Information = 0;
		Irp->IoStatus.Status = status;
		return status;
	}

	PCONTROL_PROC_INTERNAL controlProcInternal = (PCONTROL_PROC_INTERNAL)&controlExt->DecisionData[pCommControlProc->ProcIndex];

	//
	// Record decision so the process callback can pick it up (it should be waiting on this info right now)
	//
	ExAcquireFastMutex(&controlExt->DecisionDataLock);
	{
		if (pCommControlProc->IntegrityCheck != controlProcInternal->IntegrityCheck) {
			// This is not good.  The integrity check failed.  Something malicious possibly?
			DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_ERROR_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Integrity check mismatch\n");
			status = STATUS_UNSUCCESSFUL;
			// TODO ensure I'm failing correctly
			Irp->IoStatus.Information = 0;
			Irp->IoStatus.Status = status;
			// Release lock
			ExReleaseFastMutex(&controlExt->DecisionDataLock);
			return status;
		}
		DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Setting decision\n");
		controlProcInternal->Decision = pCommControlProc->Decision;
	}
	// Release lock
	ExReleaseFastMutex(&controlExt->DecisionDataLock);

	DbgPrintEx(DPFLTR_IHVDRIVER_ID, DPFLTR_TRACE_LEVEL, "QuietDragon: ProcessIoctl_ControllerProcessDecision: Signalling via event for proc with integrity check %lu\n", controlProcInternal->IntegrityCheck);

	// Signal the process callback method so it wakes up and acts on this info
	KeSetEvent(&controlProcInternal->DecisionEvent, 1, FALSE);

	// Tell the controller we're done now
	Irp->IoStatus.Information = 0;  // No info to return
	status = STATUS_SUCCESS;
	Irp->IoStatus.Status = status;

	return status;
}
