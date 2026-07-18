using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace CompanyEmployees.Persistence
{
    public class DatabaseSeeder
    {
        private readonly CompanyEmployeesDbContext _db;
        private readonly UserManager<User> _userManager;

        // Days granted per leave type for every demo user (current year).
        private static readonly Dictionary<LeaveType, int> DefaultAllocations = new()
        {
            [LeaveType.Annual] = 21,
            [LeaveType.Sick] = 10,
            [LeaveType.Parental] = 10,
            [LeaveType.Unpaid] = 30
        };

        public DatabaseSeeder(CompanyEmployeesDbContext db, UserManager<User> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public void Seed()
        {
            _db.Database.EnsureDeleted();
            _db.Database.EnsureCreated();

            // Reporting chain (Admin -> LM -> PM -> employees) is expressed only through
            // ManagerId, so each level must be saved before the one below can reference it.
            var admin = SeedDemoUser("itadmin@siemens.com", "Demo Admin", UserRole.Admin);
            var lineManager = SeedDemoUser("linemanager@siemens.com", "Demo Line Manager", UserRole.LineManager, admin.Id);
            var projectManager = SeedDemoUser("projectmanager@siemens.com", "Demo Project Manager", UserRole.ProjectManager, lineManager.Id);
            var employee = SeedDemoUser("employee@siemens.com", "Demo Employee", UserRole.Employee, projectManager.Id);
            var colleague = SeedDemoUser("colleague@siemens.com", "Demo Colleague", UserRole.Employee, projectManager.Id);

            SeedAllocations([admin, lineManager, projectManager, employee, colleague]);
            SeedDemoRequests(employee, colleague, projectManager, lineManager);
        }

        private User SeedDemoUser(string email, string name, UserRole role, Guid? managerId = null)
        {
            var existing = _db.Users.FirstOrDefault(u => u.Email == email);
            if (existing != null)
            {
                return existing;
            }
                
            var user = new User
            {
                Name = name,
                UserName = email,
                Email = email,
                Role = role,
                Status = UserStatus.Active,
                ManagerId = managerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var result = _userManager.CreateAsync(user, "Passw0rd!").Result;

            return user;
        }

        private void SeedAllocations(List<User> users)
        {
            var year = DateTime.UtcNow.Year;

            foreach (var user in users)
            {
                foreach (var (type, days) in DefaultAllocations)
                {
                    _db.LeaveAllocations.Add(new LeaveAllocation
                    {
                        UserId = user.Id,
                        LeaveType = type,
                        Year = year,
                        NumberOfDays = days,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
            _db.SaveChanges();
        }

        private void SeedDemoRequests(User employee, User colleague, User projectManager, User lineManager)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);

            // Approved sick leave in the recent past, reviewed by the manager.
            var approvedRequest = new LeaveRequest
            {
                UserId = employee.Id,
                Type = LeaveType.Sick,
                StartDate = today.AddDays(-12),
                EndDate = today.AddDays(-11),
                Reason = "Flu",
                Status = LeaveStatus.Approved,
                CreatedAt = DateTime.UtcNow.AddDays(-13)
            };
            approvedRequest.Approvals.Add(new LeaveApproval
            {
                ApproverId = projectManager.Id,
                Step = 1,
                Status = LeaveStatus.Approved,
                ReviewedAt = DateTime.UtcNow.AddDays(-12),
                CreatedAt = DateTime.UtcNow.AddDays(-13)
            });
            _db.LeaveRequests.Add(approvedRequest);

            // Pending vacation waiting for review.
            _db.LeaveRequests.Add(new LeaveRequest
            {
                UserId = employee.Id,
                Type = LeaveType.Annual,
                StartDate = today.AddDays(14),
                EndDate = today.AddDays(18),
                Reason = "Mountain trip",
                Status = LeaveStatus.Pending,
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });

            // A teammate's approved leave so the team calendar has something to show.
            var colleagueRequest = new LeaveRequest
            {
                UserId = colleague.Id,
                Type = LeaveType.Annual,
                StartDate = today.AddDays(5),
                EndDate = today.AddDays(9),
                Reason = "City break",
                Status = LeaveStatus.Approved,
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            };
            colleagueRequest.Approvals.Add(new LeaveApproval
            {
                ApproverId = projectManager.Id,
                Step = 1,
                Status = LeaveStatus.Approved,
                ReviewedAt = DateTime.UtcNow.AddDays(-5),
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            });
            _db.LeaveRequests.Add(colleagueRequest);

            // The PM's approved leave shows up on the team calendar and Team page of the
            // employees they manage (the manager is part of the team).
            var managerRequest = new LeaveRequest
            {
                UserId = projectManager.Id,
                Type = LeaveType.Annual,
                StartDate = today.AddDays(1),
                EndDate = today.AddDays(2),
                Reason = "Long weekend",
                Status = LeaveStatus.Approved,
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            };
            managerRequest.Approvals.Add(new LeaveApproval
            {
                ApproverId = lineManager.Id,
                Step = 1,
                Status = LeaveStatus.Approved,
                ReviewedAt = DateTime.UtcNow.AddDays(-2),
                CreatedAt = DateTime.UtcNow.AddDays(-3)
            });
            _db.LeaveRequests.Add(managerRequest);

            // The PM's own pending request gives the line manager something to review,
            // proving the approval flow works at every level of the hierarchy.
            _db.LeaveRequests.Add(new LeaveRequest
            {
                UserId = projectManager.Id,
                Type = LeaveType.Annual,
                StartDate = today.AddDays(21),
                EndDate = today.AddDays(23),
                Reason = "Conference",
                Status = LeaveStatus.Pending,
                CreatedAt = DateTime.UtcNow
            });

            _db.SaveChanges();
        }
    }
}
