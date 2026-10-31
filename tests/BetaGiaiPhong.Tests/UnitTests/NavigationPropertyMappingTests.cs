using BetaGiaiPhong.Data.Entities;
using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BetaGiaiPhong.Tests.UnitTests;

public sealed class NavigationPropertyMappingTests
{
    [Fact]
    public void ModelBuilder_ValidatesAllNavigationProperties()
    {
        var options = new DbContextOptionsBuilder<BetaDbContext>()
            .UseSqlServer("Server=localhost;Database=DummyModelDb;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        using var db = new BetaDbContext(options);
        var model = db.Model;

        // Verify key navigation properties exist in EF Core metadata
        var movieEntity = model.FindEntityType(typeof(Movie));
        Assert.NotNull(movieEntity?.FindNavigation(nameof(Movie.Showtimes)));

        var roomEntity = model.FindEntityType(typeof(Room));
        Assert.NotNull(roomEntity?.FindNavigation(nameof(Room.Seats)));
        Assert.NotNull(roomEntity?.FindNavigation(nameof(Room.Showtimes)));

        var showtimeEntity = model.FindEntityType(typeof(Showtime));
        Assert.NotNull(showtimeEntity?.FindNavigation(nameof(Showtime.Movie)));
        Assert.NotNull(showtimeEntity?.FindNavigation(nameof(Showtime.Room)));
        Assert.NotNull(showtimeEntity?.FindNavigation(nameof(Showtime.ShowtimeSeats)));
        Assert.NotNull(showtimeEntity?.FindNavigation(nameof(Showtime.Orders)));

        var orderEntity = model.FindEntityType(typeof(BookingOrder));
        Assert.NotNull(orderEntity?.FindNavigation(nameof(BookingOrder.OrderSeats)));
        Assert.NotNull(orderEntity?.FindNavigation(nameof(BookingOrder.Payments)));

        var ticketEntity = model.FindEntityType(typeof(Ticket));
        Assert.NotNull(ticketEntity?.FindNavigation(nameof(Ticket.OrderSeat)));
        Assert.NotNull(ticketEntity?.FindNavigation(nameof(Ticket.TicketScans)));
    }
}
