namespace CompanyEmployees.Domain.GatewayInterfaces;

using CompanyEmployees.Domain.Entities;

public interface ILeaveRequestGateway
{                                                  
    Task<List<LeaveRequest>>                       
        GetRequestsByUserAsync(Guid userId);               
                                                     
    Task<List<LeaveAllocation>>                    
        GetAllocationsByUserAsync(Guid userId, int year);  
                                                     
    Task<List<LeaveRequest>>                       
        GetApprovedRequestsForUsersAsync(                  
            List<Guid> userIds, DateOnly from, DateOnly
                to);                                               
                                                     
    Task CreateRequestAsync(LeaveRequest request); 
}  