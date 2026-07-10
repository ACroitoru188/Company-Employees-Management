using CompanyEmployees.Domain.Entities;
using CompanyEmployees.Domain.Enums;
using CompanyEmployees.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CompanyEmployees.Persistence
{
    public class DatabaseSeeder
    {
        private readonly CompanyEmployeesDbContext _db;
        private readonly IPasswordHasher _hasher;

        public DatabaseSeeder(CompanyEmployeesDbContext db, IPasswordHasher hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        public void Seed()
        {
            _db.Database.EnsureDeleted();
            _db.Database.EnsureCreated();
            SeedDemoUser("employee@siemens.com", "Demo Employee", UserRole.Employee);
            SeedDemoUser("linemanager@siemens.com", "Demo Manager", UserRole.ProjectManager);
            SeedDemoUser("itadmin@siemens.com", "Demo Admin", UserRole.Admin);
        }

        private void SeedDemoUser(string email, string name, UserRole role)
        {
            if (_db.Users.Any(u => u.Email == email))
                return;
            _db.Users.Add(new User
            {
                Name = name,
                Email = email,
                PasswordHash = _hasher.HashPassword("Passw0rd!"),
                Role = role,
                Status = UserStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            _db.SaveChanges();
        }
    }
}
