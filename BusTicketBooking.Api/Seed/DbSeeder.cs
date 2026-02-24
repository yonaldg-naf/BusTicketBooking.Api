using BusTicketBooking.Contexts;
using BusTicketBooking.Interfaces;
using BusTicketBooking.Models;
using Microsoft.EntityFrameworkCore;

namespace BusTicketBooking.Seed
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext db, IPasswordService passwords, IConfiguration config, ILogger logger)
        {
            // Ensure database exists/migrated
            await db.Database.MigrateAsync();

            // 1) Admin user
            string adminUser = config["Seed:Admin:Username"] ?? "admin";
            string adminEmail = config["Seed:Admin:Email"] ?? "admin@btb.local";
            string adminPass = config["Seed:Admin:Password"] ?? "Admin@123";

            if (!await db.Users.AnyAsync(u => u.Username == adminUser))
            {
                var user = new User
                {
                    Username = adminUser,
                    Email = adminEmail,
                    FullName = "System Administrator",
                    Role = Roles.Admin
                };
                user.PasswordHash = passwords.Hash(user, adminPass);
                db.Users.Add(user);
                logger.LogInformation("Seeded Admin user: {Username}", adminUser);
            }

            // 2) Operator user + Operator profile (optional)
            string opUser = config["Seed:Operator:Username"] ?? "operator";
            string opEmail = config["Seed:Operator:Email"] ?? "operator@btb.local";
            string opPass = config["Seed:Operator:Password"] ?? "Operator@123";
            bool createOperator = bool.TryParse(config["Seed:Operator:Enabled"], out var opEnabled) ? opEnabled : true;

            if (createOperator && !await db.Users.AnyAsync(u => u.Username == opUser))
            {
                var op = new User
                {
                    Username = opUser,
                    Email = opEmail,
                    FullName = "Default Operator",
                    Role = Roles.Operator
                };
                op.PasswordHash = passwords.Hash(op, opPass);
                db.Users.Add(op);
                await db.SaveChangesAsync(); // ensure Id

                db.BusOperators.Add(new BusOperator
                {
                    UserId = op.Id,
                    CompanyName = "Sample Bus Operator",
                    SupportPhone = "+91-00000-00000"
                });

                logger.LogInformation("Seeded Operator user/profile: {Username}", opUser);
            }

            // 3) Minimal lookup data (Stops) if empty — helpful for quick tests
            if (!await db.Stops.AnyAsync())
            {
                db.Stops.AddRange(
                    new Stop { City = "Mumbai", Name = "Borivali East" },
                    new Stop { City = "Pune", Name = "Wakad Bridge" }
                );
                logger.LogInformation("Seeded sample Stops (Mumbai/Pune).");
            }

            await db.SaveChangesAsync();
        }
    }
}
