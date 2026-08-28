using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CompanyEmployees.Persistence;

/// <summary>
/// Provider-agnostic database seeder that idempotently populates the complete
/// enterprise demo dataset
/// </summary>
public static class DatabaseSeeder
{
    public static readonly Guid RomaniaRegionId = new("44444444-4444-4444-4444-444444444401");
    public static readonly Guid GermanyRegionId = new("44444444-4444-4444-4444-444444444413");
    public static readonly Guid UnitedKingdomRegionId = new("44444444-4444-4444-4444-444444444431");
    public static readonly Guid UnitedStatesRegionId = new("44444444-4444-4444-4444-444444444432");

    public static async Task SeedAsync(CompanyEmployeesDbContext db, CancellationToken ct = default)
    {
        await SeedRegionsAsync(db, ct);
        await SeedDepartmentsAsync(db, ct);
        await SeedUsersAsync(db, ct);
        await SeedLeaveAllocationsAsync(db, ct);
        await SeedLeaveRequestsAndApprovalsAsync(db, ct);
        await SeedCarryOverDemoAsync(db, ct);
        await SeedContractsAsync(db, ct);
    }

    private static async Task SeedRegionsAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        var regions = new (Guid Id, string Name, string Code)[]
        {
            (RomaniaRegionId, "Romania", "RO"),
            (GermanyRegionId, "Germany", "DE"),
            (UnitedKingdomRegionId, "United Kingdom", "GB"),
            (UnitedStatesRegionId, "United States", "US"),
            (new("44444444-4444-4444-4444-444444444404"), "Austria", "AT"),
            (new("44444444-4444-4444-4444-444444444412"), "France", "FR"),
            (new("44444444-4444-4444-4444-444444444417"), "Italy", "IT"),
            (new("44444444-4444-4444-4444-444444444426"), "Spain", "ES"),
            (new("44444444-4444-4444-4444-444444444420"), "Netherlands", "NL"),
            (new("44444444-4444-4444-4444-444444444422"), "Poland", "PL")
        };

        foreach (var (id, name, code) in regions)
        {
            if (!await db.Regions.AnyAsync(r => r.Id == id || r.Code == code, ct))
            {
                db.Regions.Add(new Region
                {
                    Id = id,
                    Name = name,
                    Code = code,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDepartmentsAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        var departments = new (Guid Id, string Name)[]
        {
            (new("22222222-0000-0000-0000-000000000001"), "Design"),
            (new("22222222-0000-0000-0000-000000000002"), "Production"),
            (new("22222222-0000-0000-0000-000000000003"), "HR"),
            (new("22222222-0000-0000-0000-000000000004"), "Engineering"),
            (new("22222222-0000-0000-0000-000000000005"), "Sales"),
            (new("22222222-0000-0000-0000-000000000006"), "Support"),
            (new("22222222-0000-0000-0000-000000000007"), "Marketing")
        };

        foreach (var (id, name) in departments)
        {
            if (!await db.Departments.AnyAsync(d => d.Id == id || d.Name == name, ct))
            {
                db.Departments.Add(new Department
                {
                    Id = id,
                    Name = name
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static (string City, string Site) GetCityAndSite(Guid id)
    {
        var rdIds = new HashSet<Guid>
        {
            new("11111111-0000-0000-0000-000000000002"),
            new("11111111-0000-0000-0000-000000000005"),
            new("11111111-0000-0000-0000-000000000008"),
            new("11111111-0000-0000-0000-000000000011"),
            new("11111111-0000-0000-0000-000000000029"),
            new("11111111-0000-0000-0000-000000000032"),
            new("11111111-0000-0000-0000-000000000035")
        };
        var disIds = new HashSet<Guid>
        {
            new("11111111-0000-0000-0000-000000000014"),
            new("11111111-0000-0000-0000-000000000017"),
            new("11111111-0000-0000-0000-000000000020"),
            new("11111111-0000-0000-0000-000000000023"),
            new("11111111-0000-0000-0000-000000000026"),
            new("11111111-0000-0000-0000-000000000260"),
            new("11111111-0000-0000-0000-000000000261"),
            new("11111111-0000-0000-0000-000000000262"),
            new("11111111-0000-0000-0000-000000000263"),
            new("11111111-0000-0000-0000-000000000264"),
            new("11111111-0000-0000-0000-000000000265")
        };
        var advantaIds = new HashSet<Guid>
        {
            new("11111111-0000-0000-0000-000000000003"),
            new("11111111-0000-0000-0000-000000000006"),
            new("11111111-0000-0000-0000-000000000009"),
            new("11111111-0000-0000-0000-000000000012"),
            new("11111111-0000-0000-0000-000000000015"),
            new("11111111-0000-0000-0000-000000000018"),
            new("11111111-0000-0000-0000-000000000021"),
            new("11111111-0000-0000-0000-000000000024"),
            new("11111111-0000-0000-0000-000000000027"),
            new("11111111-0000-0000-0000-000000000030"),
            new("11111111-0000-0000-0000-000000000033"),
            new("11111111-0000-0000-0000-000000000036")
        };

        if (rdIds.Contains(id)) return ("Brașov", "Siemens R&D");
        if (disIds.Contains(id)) return ("Brașov", "Siemens Digital Industry Software");
        if (advantaIds.Contains(id)) return ("Cluj-Napoca", "Siemens Advanta");
        return ("București", "Siemens HQ");
    }

    private static async Task SeedUsersAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        var defaultRegion = await db.Regions.FirstAsync(r => r.Id == RomaniaRegionId, ct);
        var hasher = new PasswordHasher<User>();

        var rawUsers = new (Guid Id, string Name, string Email, string Pwd, UserRole Role, Guid? MgrId, Guid? DeptId)[]
        {
            (new("11111111-0000-0000-0000-000000000001"), "Demo Admin", "itadmin@siemens.com", "User123!", UserRole.Admin, null, null),
            (new("11111111-0000-0000-0000-000000000002"), "Demo Line Manager", "linemanager@siemens.com", "User123!", UserRole.LineManager, new("11111111-0000-0000-0000-000000000001"), new("22222222-0000-0000-0000-000000000001")),
            (new("11111111-0000-0000-0000-000000000003"), "Demo Project Manager", "projectmanager@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000002"), null),
            (new("11111111-0000-0000-0000-000000000004"), "Demo Employee", "employee@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000003"), new("22222222-0000-0000-0000-000000000001")),
            (new("11111111-0000-0000-0000-000000000005"), "Demo Colleague", "colleague@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000003"), new("22222222-0000-0000-0000-000000000001")),
            (new("11111111-0000-0000-0000-000000000006"), "Paul Rusu", "admin.paul@siemens.com", "Admin123!", UserRole.Admin, null, null),
            (new("11111111-0000-0000-0000-000000000007"), "Monica Grigore", "admin.monica@siemens.com", "Admin123!", UserRole.Admin, null, null),
            (new("11111111-0000-0000-0000-000000000008"), "Victor Neagu", "admin.victor@siemens.com", "Admin123!", UserRole.Admin, null, null),
            (new("11111111-0000-0000-0000-000000000009"), "Elena Vasilescu", "lm.elena@siemens.com", "User123!", UserRole.LineManager, new("11111111-0000-0000-0000-000000000006"), new("22222222-0000-0000-0000-000000000003")),
            (new("11111111-0000-0000-0000-000000000010"), "Andreea Popa", "hr.andreea@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000009"), new("22222222-0000-0000-0000-000000000003")),
            (new("11111111-0000-0000-0000-000000000011"), "Bogdan Radu", "hr.bogdan@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000009"), new("22222222-0000-0000-0000-000000000003")),
            (new("11111111-0000-0000-0000-000000000012"), "Carmen Iliescu", "hr.carmen@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000009"), new("22222222-0000-0000-0000-000000000003")),
            (new("11111111-0000-0000-0000-000000000013"), "Daniel Stan", "hr.daniel@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000009"), new("22222222-0000-0000-0000-000000000003")),
            (new("11111111-0000-0000-0000-000000000014"), "Radu Constantin", "lm.radu@siemens.com", "User123!", UserRole.LineManager, new("11111111-0000-0000-0000-000000000007"), new("22222222-0000-0000-0000-000000000004")),
            (new("11111111-0000-0000-0000-000000000015"), "Diana Marinescu", "diana.marinescu@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000014"), new("22222222-0000-0000-0000-000000000004")),
            (new("11111111-0000-0000-0000-000000000016"), "Vlad Moldovan", "vlad.moldovan@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000015"), new("22222222-0000-0000-0000-000000000004")),
            (new("11111111-0000-0000-0000-000000000017"), "Simona Barbu", "simona.barbu@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000015"), new("22222222-0000-0000-0000-000000000004")),
            (new("11111111-0000-0000-0000-000000000018"), "Tudor Nistor", "tudor.nistor@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000015"), new("22222222-0000-0000-0000-000000000004")),
            (new("11111111-0000-0000-0000-000000000019"), "Larisa Dobre", "larisa.dobre@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000015"), new("22222222-0000-0000-0000-000000000004")),
            (new("11111111-0000-0000-0000-000000000020"), "Cristian Dumitru", "lm.cristian@siemens.com", "User123!", UserRole.LineManager, new("11111111-0000-0000-0000-000000000008"), new("22222222-0000-0000-0000-000000000005")),
            (new("11111111-0000-0000-0000-000000000021"), "Alexandru Stoica", "alexandru.stoica@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000020"), new("22222222-0000-0000-0000-000000000005")),
            (new("11111111-0000-0000-0000-000000000022"), "Cosmin Pavel", "cosmin.pavel@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000021"), new("22222222-0000-0000-0000-000000000005")),
            (new("11111111-0000-0000-0000-000000000023"), "Raluca Enache", "raluca.enache@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000021"), new("22222222-0000-0000-0000-000000000005")),
            (new("11111111-0000-0000-0000-000000000024"), "Florin Tudor", "florin.tudor@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000021"), new("22222222-0000-0000-0000-000000000005")),
            (new("11111111-0000-0000-0000-000000000025"), "Adriana Ciobanu", "adriana.ciobanu@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000021"), new("22222222-0000-0000-0000-000000000005")),
            (new("11111111-0000-0000-0000-000000000026"), "Mihai Georgescu", "lm.mihai@siemens.com", "User123!", UserRole.LineManager, new("11111111-0000-0000-0000-000000000006"), new("22222222-0000-0000-0000-000000000006")),
            (new("11111111-0000-0000-0000-000000000027"), "Gabriel Matei", "gabriel.matei@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000026"), new("22222222-0000-0000-0000-000000000006")),
            (new("11111111-0000-0000-0000-000000000028"), "Roxana Sandu", "roxana.sandu@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000026"), new("22222222-0000-0000-0000-000000000006")),
            (new("11111111-0000-0000-0000-000000000029"), "Marius Cretu", "marius.cretu@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000026"), new("22222222-0000-0000-0000-000000000006")),
            (new("11111111-0000-0000-0000-000000000030"), "Alina Toma", "alina.toma@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000026"), new("22222222-0000-0000-0000-000000000006")),
            (new("11111111-0000-0000-0000-000000000031"), "Sergiu Balan", "sergiu.balan@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000026"), new("22222222-0000-0000-0000-000000000006")),
            (new("11111111-0000-0000-0000-000000000032"), "Ioana Munteanu", "lm.ioana@siemens.com", "User123!", UserRole.LineManager, new("11111111-0000-0000-0000-000000000007"), new("22222222-0000-0000-0000-000000000007")),
            (new("11111111-0000-0000-0000-000000000033"), "Nicoleta Serban", "nicoleta.serban@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000032"), new("22222222-0000-0000-0000-000000000007")),
            (new("11111111-0000-0000-0000-000000000034"), "Bogdan Ilie", "bogdan.ilie@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000032"), new("22222222-0000-0000-0000-000000000007")),
            (new("11111111-0000-0000-0000-000000000035"), "Camelia Nicolae", "camelia.nicolae@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000032"), new("22222222-0000-0000-0000-000000000007")),
            (new("11111111-0000-0000-0000-000000000036"), "Stefan Voicu", "stefan.voicu@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000032"), new("22222222-0000-0000-0000-000000000007")),
            (new("11111111-0000-0000-0000-000000000037"), "Teodora Anghel", "teodora.anghel@siemens.com", "User123!", UserRole.Employee, new("11111111-0000-0000-0000-000000000032"), new("22222222-0000-0000-0000-000000000007"))
        };

        // First insert users without ManagerId to avoid foreign key cyclic dependency
        foreach (var (id, name, email, pwd, role, _, deptId) in rawUsers)
        {
            var (city, site) = GetCityAndSite(id);
            var normalizedEmail = email.ToUpperInvariant();
            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Id == id || u.NormalizedEmail == normalizedEmail, ct);
            if (existingUser != null)
            {
                if (string.IsNullOrEmpty(existingUser.City) || string.IsNullOrEmpty(existingUser.Site))
                {
                    existingUser.City = city;
                    existingUser.Site = site;
                }
                continue;
            }

            var user = new User
            {
                Id = id,
                Name = name,
                UserName = email,
                NormalizedUserName = normalizedEmail,
                Email = email,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                Role = role,
                Status = UserStatus.Active,
                RegionId = defaultRegion.Id,
                DepartmentId = deptId,
                City = city,
                Site = site,
                SecurityStamp = Guid.NewGuid().ToString("D"),
                ConcurrencyStamp = Guid.NewGuid().ToString("D"),
                LockoutEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, pwd);
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(ct);

        // Second pass: Set ManagerId links
        foreach (var (id, _, _, _, _, mgrId, _) in rawUsers)
        {
            if (mgrId.HasValue)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
                if (user != null && user.ManagerId != mgrId.Value)
                {
                    user.ManagerId = mgrId.Value;
                }
            }
        }

        // Set department managers
        var deptManagers = new (Guid DeptId, Guid MgrId)[]
        {
            (new("22222222-0000-0000-0000-000000000001"), new("11111111-0000-0000-0000-000000000002")),
            (new("22222222-0000-0000-0000-000000000003"), new("11111111-0000-0000-0000-000000000009")),
            (new("22222222-0000-0000-0000-000000000004"), new("11111111-0000-0000-0000-000000000014")),
            (new("22222222-0000-0000-0000-000000000005"), new("11111111-0000-0000-0000-000000000020")),
            (new("22222222-0000-0000-0000-000000000006"), new("11111111-0000-0000-0000-000000000026")),
            (new("22222222-0000-0000-0000-000000000007"), new("11111111-0000-0000-0000-000000000032"))
        };

        foreach (var (deptId, mgrId) in deptManagers)
        {
            var dept = await db.Departments.FirstOrDefaultAsync(d => d.Id == deptId, ct);
            if (dept != null && dept.ManagerId != mgrId)
            {
                dept.ManagerId = mgrId;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLeaveAllocationsAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        var currentYear = DateTime.Today.Year;
        var users = await db.Users.ToListAsync(ct);

        foreach (var user in users)
        {
            var allocations = new (LeaveType Type, int Days)[]
            {
                (LeaveType.Annual, 21),
                (LeaveType.Sick, 10),
                (LeaveType.Parental, 10),
                (LeaveType.Unpaid, 30)
            };

            foreach (var (type, days) in allocations)
            {
                if (!await db.LeaveAllocations.AnyAsync(la => la.UserId == user.Id && la.Year == currentYear && la.LeaveType == type, ct))
                {
                    db.LeaveAllocations.Add(new LeaveAllocation
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        LeaveType = type,
                        Year = currentYear,
                        NumberOfDays = days,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedLeaveRequestsAndApprovalsAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Predefined demo requests with status & approvers
        var requests = new (Guid Id, Guid UserId, int StartOffset, int EndOffset, string Reason, LeaveType Type, LeaveStatus Status, Guid? ApproverId)[]
        {
            (new("44444444-0000-0000-0000-000000000001"), new("11111111-0000-0000-0000-000000000004"), -12, -11, "Flu", LeaveType.Sick, LeaveStatus.Approved, new("11111111-0000-0000-0000-000000000003")),
            (new("44444444-0000-0000-0000-000000000002"), new("11111111-0000-0000-0000-000000000004"), 14, 18, "Mountain trip", LeaveType.Annual, LeaveStatus.Pending, null),
            (new("44444444-0000-0000-0000-000000000003"), new("11111111-0000-0000-0000-000000000005"), 5, 9, "City break", LeaveType.Annual, LeaveStatus.Approved, new("11111111-0000-0000-0000-000000000003")),
            (new("44444444-0000-0000-0000-000000000004"), new("11111111-0000-0000-0000-000000000003"), 1, 2, "Long weekend", LeaveType.Annual, LeaveStatus.Approved, new("11111111-0000-0000-0000-000000000002")),
            (new("44444444-0000-0000-0000-000000000005"), new("11111111-0000-0000-0000-000000000003"), 21, 23, "Conference", LeaveType.Annual, LeaveStatus.Pending, null),
            (new("44444444-0000-0000-0000-000000000006"), new("11111111-0000-0000-0000-000000000009"), -20, -19, "Annual leave", LeaveType.Annual, LeaveStatus.Pending, null),
            (new("44444444-0000-0000-0000-000000000007"), new("11111111-0000-0000-0000-000000000009"), -10, -8, "Sick leave", LeaveType.Sick, LeaveStatus.Approved, new("11111111-0000-0000-0000-000000000006")),
            (new("44444444-0000-0000-0000-000000000008"), new("11111111-0000-0000-0000-000000000010"), -5, -2, "Parental leave", LeaveType.Parental, LeaveStatus.Rejected, new("11111111-0000-0000-0000-000000000009")),
            (new("44444444-0000-0000-0000-000000000009"), new("11111111-0000-0000-0000-000000000010"), -2, 2, "Unpaid leave", LeaveType.Unpaid, LeaveStatus.Pending, null),
            (new("44444444-0000-0000-0000-000000000010"), new("11111111-0000-0000-0000-000000000011"), 1, 2, "Annual leave", LeaveType.Annual, LeaveStatus.Pending, null),
            (new("44444444-0000-0000-0000-000000000011"), new("11111111-0000-0000-0000-000000000011"), 3, 5, "Sick leave", LeaveType.Sick, LeaveStatus.Approved, new("11111111-0000-0000-0000-000000000009"))
        };

        foreach (var (id, userId, startOffset, endOffset, reason, type, status, approverId) in requests)
        {
            if (!await db.LeaveRequests.AnyAsync(lr => lr.Id == id, ct))
            {
                var userExists = await db.Users.AnyAsync(u => u.Id == userId, ct);
                if (!userExists) continue;

                var startDate = today.AddDays(startOffset);
                var endDate = today.AddDays(endOffset);

                var lr = new LeaveRequest
                {
                    Id = id,
                    UserId = userId,
                    StartDate = startDate,
                    EndDate = endDate,
                    Reason = reason,
                    Type = type,
                    Status = status,
                    CreatedAt = DateTime.UtcNow.AddDays(startOffset - 1)
                };
                db.LeaveRequests.Add(lr);

                if (approverId.HasValue && (status == LeaveStatus.Approved || status == LeaveStatus.Rejected))
                {
                    db.LeaveApprovals.Add(new LeaveApproval
                    {
                        Id = Guid.NewGuid(),
                        LeaveRequestId = id,
                        ApproverId = approverId.Value,
                        Step = 1,
                        Status = status,
                        ReviewedAt = DateTime.UtcNow.AddDays(startOffset),
                        CreatedAt = DateTime.UtcNow.AddDays(startOffset - 1)
                    });
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCarryOverDemoAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        const string email = "carryover.test@siemens.com";
        const string password = "User123!";
        var normalizedEmail = email.ToUpperInvariant();

        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);
        if (user is null)
        {
            var region = await db.Regions.FirstAsync(r => r.Id == RomaniaRegionId, ct);
            user = new User
            {
                Id = new("aaaaaaaa-0000-0000-0000-000000000001"),
                Name = "Carry-over Test",
                UserName = email,
                NormalizedUserName = normalizedEmail,
                Email = email,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
                Role = UserRole.Employee,
                Status = UserStatus.Active,
                RegionId = region.Id,
                SecurityStamp = Guid.NewGuid().ToString("D"),
                ConcurrencyStamp = Guid.NewGuid().ToString("D"),
                LockoutEnabled = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
        }

        var currentYear = DateTime.Today.Year;
        var previousYear = currentYear - 1;

        if (!await db.LeaveAllocations.AnyAsync(item => item.UserId == user.Id && item.Year == previousYear && item.LeaveType == LeaveType.Annual, ct))
        {
            db.LeaveAllocations.Add(new LeaveAllocation
            {
                Id = new("aaaaaaaa-0000-0000-0000-000000000002"),
                UserId = user.Id,
                LeaveType = LeaveType.Annual,
                Year = previousYear,
                NumberOfDays = 21,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!await db.LeaveAllocations.AnyAsync(item => item.UserId == user.Id && item.Year == currentYear && item.LeaveType == LeaveType.Annual, ct))
        {
            db.LeaveAllocations.Add(new LeaveAllocation
            {
                Id = new("aaaaaaaa-0000-0000-0000-000000000003"),
                UserId = user.Id,
                LeaveType = LeaveType.Annual,
                Year = currentYear,
                NumberOfDays = 21,
                CreatedAt = DateTime.UtcNow
            });
        }

        var previousSeptember = new DateOnly(previousYear, 9, 1);
        while (previousSeptember.DayOfWeek != DayOfWeek.Monday)
            previousSeptember = previousSeptember.AddDays(1);

        var leaveRequestId = new Guid("aaaaaaaa-0000-0000-0000-000000000004");
        if (!await db.LeaveRequests.AnyAsync(item => item.Id == leaveRequestId, ct))
        {
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = leaveRequestId,
                UserId = user.Id,
                Type = LeaveType.Annual,
                StartDate = previousSeptember,
                EndDate = previousSeptember.AddDays(4),
                Reason = "Carry-over demonstration",
                Status = LeaveStatus.Approved,
                CreatedAt = previousSeptember.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedContractsAsync(CompanyEmployeesDbContext db, CancellationToken ct)
    {
        var users = await db.Users.Include(u => u.Contracts).ToListAsync(ct);
        if (users.Count == 0) return;

        var random = new Random(12345);

        foreach (var user in users)
        {
            if (user.Contracts == null || user.Contracts.Count == 0)
            {
                var isDeterminate = random.Next(100) < 40;
                var startDaysAgo = random.Next(200, 1400);
                var startDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-startDaysAgo));
                DateOnly? endDate = isDeterminate
                    ? DateOnly.FromDateTime(DateTime.Today.AddDays(random.Next(90, 550)))
                    : null;

                db.Contracts.Add(new Contract
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Type = isDeterminate ? ContractType.Determinate : ContractType.Indeterminate,
                    StartDate = startDate,
                    EndDate = endDate,
                    Status = ContractStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
