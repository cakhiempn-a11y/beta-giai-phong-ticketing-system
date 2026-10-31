using BetaGiaiPhong.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace BetaGiaiPhong.Data.Persistence.Migrations;

public partial class ReportView : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One batch; CREATE VIEW must be first non-comment statement in this command.
        migrationBuilder.Sql(@"CREATE OR ALTER VIEW dbo.vw_ShowtimeSales
AS
WITH TicketCounts AS (
    SELECT t.showtime_id, COUNT_BIG(*) AS tickets_sold
    FROM dbo.Tickets t
    INNER JOIN dbo.Orders o ON o.order_id = t.order_id AND o.status = 'PAID'
    GROUP BY t.showtime_id
), Revenue AS (
    SELECT o.showtime_id, SUM(CONVERT(DECIMAL(19,0), o.total_vnd)) AS revenue_vnd
    FROM dbo.Orders o
    WHERE o.status = 'PAID'
      AND EXISTS (SELECT 1 FROM dbo.Tickets t WHERE t.order_id = o.order_id)
    GROUP BY o.showtime_id
)
SELECT s.showtime_id, s.movie_id, s.room_id, s.start_utc,
       COALESCE(tc.tickets_sold, 0) AS tickets_sold,
       COALESCE(r.revenue_vnd, 0) AS revenue_vnd,
       COALESCE(s.sellable_capacity_snapshot, 0) AS sellable_capacity,
       CASE WHEN s.sellable_capacity_snapshot > 0
            THEN CAST(100.0 * COALESCE(tc.tickets_sold, 0) / s.sellable_capacity_snapshot AS DECIMAL(7,2))
            ELSE CAST(0 AS DECIMAL(7,2)) END AS occupancy_pct
FROM dbo.Showtimes s
LEFT JOIN TicketCounts tc ON tc.showtime_id = s.showtime_id
LEFT JOIN Revenue r ON r.showtime_id = s.showtime_id;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_ShowtimeSales;");
    }
}
