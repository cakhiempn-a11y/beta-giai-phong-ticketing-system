using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using BetaGiaiPhong.Data.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Runner;

public static class DemoSeeder
{
    public static async Task SeedAsync(BetaDbContext db)
    {
        if (Environment.GetEnvironmentVariable("BGP_ALLOW_DEMO_SEED") != "true")
            throw new InvalidOperationException("Set BGP_ALLOW_DEMO_SEED=true explicitly for synthetic data.");
        var password = Environment.GetEnvironmentVariable("BGP_DEMO_PASSWORD")
            ?? throw new InvalidOperationException("Set BGP_DEMO_PASSWORD to a non-shared local test password.");
        if (password.Length < 12) throw new InvalidOperationException("Demo password must be >=12 chars.");

        foreach (var (code, name) in new[] {
            ("CUSTOMER", "Khach hang"), ("STAFF", "Nhan vien"),
            ("MANAGER", "Quan ly"), ("ADMIN", "Quan tri") })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleCode == code))
                db.Roles.Add(new Role { RoleCode = code, RoleName = name });
        }
        foreach (var (code, name) in new[] { ("STANDARD", "Ghe thuong"), ("VIP", "Ghe VIP") })
        {
            if (!await db.SeatTypes.AnyAsync(t => t.SeatTypeCode == code))
                db.SeatTypes.Add(new SeatType { SeatTypeCode = code, TypeName = name, IsActive = true });
        }
        await db.SaveChangesAsync();

        var hasher = new PasswordHasher<AppUser>();
        foreach (var (mail, role) in new[] {
            ("customer1@demo.invalid", "CUSTOMER"), ("customer2@demo.invalid", "CUSTOMER"),
            ("staff@demo.invalid", "STAFF"), ("manager@demo.invalid", "MANAGER") })
        {
            var normalized = mail.ToUpperInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized);
            if (user is null)
            {
                user = new AppUser { Email = mail, NormalizedEmail = normalized,
                    DisplayName = "Demo " + role, Status = "ACTIVE", CreatedUtc = DateTime.UtcNow };
                user.PasswordHash = hasher.HashPassword(user, password);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            var roleId = await db.Roles.Where(r => r.RoleCode == role).Select(r => r.RoleId).SingleAsync();
            if (!await db.UserRoles.AnyAsync(x => x.UserId == user.UserId && x.RoleId == roleId))
                db.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = roleId });
        }
        await db.SaveChangesAsync();

        var room = await db.Rooms.SingleOrDefaultAsync(r => r.RoomCode == "PHONG_DEMO");
        if (room is null)
        {
            room = new Room { RoomCode = "PHONG_DEMO", RoomName = "Phong du lieu mo phong",
                Status = "ACTIVE" };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            for (var row = 0; row < 2; row++)
                for (short i = 1; i <= 5; i++)
                {
                    var label = row == 0 ? "A" : "B";
                    db.Seats.Add(new Seat {
                        RoomId = room.RoomId, RowLabel = label, ColumnNo = i,
                        SeatCode = label + i, SeatTypeCode = row == 0 ? "STANDARD" : "VIP",
                        IsActive = true
                    });
                }
            await db.SaveChangesAsync();
        }
        var movie = await db.Movies.SingleOrDefaultAsync(m => m.Title == "Phim mo phong - KHONG phai lich Beta");
        if (movie is null)
        {
            movie = new Movie { Title = "Phim mo phong - KHONG phai lich Beta",
                DurationMin = 105, GenreText = "Demo", Status = "PUBLISHED" };
            db.Movies.Add(movie);
            await db.SaveChangesAsync();
        }
        var managerId = await db.Users.Where(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            .Select(u => u.UserId).SingleAsync();
        var exists = await db.Showtimes.AnyAsync(s => s.RoomId == room.RoomId && s.StartUtc > DateTime.UtcNow);
        if (!exists)
        {
            var publisher = new ShowtimePublishingService(db, new SystemClock());
            var start = DateTime.UtcNow.AddDays(2).AddMinutes(5);
            var showtimeId = await publisher.PublishAsync(managerId, room.RoomId, movie.MovieId, start, 15,
                new[] { new SeatPriceInput("STANDARD", 70000), new SeatPriceInput("VIP", 90000) });
            Console.WriteLine($"Created synthetic showtime: {showtimeId}");
        }
        Console.WriteLine("Reference and synthetic demo data ready. NEVER treat these as actual Beta records.");
    }
}
