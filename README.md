Project Abandoned
=================
This is a code dump of what I had hoped to turn into a business, but walked away from.  No support or maintenance will be provided.

Summit Route End Point Protection (SREPP) - Client code
===============================================

See the [Server code](https://github.com/SummitRoute/srepp_server) for a description of the project.  

"quietdragon" is the working name I was using for some of the code for a while as this was an ETDR (end-point threat detect and response) solution, and "quiETDRagon" contains those letters. So you'll see references to "qd" throughout the project.


How the driver works:
- Registers process notification callback: MyCreateProcessNotifyRoutine
- Userland controller calls ProcessIoctl_GetNewProcesses, which responds with pending, and stores the IRP
- Whenever a process is created, MyCreateProcessNotifyRoutine stores some info about it in a _CONTROL_PROC_INTERNAL struct in an array and tells the controller about it via the IRP, then it waits on an event that will be signalled.
- The controller decides if the process should be allowed or denied by calling ProcessIoctl_ControllerProcessDecision which then sets more info in that _CONTROL_PROC_INTERNAL struct and signals the event to tell MyCreateProcessNotifyRoutine that a decision has been made
- MyCreateProcessNotifyRoutine then looks up and acts on the decision



General fixes needed:
- Need to better deal with string sizes, currently most strings are set to 256 wchars, if over, they truncate.
- Better stop service and driver + uninstall + update
- Test many processes starting at once.  Test races.  Test multiple CPU.