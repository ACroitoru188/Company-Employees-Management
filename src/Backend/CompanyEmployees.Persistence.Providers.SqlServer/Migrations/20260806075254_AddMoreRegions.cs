using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompanyEmployees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoreRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Regions",
                columns: new[] { "Id", "Name", "Code", "IsActive" },
                values: new object[,]
                {
                    { new Guid("44444444-4444-4444-4444-444444444403"), "Australia", "AU", true },
                    { new Guid("44444444-4444-4444-4444-444444444404"), "Austria", "AT", true },
                    { new Guid("44444444-4444-4444-4444-444444444405"), "Belgium", "BE", true },
                    { new Guid("44444444-4444-4444-4444-444444444406"), "Brazil", "BR", true },
                    { new Guid("44444444-4444-4444-4444-444444444407"), "Canada", "CA", true },
                    { new Guid("44444444-4444-4444-4444-444444444408"), "China", "CN", true },
                    { new Guid("44444444-4444-4444-4444-444444444409"), "Czechia", "CZ", true },
                    { new Guid("44444444-4444-4444-4444-444444444410"), "Denmark", "DK", true },
                    { new Guid("44444444-4444-4444-4444-444444444411"), "Finland", "FI", true },
                    { new Guid("44444444-4444-4444-4444-444444444412"), "France", "FR", true },
                    { new Guid("44444444-4444-4444-4444-444444444413"), "Germany", "DE", true },
                    { new Guid("44444444-4444-4444-4444-444444444414"), "Hungary", "HU", true },
                    { new Guid("44444444-4444-4444-4444-444444444415"), "India", "IN", true },
                    { new Guid("44444444-4444-4444-4444-444444444416"), "Ireland", "IE", true },
                    { new Guid("44444444-4444-4444-4444-444444444417"), "Italy", "IT", true },
                    { new Guid("44444444-4444-4444-4444-444444444418"), "Japan", "JP", true },
                    { new Guid("44444444-4444-4444-4444-444444444419"), "Mexico", "MX", true },
                    { new Guid("44444444-4444-4444-4444-444444444420"), "Netherlands", "NL", true },
                    { new Guid("44444444-4444-4444-4444-444444444421"), "Norway", "NO", true },
                    { new Guid("44444444-4444-4444-4444-444444444422"), "Poland", "PL", true },
                    { new Guid("44444444-4444-4444-4444-444444444423"), "Portugal", "PT", true },
                    { new Guid("44444444-4444-4444-4444-444444444424"), "Singapore", "SG", true },
                    { new Guid("44444444-4444-4444-4444-444444444425"), "South Africa", "ZA", true },
                    { new Guid("44444444-4444-4444-4444-444444444426"), "Spain", "ES", true },
                    { new Guid("44444444-4444-4444-4444-444444444427"), "Sweden", "SE", true },
                    { new Guid("44444444-4444-4444-4444-444444444428"), "Switzerland", "CH", true },
                    { new Guid("44444444-4444-4444-4444-444444444429"), "Turkey", "TR", true },
                    { new Guid("44444444-4444-4444-4444-444444444430"), "United Arab Emirates", "AE", true },
                    { new Guid("44444444-4444-4444-4444-444444444431"), "United Kingdom", "GB", true },
                    { new Guid("44444444-4444-4444-4444-444444444432"), "United States", "US", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Regions",
                keyColumn: "Id",
                keyValues: new object[]
                {
                    new Guid("44444444-4444-4444-4444-444444444403"),
                    new Guid("44444444-4444-4444-4444-444444444404"),
                    new Guid("44444444-4444-4444-4444-444444444405"),
                    new Guid("44444444-4444-4444-4444-444444444406"),
                    new Guid("44444444-4444-4444-4444-444444444407"),
                    new Guid("44444444-4444-4444-4444-444444444408"),
                    new Guid("44444444-4444-4444-4444-444444444409"),
                    new Guid("44444444-4444-4444-4444-444444444410"),
                    new Guid("44444444-4444-4444-4444-444444444411"),
                    new Guid("44444444-4444-4444-4444-444444444412"),
                    new Guid("44444444-4444-4444-4444-444444444413"),
                    new Guid("44444444-4444-4444-4444-444444444414"),
                    new Guid("44444444-4444-4444-4444-444444444415"),
                    new Guid("44444444-4444-4444-4444-444444444416"),
                    new Guid("44444444-4444-4444-4444-444444444417"),
                    new Guid("44444444-4444-4444-4444-444444444418"),
                    new Guid("44444444-4444-4444-4444-444444444419"),
                    new Guid("44444444-4444-4444-4444-444444444420"),
                    new Guid("44444444-4444-4444-4444-444444444421"),
                    new Guid("44444444-4444-4444-4444-444444444422"),
                    new Guid("44444444-4444-4444-4444-444444444423"),
                    new Guid("44444444-4444-4444-4444-444444444424"),
                    new Guid("44444444-4444-4444-4444-444444444425"),
                    new Guid("44444444-4444-4444-4444-444444444426"),
                    new Guid("44444444-4444-4444-4444-444444444427"),
                    new Guid("44444444-4444-4444-4444-444444444428"),
                    new Guid("44444444-4444-4444-4444-444444444429"),
                    new Guid("44444444-4444-4444-4444-444444444430"),
                    new Guid("44444444-4444-4444-4444-444444444431"),
                    new Guid("44444444-4444-4444-4444-444444444432")
                });
        }
    }
}
