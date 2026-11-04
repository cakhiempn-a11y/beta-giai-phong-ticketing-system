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
            throw new InvalidOperationException("Set BGP_ALLOW_DEMO_SEED=true explicitly for academic demo seed data.");
        var password = Environment.GetEnvironmentVariable("BGP_DEMO_PASSWORD")
            ?? throw new InvalidOperationException("Set BGP_DEMO_PASSWORD to a non-shared local test password.");
        if (password.Length < 12) throw new InvalidOperationException("Demo password must be >=12 chars.");

        foreach (var (code, name) in new[] {
            ("CUSTOMER", "Khách hàng"), ("STAFF", "Nhân viên"),
            ("MANAGER", "Quản lý"), ("ADMIN", "Quản trị") })
        {
            if (!await db.Roles.AnyAsync(r => r.RoleCode == code))
                db.Roles.Add(new Role { RoleCode = code, RoleName = name });
        }
        foreach (var (code, name) in new[] { ("STANDARD", "Ghế thường"), ("VIP", "Ghế VIP") })
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
                user = new AppUser
                {
                    Email = mail,
                    NormalizedEmail = normalized,
                    DisplayName = "Demo " + role,
                    Status = "ACTIVE",
                    CreatedUtc = DateTime.UtcNow
                };
                user.PasswordHash = hasher.HashPassword(user, password);
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
            var roleId = await db.Roles.Where(r => r.RoleCode == role).Select(r => r.RoleId).SingleAsync();
            if (!await db.UserRoles.AnyAsync(x => x.UserId == user.UserId && x.RoleId == roleId))
                db.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = roleId });
        }
        await db.SaveChangesAsync();

        var room = await EnsureRoomWithSeatsAsync(db, "PHONG_DEMO", "Phòng 01 — Beta Giải Phóng", rows: 2, cols: 5, vipStartRow: 1);

        // Sync 31 real verified films (Wikidata + IMDb + TMDB Theatrical Assets + VN Cinema Age Labels)
        var clock = new SystemClock();
        var syncService = new MovieContentSyncService(db, clock);
        await syncService.SyncVerifiedShowcaseCatalogAsync(publishAll: true);

        var heroMovie = await db.Movies.SingleAsync(m => m.WikidataQid == "Q496266");
        var managerId = await db.Users.Where(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            .Select(u => u.UserId).SingleAsync();

        var exists = await db.Showtimes.AnyAsync(s => s.RoomId == room.RoomId && s.StartUtc > DateTime.UtcNow);
        if (!exists)
        {
            var publisher = new ShowtimePublishingService(db, clock);
            var start = DateTime.UtcNow.AddDays(2).AddMinutes(5);
            var showtimeId = await publisher.PublishAsync(
                managerId,
                room.RoomId,
                heroMovie.MovieId,
                start,
                15,
                new[] { new SeatPriceInput("STANDARD", 70000), new SeatPriceInput("VIP", 90000) });
            Console.WriteLine($"Created verified film showtime #{showtimeId} for '{heroMovie.Title}' ({heroMovie.OriginalTitle}, {heroMovie.ReleaseYear}).");
        }
        Console.WriteLine("Verified film catalog (31 real works) and academic schedule ready.");
    }

    public static async Task SeedVerifiedShowcaseAsync(BetaDbContext db)
    {
        await SeedAsync(db);

        var room2 = await EnsureRoomWithSeatsAsync(db, "PHONG_02", "Phòng 02 — Điện ảnh Tiêu chuẩn", rows: 4, cols: 8, vipStartRow: 2);
        var room3 = await EnsureRoomWithSeatsAsync(db, "PHONG_03", "Phòng 03 — Chuyên đề Nghệ thuật", rows: 3, cols: 6, vipStartRow: 2);
        var room4 = await EnsureRoomWithSeatsAsync(db, "PHONG_04", "Phòng 04 — IMAX Laser", rows: 4, cols: 8, vipStartRow: 2);
        var room5 = await EnsureRoomWithSeatsAsync(db, "PHONG_05", "Phòng 05 — Dolby Atmos", rows: 4, cols: 7, vipStartRow: 2);

        var managerId = await db.Users.Where(u => u.NormalizedEmail == "MANAGER@DEMO.INVALID")
            .Select(u => u.UserId).SingleAsync();

        var clock = new SystemClock();
        var publisher = new ShowtimePublishingService(db, clock);

        var vnOffset = TimeSpan.FromHours(7);
        var nowVn = DateTimeOffset.UtcNow.ToOffset(vnOffset);
        var tomorrowVnDate = nowVn.Date.AddDays(1);
        var dayAfterVnDate = nowVn.Date.AddDays(2);

        // Schedule 16 verified showcase films across PHONG_02..PHONG_05; keep 15 films at 0 showtimes for realistic catalogue filtering & pagination.
        var schedulePlan = new (string WikidataQid, long RoomId, DateTime LocalDate, int Hour, int Minute, long StdPrice, long VipPrice)[]
        {
            // Classic & Open-Movie Repertory (PHONG_02 & PHONG_03)
            ("Q496266", room2.RoomId, tomorrowVnDate, 19, 30, 75000, 95000),      // Mê trong mê (Charade, 1963)
            ("Q496266", room3.RoomId, tomorrowVnDate, 23, 15, 75000, 95000),      // Mê trong mê (Late showtime crossing midnight +1d)
            ("Q496266", room2.RoomId, dayAfterVnDate, 20, 0, 75000, 95000),       // Mê trong mê (Day +2)
            ("Q1066714", room2.RoomId, tomorrowVnDate, 14, 0, 70000, 90000),      // His Girl Friday (1940)
            ("Q623051", room2.RoomId, tomorrowVnDate, 22, 15, 80000, 100000),     // Night of the Living Dead (1968)
            ("Q115715270", room3.RoomId, tomorrowVnDate, 15, 0, 60000, 80000),    // Charge (2022)
            ("Q1173161", room3.RoomId, tomorrowVnDate, 16, 30, 60000, 80000),     // Tears of Steel (2012)
            ("Q925587", room3.RoomId, dayAfterVnDate, 15, 0, 60000, 80000),       // Sintel (2010)
            ("Q62770443", room3.RoomId, dayAfterVnDate, 17, 0, 60000, 80000),     // Spring (2019)

            // 2024–2025 Theatrical Blockbusters (PHONG_04 — IMAX Laser & PHONG_05 — Dolby Atmos)
            ("Q124694260", room4.RoomId, tomorrowVnDate, 10, 0, 95000, 125000),   // Địa Đạo: Mặt Trời Trong Bóng Tối (2025)
            ("Q124694260", room4.RoomId, tomorrowVnDate, 19, 30, 105000, 135000), // Địa Đạo: Mặt Trời Trong Bóng Tối (2025)
            ("Q124694260", room5.RoomId, dayAfterVnDate, 18, 30, 95000, 125000),  // Địa Đạo: Mặt Trời Trong Bóng Tối (2025)
            ("Q114246242", room4.RoomId, tomorrowVnDate, 13, 0, 110000, 145000),  // F1 (2025)
            ("Q114246242", room4.RoomId, dayAfterVnDate, 19, 45, 110000, 145000), // F1 (2025)
            ("Q116547790", room4.RoomId, tomorrowVnDate, 16, 15, 105000, 135000), // Superman (2025)
            ("Q116547790", room4.RoomId, dayAfterVnDate, 16, 30, 105000, 135000), // Superman (2025)
            ("Q109228991", room4.RoomId, dayAfterVnDate, 12, 45, 110000, 145000), // Dune: Hành Tinh Cát — Phần Hai (2024)
            ("Q102180106", room4.RoomId, tomorrowVnDate, 22, 15, 105000, 135000), // Deadpool & Wolverine (2024, +1d)
            ("Q133863621", room5.RoomId, tomorrowVnDate, 10, 15, 90000, 115000),  // Mưa Đỏ (2025)
            ("Q133863621", room5.RoomId, dayAfterVnDate, 15, 30, 90000, 115000),  // Mưa Đỏ (2025)
            ("Q136652736", room5.RoomId, tomorrowVnDate, 13, 15, 95000, 120000),  // Thám Tử Kiên: Kỳ Án Không Đầu (2025)
            ("Q136652736", room5.RoomId, dayAfterVnDate, 21, 15, 95000, 120000),  // Thám Tử Kiên: Kỳ Án Không Đầu (2025)
            ("Q112977213", room5.RoomId, tomorrowVnDate, 16, 15, 95000, 125000),  // Mickey 17 (2025)
            ("Q102180106", room5.RoomId, tomorrowVnDate, 19, 15, 100000, 130000), // Deadpool & Wolverine (2024)
            ("Q117322466", room5.RoomId, tomorrowVnDate, 22, 0, 95000, 120000),   // Quật Mộ Trùng Ma — Exhuma (2024, +1d)
            ("Q117322466", room5.RoomId, dayAfterVnDate, 12, 30, 90000, 115000)   // Quật Mộ Trùng Ma — Exhuma (2024)
        };

        int createdShowtimes = 0;
        foreach (var slot in schedulePlan)
        {
            var movie = await db.Movies.SingleOrDefaultAsync(m => m.WikidataQid == slot.WikidataQid);
            if (movie is null) continue;

            var startVn = new DateTimeOffset(
                slot.LocalDate.Year, slot.LocalDate.Month, slot.LocalDate.Day,
                slot.Hour, slot.Minute, 0, vnOffset);
            var startUtc = startVn.UtcDateTime;
            var endUtc = startUtc.AddMinutes(movie.DurationMin);

            bool conflict = await db.Showtimes.AnyAsync(s =>
                s.RoomId == slot.RoomId &&
                s.Status != "CANCELLED" &&
                startUtc < s.EndUtc.AddMinutes(s.BufferMinutes) &&
                s.StartUtc < endUtc.AddMinutes(15));

            if (!conflict && startUtc > DateTime.UtcNow.AddMinutes(10))
            {
                await publisher.PublishAsync(
                    managerId,
                    slot.RoomId,
                    movie.MovieId,
                    startUtc,
                    15,
                    new[]
                    {
                        new SeatPriceInput("STANDARD", slot.StdPrice),
                        new SeatPriceInput("VIP", slot.VipPrice)
                    });
                createdShowtimes++;
            }
        }

        Console.WriteLine($"SEED_VERIFIED_SHOWCASE_READY: {createdShowtimes} additional showcase showtime(s) published across PHONG_02..PHONG_05.");
    }

    private static async Task<Room> EnsureRoomWithSeatsAsync(
        BetaDbContext db,
        string roomCode,
        string roomName,
        int rows,
        short cols,
        int vipStartRow)
    {
        var room = await db.Rooms.SingleOrDefaultAsync(r => r.RoomCode == roomCode);
        if (room is not null)
        {
            return room;
        }

        room = new Room
        {
            RoomCode = roomCode,
            RoomName = roomName,
            Status = "ACTIVE"
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        for (int r = 0; r < rows; r++)
        {
            var rowLabel = ((char)('A' + r)).ToString();
            var seatType = r >= vipStartRow ? "VIP" : "STANDARD";
            for (short c = 1; c <= cols; c++)
            {
                db.Seats.Add(new Seat
                {
                    RoomId = room.RoomId,
                    RowLabel = rowLabel,
                    ColumnNo = c,
                    SeatCode = rowLabel + c,
                    SeatTypeCode = seatType,
                    IsActive = true
                });
            }
        }
        await db.SaveChangesAsync();
        return room;
    }
}
