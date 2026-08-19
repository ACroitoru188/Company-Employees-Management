namespace CompanyEmployees.Domain.Enums;

public enum DelegatedActionType
{
    LeaveApproved = 1,
    LeaveRejected = 2,
    ContractExtended = 3,
    ContractTerminated = 4,

    // Borrowing an ordinary employee's account grants no approval rights, so requesting that
    // person's own leave is the only mark a delegate can leave there. Audited for the same
    // reason the manager ones are: the request table credits the borrowed account.
    LeaveRequested = 5
}
