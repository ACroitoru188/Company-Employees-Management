using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CompanyEmployees.Persistence
{
    public class DatabaseSeeder
    {
        private readonly CompanyEmployeesDbContext _db;
        private readonly IPasswordHasher _hasher;

        // Days granted per leave type for every demo user (current year).
        private static readonly Dictionary<LeaveType, int> DefaultAllocations = new()
        {
            [LeaveType.Annual] = 21,
            [LeaveType.Sick] = 10,
            [LeaveType.Parental] = 10,
            [LeaveType.Unpaid] = 30
        };

        public DatabaseSeeder(CompanyEmployeesDbContext db, IPasswordHasher hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        public void Seed()
        {
            _db.Database.EnsureDeleted();
            _db.Database.EnsureCreated();

            // The manager must be saved first so the employees can reference his Id.
            var manager = SeedDemoUser("linemanager@siemens.com", "Demo Manager", UserRole.ProjectManager);
            var employee = SeedDemoUser("employee@siemens.com", "Demo Employee", UserRole.Employee, manager.Id);
            var colleague = SeedDemoUser("colleague@siemens.com", "Demo Colleague", UserRole.Employee, manager.Id);
            var admin = SeedDemoUser("itadmin@siemens.com", "Demo Admin", UserRole.Admin);

            SeedAllocations([manager, employee, colleague, admin]);
            SeedDemoRequests(employee, colleague, manager);
        }

        private User SeedDemoUser(string email, string name, UserRole role, Guid? managerId = null)
        {
            var existing = _db.Users.FirstOrDefault(u => u.Email == email);
            if (existing != null)
                return existing;

            var user = new User
            {
                Name = name,
                Email = email,
                PasswordHash = _hasher.HashPassword("Passw0rd!"),
                Role = role,
                Status = UserStatus.Active,
                ManagerId = managerId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);
            _db.SaveChanges();
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

        private void SeedDemoRequests(User employee, User colleague, User manager)
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
                ApproverId = manager.Id,
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
                ApproverId = manager.Id,
                Step = 1,
                Status = LeaveStatus.Approved,
                ReviewedAt = DateTime.UtcNow.AddDays(-5),
                CreatedAt = DateTime.UtcNow.AddDays(-6)
            });
            _db.LeaveRequests.Add(colleagueRequest);

            _db.SaveChanges();
        }
    }
}
