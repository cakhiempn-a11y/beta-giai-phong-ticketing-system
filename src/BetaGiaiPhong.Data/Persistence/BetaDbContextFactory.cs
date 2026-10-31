using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BetaGiaiPhong.Data.Persistence;

public sealed class BetaDbContextFactory : IDesignTimeDbContextFactory<BetaDbContext>
{
    public BetaDbContext CreateDbContext(string[] args)
    {
        // Design-time creation does not connect. Runtime operations must supply a real connection string.
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__BetaDb")
            ?? "Server=localhost;Database=BetaGiaiPhongDev;Integrated Security=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<BetaDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(BetaDbContext).Assembly.FullName))
            .Options;
        return new BetaDbContext(options);
    }
}
