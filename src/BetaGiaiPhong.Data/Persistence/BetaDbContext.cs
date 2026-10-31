using BetaGiaiPhong.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Persistence;

public sealed class BetaDbContext(DbContextOptions<BetaDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<SeatType> SeatTypes => Set<SeatType>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Showtime> Showtimes => Set<Showtime>();
    public DbSet<SeatPrice> SeatPrices => Set<SeatPrice>();
    public DbSet<BookingOrder> Orders => Set<BookingOrder>();
    public DbSet<ShowtimeSeat> ShowtimeSeats => Set<ShowtimeSeat>();
    public DbSet<OrderSeat> OrderSeats => Set<OrderSeat>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketScan> TicketScans => Set<TicketScan>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ShowtimeSalesSummary> ShowtimeSales => Set<ShowtimeSalesSummary>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        BetaModelConfiguration.Configure(modelBuilder);
    }
}
