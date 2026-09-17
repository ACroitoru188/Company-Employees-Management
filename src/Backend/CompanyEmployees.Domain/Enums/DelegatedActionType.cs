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
    LeaveRequested = 5,

    DepartmentChanged = 6,
    ContractUpdated = 7,
    RegionChanged = 8,

    // Undoing leave that was already approved: the employee asks, HR answers. Kept apart from
    // LeaveRejected, which decides a request nobody had approved yet — these three overturn a
    // decision that already stood and move days back into a balance.
    LeaveCancellationRequested = 9,
    LeaveCancellationApproved = 10,
    LeaveCancellationRejected = 11
}
