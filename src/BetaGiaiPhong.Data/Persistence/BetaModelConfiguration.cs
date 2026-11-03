using BetaGiaiPhong.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BetaGiaiPhong.Data.Persistence;

public static class BetaModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.Entity<AppUser>(b =>
        {
            b.ToTable("Users", t =>
            {
                t.HasCheckConstraint("CK_Users_01", @"(status IN ('ACTIVE','DISABLED'))");
            });
            b.HasKey(e => e.UserId).HasName("PK_Users");
            b.HasAlternateKey(e => e.NormalizedEmail).HasName("UQ_Users_01");
            b.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.Email).HasColumnName("email").HasColumnType("nvarchar(256)").IsRequired();
            b.Property(e => e.NormalizedEmail).HasColumnName("normalized_email").HasColumnType("nvarchar(256)").IsRequired();
            b.Property(e => e.PasswordHash).HasColumnName("password_hash").HasColumnType("nvarchar(512)").IsRequired();
            b.Property(e => e.DisplayName).HasColumnName("display_name").HasColumnType("nvarchar(120)").IsRequired();
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(12)").IsRequired().HasDefaultValueSql(@"('ACTIVE')");
            b.Property(e => e.CreatedUtc).HasColumnName("created_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(e => e.RoleId).HasName("PK_Roles");
            b.HasAlternateKey(e => e.RoleCode).HasName("UQ_Roles_01");
            b.Property(e => e.RoleId).HasColumnName("role_id").HasColumnType("int").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.RoleCode).HasColumnName("role_code").HasColumnType("varchar(20)").IsRequired();
            b.Property(e => e.RoleName).HasColumnName("role_name").HasColumnType("nvarchar(80)").IsRequired();
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.ToTable("UserRoles");
            b.HasKey(e => new { e.UserId, e.RoleId }).HasName("PK_UserRoles");
            b.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("bigint");
            b.Property(e => e.RoleId).HasColumnName("role_id").HasColumnType("int");
            b.HasOne(e => e.User).WithMany(u => u.UserRoles).HasForeignKey(e => e.UserId).HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_UserRoles_Users_01");
            b.HasOne(e => e.Role).WithMany(r => r.UserRoles).HasForeignKey(e => e.RoleId).HasPrincipalKey(r => r.RoleId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_UserRoles_Roles_02");
        });

        modelBuilder.Entity<Movie>(b =>
        {
            b.ToTable("Movies", t =>
            {
                t.HasCheckConstraint("CK_Movies_01", @"(duration_min BETWEEN 1 AND 480)");
                t.HasCheckConstraint("CK_Movies_02", @"(status IN ('DRAFT','PUBLISHED','ARCHIVED'))");
                t.HasCheckConstraint("CK_Movies_03", @"(release_year IS NULL OR release_year BETWEEN 1888 AND 2100)");
                t.HasCheckConstraint("CK_Movies_04", @"(artwork_source IS NULL OR artwork_source IN ('TMDB','WIKIMEDIA_COMMONS','DISTRIBUTOR','PENDING_TMDB_API_KEY','NONE'))");
                t.HasCheckConstraint("CK_Movies_05", @"(rating_source IS NULL OR rating_source IN ('IMDB','TMDB'))");
                t.HasCheckConstraint("CK_Movies_06", @"(rating_value IS NULL OR (rating_scale_max IS NOT NULL AND rating_scale_max > 0 AND rating_value >= 0 AND rating_value <= rating_scale_max AND rating_vote_count IS NOT NULL AND rating_vote_count >= 0))");
                t.HasCheckConstraint("CK_Movies_07", @"(cast_json IS NULL OR ISJSON(cast_json) = 1)");
                t.HasCheckConstraint("CK_Movies_08", @"(age_label_market IS NULL OR age_label_market IN ('VN'))");
            });
            b.HasKey(e => e.MovieId).HasName("PK_Movies");
            b.Property(e => e.MovieId).HasColumnName("movie_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.Title).HasColumnName("title").HasColumnType("nvarchar(250)").IsRequired();
            b.Property(e => e.DurationMin).HasColumnName("duration_min").HasColumnType("smallint");
            b.Property(e => e.GenreText).HasColumnName("genre_text").HasColumnType("nvarchar(160)");
            b.Property(e => e.AgeLabel).HasColumnName("age_label").HasColumnType("nvarchar(24)");
            b.Property(e => e.Synopsis).HasColumnName("synopsis").HasColumnType("nvarchar(max)");
            b.Property(e => e.PosterUrl).HasColumnName("poster_url").HasColumnType("nvarchar(500)");
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(12)").IsRequired().HasDefaultValueSql(@"('DRAFT')");
            b.Property(e => e.OriginalTitle).HasColumnName("original_title").HasColumnType("nvarchar(250)");
            b.Property(e => e.ReleaseYear).HasColumnName("release_year").HasColumnType("smallint");
            b.Property(e => e.ReleaseDateVn).HasColumnName("release_date_vn").HasColumnType("date");
            b.Property(e => e.OriginCountry).HasColumnName("origin_country").HasColumnType("nvarchar(100)");
            b.Property(e => e.OriginalLanguage).HasColumnName("original_language").HasColumnType("varchar(16)");
            b.Property(e => e.TmdbId).HasColumnName("tmdb_id").HasColumnType("int");
            b.Property(e => e.ImdbId).HasColumnName("imdb_id").HasColumnType("varchar(16)");
            b.Property(e => e.WikidataQid).HasColumnName("wikidata_qid").HasColumnType("varchar(16)");
            b.Property(e => e.PosterW342Url).HasColumnName("poster_w342_url").HasColumnType("nvarchar(500)");
            b.Property(e => e.BackdropUrl).HasColumnName("backdrop_url").HasColumnType("nvarchar(500)");
            b.Property(e => e.BackdropW780Url).HasColumnName("backdrop_w780_url").HasColumnType("nvarchar(500)");
            b.Property(e => e.TitleLogoUrl).HasColumnName("title_logo_url").HasColumnType("nvarchar(500)");
            b.Property(e => e.OfficialTrailerYoutubeId).HasColumnName("official_trailer_youtube_id").HasColumnType("varchar(32)");
            b.Property(e => e.ArtworkSource).HasColumnName("artwork_source").HasColumnType("varchar(32)");
            b.Property(e => e.ArtworkLicenseText).HasColumnName("artwork_license_text").HasColumnType("nvarchar(300)");
            b.Property(e => e.ArtworkAttributionText).HasColumnName("artwork_attribution_text").HasColumnType("nvarchar(500)");
            b.Property(e => e.RatingSource).HasColumnName("rating_source").HasColumnType("varchar(32)");
            b.Property(e => e.RatingValue).HasColumnName("rating_value").HasColumnType("decimal(3,1)");
            b.Property(e => e.RatingScaleMax).HasColumnName("rating_scale_max").HasColumnType("decimal(4,1)");
            b.Property(e => e.RatingVoteCount).HasColumnName("rating_vote_count").HasColumnType("int");
            b.Property(e => e.RatingFetchedUtc).HasColumnName("rating_fetched_utc").HasColumnType("datetime2(0)");
            b.Property(e => e.AgeLabelMarket).HasColumnName("age_label_market").HasColumnType("varchar(8)");
            b.Property(e => e.AgeLabelSource).HasColumnName("age_label_source").HasColumnType("nvarchar(200)");
            b.Property(e => e.DirectorNames).HasColumnName("director_names").HasColumnType("nvarchar(300)");
            b.Property(e => e.CastJson).HasColumnName("cast_json").HasColumnType("nvarchar(max)");
            b.Property(e => e.MetadataSource).HasColumnName("metadata_source").HasColumnType("varchar(32)");
            b.Property(e => e.ExternalSyncedUtc).HasColumnName("external_synced_utc").HasColumnType("datetime2(0)");
            b.Property(e => e.ExternalExpiresUtc).HasColumnName("external_expires_utc").HasColumnType("datetime2(0)");
            b.Property(e => e.ManualOverrideMask).HasColumnName("manual_override_mask").HasColumnType("nvarchar(500)");
        });

        modelBuilder.Entity<Room>(b =>
        {
            b.ToTable("Rooms", t =>
            {
                t.HasCheckConstraint("CK_Rooms_01", @"(status IN ('ACTIVE','INACTIVE'))");
            });
            b.HasKey(e => e.RoomId).HasName("PK_Rooms");
            b.HasAlternateKey(e => e.RoomCode).HasName("UQ_Rooms_01");
            b.Property(e => e.RoomId).HasColumnName("room_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.RoomCode).HasColumnName("room_code").HasColumnType("varchar(24)").IsRequired();
            b.Property(e => e.RoomName).HasColumnName("room_name").HasColumnType("nvarchar(100)").IsRequired();
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(12)").IsRequired().HasDefaultValueSql(@"('ACTIVE')");
        });

        modelBuilder.Entity<SeatType>(b =>
        {
            b.ToTable("SeatTypes");
            b.HasKey(e => e.SeatTypeCode).HasName("PK_SeatTypes");
            b.Property(e => e.SeatTypeCode).HasColumnName("seat_type_code").HasColumnType("varchar(20)").IsRequired();
            b.Property(e => e.TypeName).HasColumnName("type_name").HasColumnType("nvarchar(80)").IsRequired();
            b.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("bit").HasDefaultValueSql(@"(1)");
        });

        modelBuilder.Entity<Seat>(b =>
        {
            b.ToTable("Seats", t =>
            {
                t.HasCheckConstraint("CK_Seats_01", @"(column_no > 0)");
            });
            b.HasKey(e => e.SeatId).HasName("PK_Seats");
            b.HasAlternateKey(e => new { e.RoomId, e.SeatCode }).HasName("UQ_Seats_01");
            b.HasAlternateKey(e => new { e.SeatId, e.RoomId }).HasName("UQ_Seats_02");
            b.Property(e => e.SeatId).HasColumnName("seat_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.RoomId).HasColumnName("room_id").HasColumnType("bigint");
            b.Property(e => e.SeatCode).HasColumnName("seat_code").HasColumnType("varchar(16)").IsRequired();
            b.Property(e => e.SeatTypeCode).HasColumnName("seat_type_code").HasColumnType("varchar(20)").IsRequired();
            b.Property(e => e.RowLabel).HasColumnName("row_label").HasColumnType("varchar(8)").IsRequired();
            b.Property(e => e.ColumnNo).HasColumnName("column_no").HasColumnType("smallint");
            b.Property(e => e.IsActive).HasColumnName("is_active").HasColumnType("bit").HasDefaultValueSql(@"(1)");
            b.HasOne(e => e.Room).WithMany(r => r.Seats).HasForeignKey(e => e.RoomId).HasPrincipalKey(r => r.RoomId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Seats_Rooms_01");
            b.HasOne(e => e.SeatType).WithMany(st => st.Seats).HasForeignKey(e => e.SeatTypeCode).HasPrincipalKey(st => st.SeatTypeCode).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Seats_SeatTypes_02");
        });

        modelBuilder.Entity<Showtime>(b =>
        {
            b.ToTable("Showtimes", t =>
            {
                t.HasCheckConstraint("CK_Showtimes_01", @"(start_utc < end_utc)");
                t.HasCheckConstraint("CK_Showtimes_02", @"(buffer_minutes >= 0)");
                t.HasCheckConstraint("CK_Showtimes_03", @"(sellable_capacity_snapshot IS NULL OR sellable_capacity_snapshot >= 0)");
                t.HasCheckConstraint("CK_Showtimes_04", @"(status IN ('DRAFT','ON_SALE','CLOSED','CANCELLED'))");
                t.HasCheckConstraint("CK_Showtimes_05", @"(status <> 'ON_SALE' OR sellable_capacity_snapshot IS NOT NULL)");
            });
            b.HasKey(e => e.ShowtimeId).HasName("PK_Showtimes");
            b.HasAlternateKey(e => new { e.ShowtimeId, e.RoomId }).HasName("UQ_Showtimes_01");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.MovieId).HasColumnName("movie_id").HasColumnType("bigint");
            b.Property(e => e.RoomId).HasColumnName("room_id").HasColumnType("bigint");
            b.Property(e => e.StartUtc).HasColumnName("start_utc").HasColumnType("datetime2(3)");
            b.Property(e => e.EndUtc).HasColumnName("end_utc").HasColumnType("datetime2(3)");
            b.Property(e => e.BufferMinutes).HasColumnName("buffer_minutes").HasColumnType("smallint").HasDefaultValueSql(@"(15)");
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(12)").IsRequired().HasDefaultValueSql(@"('DRAFT')");
            b.Property(e => e.SellableCapacitySnapshot).HasColumnName("sellable_capacity_snapshot").HasColumnType("int");
            b.Property(e => e.CreatedBy).HasColumnName("created_by").HasColumnType("bigint");
            b.Property(e => e.CreatedUtc).HasColumnName("created_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.HasOne(e => e.Movie).WithMany(m => m.Showtimes).HasForeignKey(e => e.MovieId).HasPrincipalKey(m => m.MovieId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Showtimes_Movies_01");
            b.HasOne(e => e.Room).WithMany(r => r.Showtimes).HasForeignKey(e => e.RoomId).HasPrincipalKey(r => r.RoomId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Showtimes_Rooms_02");
            b.HasOne(e => e.Creator).WithMany().HasForeignKey(e => e.CreatedBy).HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Showtimes_Users_03");
        });

        modelBuilder.Entity<SeatPrice>(b =>
        {
            b.ToTable("SeatPrices", t =>
            {
                t.HasCheckConstraint("CK_SeatPrices_01", @"(amount_vnd > 0)");
            });
            b.HasKey(e => new { e.ShowtimeId, e.SeatTypeCode }).HasName("PK_SeatPrices");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint");
            b.Property(e => e.SeatTypeCode).HasColumnName("seat_type_code").HasColumnType("varchar(20)").IsRequired();
            b.Property(e => e.AmountVnd).HasColumnName("amount_vnd").HasColumnType("bigint");
            b.Property(e => e.UpdatedUtc).HasColumnName("updated_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.HasOne(e => e.Showtime).WithMany(s => s.SeatPrices).HasForeignKey(e => e.ShowtimeId).HasPrincipalKey(s => s.ShowtimeId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SeatPrices_Showtimes_01");
            b.HasOne(e => e.SeatType).WithMany(st => st.SeatPrices).HasForeignKey(e => e.SeatTypeCode).HasPrincipalKey(st => st.SeatTypeCode).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_SeatPrices_SeatTypes_02");
        });

        modelBuilder.Entity<BookingOrder>(b =>
        {
            b.ToTable("Orders", t =>
            {
                t.HasCheckConstraint("CK_Orders_01", @"(seat_count BETWEEN 1 AND 8)");
                t.HasCheckConstraint("CK_Orders_02", @"(subtotal_vnd >= 0)");
                t.HasCheckConstraint("CK_Orders_03", @"(fee_vnd >= 0)");
                t.HasCheckConstraint("CK_Orders_04", @"(discount_vnd >= 0)");
                t.HasCheckConstraint("CK_Orders_05", @"(total_vnd >= 0)");
                t.HasCheckConstraint("CK_Orders_06", @"(total_vnd = subtotal_vnd + fee_vnd - discount_vnd)");
                t.HasCheckConstraint("CK_Orders_07", @"(expires_utc > created_utc)");
                t.HasCheckConstraint("CK_Orders_08", @"(status IN ('PENDING_PAYMENT','PAID','FAILED','CANCELLED','EXPIRED','NEEDS_REVIEW'))");
                t.HasCheckConstraint("CK_Orders_09", @"(((status = 'PAID' AND paid_utc IS NOT NULL) OR (status <> 'PAID' AND paid_utc IS NULL)))");
            });
            b.HasKey(e => e.OrderId).HasName("PK_Orders");
            b.HasAlternateKey(e => e.OrderCode).HasName("UQ_Orders_01");
            b.HasAlternateKey(e => new { e.OrderId, e.ShowtimeId }).HasName("UQ_Orders_02");
            b.Property(e => e.OrderId).HasColumnName("order_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.OrderCode).HasColumnName("order_code").HasColumnType("varchar(40)").IsRequired();
            b.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("bigint");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint");
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(20)").IsRequired().HasDefaultValueSql(@"('PENDING_PAYMENT')");
            b.Property(e => e.SeatCount).HasColumnName("seat_count").HasColumnType("tinyint");
            b.Property(e => e.SubtotalVnd).HasColumnName("subtotal_vnd").HasColumnType("bigint");
            b.Property(e => e.FeeVnd).HasColumnName("fee_vnd").HasColumnType("bigint").HasDefaultValueSql(@"(0)");
            b.Property(e => e.DiscountVnd).HasColumnName("discount_vnd").HasColumnType("bigint").HasDefaultValueSql(@"(0)");
            b.Property(e => e.TotalVnd).HasColumnName("total_vnd").HasColumnType("bigint");
            b.Property(e => e.CreatedUtc).HasColumnName("created_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.Property(e => e.ExpiresUtc).HasColumnName("expires_utc").HasColumnType("datetime2(3)");
            b.Property(e => e.PaidUtc).HasColumnName("paid_utc").HasColumnType("datetime2(3)");
            b.Property(e => e.RowVer).HasColumnName("row_ver").HasColumnType("rowversion").IsRequired().IsRowVersion();
            b.HasOne(e => e.User).WithMany(u => u.Orders).HasForeignKey(e => e.UserId).HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Orders_Users_01");
            b.HasOne(e => e.Showtime).WithMany(s => s.Orders).HasForeignKey(e => e.ShowtimeId).HasPrincipalKey(s => s.ShowtimeId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Orders_Showtimes_02");
        });

        modelBuilder.Entity<ShowtimeSeat>(b =>
        {
            b.ToTable("ShowtimeSeats", t =>
            {
                t.HasCheckConstraint("CK_ShowtimeSeats_01", @"(((state IN ('AVAILABLE','BLOCKED') AND owner_order_id IS NULL AND hold_expires_utc IS NULL) OR (state='HELD' AND owner_order_id IS NOT NULL AND hold_expires_utc IS NOT NULL) OR (state='SOLD' AND owner_order_id IS NOT NULL AND hold_expires_utc IS NULL)))");
            });
            b.HasKey(e => new { e.ShowtimeId, e.SeatId }).HasName("PK_ShowtimeSeats");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint");
            b.Property(e => e.SeatId).HasColumnName("seat_id").HasColumnType("bigint");
            b.Property(e => e.RoomId).HasColumnName("room_id").HasColumnType("bigint");
            b.Property(e => e.State).HasColumnName("state").HasColumnType("varchar(12)").IsRequired().HasDefaultValueSql(@"('AVAILABLE')");
            b.Property(e => e.OwnerOrderId).HasColumnName("owner_order_id").HasColumnType("bigint");
            b.Property(e => e.HoldExpiresUtc).HasColumnName("hold_expires_utc").HasColumnType("datetime2(3)");
            b.Property(e => e.RowVer).HasColumnName("row_ver").HasColumnType("rowversion").IsRequired().IsRowVersion();
            b.HasOne(e => e.Showtime).WithMany(s => s.ShowtimeSeats).HasForeignKey(e => new { e.ShowtimeId, e.RoomId }).HasPrincipalKey(s => new { s.ShowtimeId, s.RoomId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ShowtimeSeats_Showtimes_01");
            b.HasOne(e => e.Seat).WithMany(s => s.ShowtimeSeats).HasForeignKey(e => new { e.SeatId, e.RoomId }).HasPrincipalKey(s => new { s.SeatId, s.RoomId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ShowtimeSeats_Seats_02");
            b.HasOne(e => e.OwnerOrder).WithMany(o => o.OwnedShowtimeSeats).HasForeignKey(e => new { e.OwnerOrderId, e.ShowtimeId }).HasPrincipalKey(o => new { o.OrderId, o.ShowtimeId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_ShowtimeSeats_Orders_03");
        });

        modelBuilder.Entity<OrderSeat>(b =>
        {
            b.ToTable("OrderSeats", t =>
            {
                t.HasCheckConstraint("CK_OrderSeats_01", @"(unit_price_vnd > 0)");
            });
            b.HasKey(e => new { e.OrderId, e.SeatId }).HasName("PK_OrderSeats");
            b.HasAlternateKey(e => new { e.OrderId, e.ShowtimeId, e.SeatId }).HasName("UQ_OrderSeats_01");
            b.Property(e => e.OrderId).HasColumnName("order_id").HasColumnType("bigint");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint");
            b.Property(e => e.SeatId).HasColumnName("seat_id").HasColumnType("bigint");
            b.Property(e => e.SeatTypeSnapshot).HasColumnName("seat_type_snapshot").HasColumnType("varchar(20)").IsRequired();
            b.Property(e => e.UnitPriceVnd).HasColumnName("unit_price_vnd").HasColumnType("bigint");
            b.HasOne(e => e.Order).WithMany(o => o.OrderSeats).HasForeignKey(e => new { e.OrderId, e.ShowtimeId }).HasPrincipalKey(o => new { o.OrderId, o.ShowtimeId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_OrderSeats_Orders_01");
            b.HasOne(e => e.ShowtimeSeat).WithMany(ss => ss.OrderSeats).HasForeignKey(e => new { e.ShowtimeId, e.SeatId }).HasPrincipalKey(ss => new { ss.ShowtimeId, ss.SeatId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_OrderSeats_ShowtimeSeats_02");
        });

        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("Payments", t =>
            {
                t.HasCheckConstraint("CK_Payments_01", @"(amount_vnd > 0)");
                t.HasCheckConstraint("CK_Payments_02", @"(status IN ('CREATED','PENDING','SUCCESS','FAILED','NEEDS_REVIEW'))");
            });
            b.HasKey(e => e.PaymentId).HasName("PK_Payments");
            b.HasAlternateKey(e => new { e.OrderId, e.AttemptKey }).HasName("UQ_Payments_01");
            b.Property(e => e.PaymentId).HasColumnName("payment_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.OrderId).HasColumnName("order_id").HasColumnType("bigint");
            b.Property(e => e.Provider).HasColumnName("provider").HasColumnType("varchar(40)").IsRequired();
            b.Property(e => e.AttemptKey).HasColumnName("attempt_key").HasColumnType("varchar(80)").IsRequired();
            b.Property(e => e.ProviderTransactionRef).HasColumnName("provider_transaction_ref").HasColumnType("varchar(120)");
            b.Property(e => e.AmountVnd).HasColumnName("amount_vnd").HasColumnType("bigint");
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(16)").IsRequired().HasDefaultValueSql(@"('CREATED')");
            b.Property(e => e.CreatedUtc).HasColumnName("created_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.Property(e => e.ResolvedUtc).HasColumnName("resolved_utc").HasColumnType("datetime2(3)");
            b.HasOne(e => e.Order).WithMany(o => o.Payments).HasForeignKey(e => e.OrderId).HasPrincipalKey(o => o.OrderId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Payments_Orders_01");
        });

        modelBuilder.Entity<PaymentEvent>(b =>
        {
            b.ToTable("PaymentEvents", t =>
            {
                t.HasCheckConstraint("CK_PaymentEvents_01", @"(auth_result IN ('VERIFIED','REJECTED'))");
                t.HasCheckConstraint("CK_PaymentEvents_02", @"(decision IN ('APPLIED','DUPLICATE','REJECTED','NEEDS_REVIEW','CONFLICT','FAILED_RECORDED'))");
            });
            b.HasKey(e => e.PaymentEventId).HasName("PK_PaymentEvents");
            b.Property(e => e.PaymentEventId).HasColumnName("payment_event_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.PaymentId).HasColumnName("payment_id").HasColumnType("bigint");
            b.Property(e => e.Provider).HasColumnName("provider").HasColumnType("varchar(40)").IsRequired();
            b.Property(e => e.ProviderEventRef).HasColumnName("provider_event_ref").HasColumnType("varchar(120)");
            b.Property(e => e.ProviderTransactionRef).HasColumnName("provider_transaction_ref").HasColumnType("varchar(120)");
            b.Property(e => e.AuthResult).HasColumnName("auth_result").HasColumnType("varchar(14)").IsRequired();
            b.Property(e => e.Decision).HasColumnName("decision").HasColumnType("varchar(24)").IsRequired();
            b.Property(e => e.PayloadSha256).HasColumnName("payload_sha256").HasColumnType("binary(32)");
            b.Property(e => e.ReceivedUtc).HasColumnName("received_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.HasOne(e => e.Payment).WithMany(p => p.PaymentEvents).HasForeignKey(e => e.PaymentId).HasPrincipalKey(p => p.PaymentId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_PaymentEvents_Payments_01");
        });

        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("Tickets", t =>
            {
                t.HasCheckConstraint("CK_Tickets_01", @"(status IN ('ISSUED','USED'))");
                t.HasCheckConstraint("CK_Tickets_02", @"(((status='ISSUED' AND used_utc IS NULL) OR (status='USED' AND used_utc IS NOT NULL)))");
            });
            b.HasKey(e => e.TicketId).HasName("PK_Tickets");
            b.HasAlternateKey(e => e.TicketCode).HasName("UQ_Tickets_01");
            b.HasAlternateKey(e => new { e.OrderId, e.SeatId }).HasName("UQ_Tickets_02");
            b.HasAlternateKey(e => new { e.ShowtimeId, e.SeatId }).HasName("UQ_Tickets_03");
            b.HasAlternateKey(e => e.QrTokenHash).HasName("UQ_Tickets_04");
            b.Property(e => e.TicketId).HasColumnName("ticket_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.TicketCode).HasColumnName("ticket_code").HasColumnType("varchar(50)").IsRequired();
            b.Property(e => e.OrderId).HasColumnName("order_id").HasColumnType("bigint");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint");
            b.Property(e => e.SeatId).HasColumnName("seat_id").HasColumnType("bigint");
            b.Property(e => e.Status).HasColumnName("status").HasColumnType("varchar(12)").IsRequired().HasDefaultValueSql(@"('ISSUED')");
            b.Property(e => e.QrTokenHash).HasColumnName("qr_token_hash").HasColumnType("binary(32)").IsRequired();
            b.Property(e => e.QrTokenCipher).HasColumnName("qr_token_cipher").HasColumnType("varbinary(max)").IsRequired();
            b.Property(e => e.QrKeyId).HasColumnName("qr_key_id").HasColumnType("varchar(32)").IsRequired();
            b.Property(e => e.IssuedUtc).HasColumnName("issued_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.Property(e => e.UsedUtc).HasColumnName("used_utc").HasColumnType("datetime2(3)");
            b.HasOne(e => e.OrderSeat).WithOne(os => os.Ticket).HasForeignKey<Ticket>(e => new { e.OrderId, e.ShowtimeId, e.SeatId }).HasPrincipalKey<OrderSeat>(os => new { os.OrderId, os.ShowtimeId, os.SeatId }).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_Tickets_OrderSeats_01");
        });

        modelBuilder.Entity<TicketScan>(b =>
        {
            b.ToTable("TicketScans", t =>
            {
                t.HasCheckConstraint("CK_TicketScans_01", @"(outcome IN ('ACCEPTED','ALREADY_USED','WRONG_SHOWTIME','OUTSIDE_WINDOW','REJECTED'))");
            });
            b.HasKey(e => e.ScanId).HasName("PK_TicketScans");
            b.Property(e => e.ScanId).HasColumnName("scan_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.TicketId).HasColumnName("ticket_id").HasColumnType("bigint");
            b.Property(e => e.StaffUserId).HasColumnName("staff_user_id").HasColumnType("bigint");
            b.Property(e => e.ScannedUtc).HasColumnName("scanned_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.Property(e => e.Outcome).HasColumnName("outcome").HasColumnType("varchar(24)").IsRequired();
            b.HasOne(e => e.Ticket).WithMany(t => t.TicketScans).HasForeignKey(e => e.TicketId).HasPrincipalKey(t => t.TicketId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_TicketScans_Tickets_01");
            b.HasOne(e => e.StaffUser).WithMany(u => u.ScansPerformed).HasForeignKey(e => e.StaffUserId).HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_TicketScans_Users_02");
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs", t =>
            {
                t.HasCheckConstraint("CK_AuditLogs_01", @"(details_json IS NULL OR ISJSON(details_json) = 1)");
            });
            b.HasKey(e => e.AuditId).HasName("PK_AuditLogs");
            b.Property(e => e.AuditId).HasColumnName("audit_id").HasColumnType("bigint").ValueGeneratedOnAdd().UseIdentityColumn();
            b.Property(e => e.ActorUserId).HasColumnName("actor_user_id").HasColumnType("bigint");
            b.Property(e => e.EventType).HasColumnName("event_type").HasColumnType("varchar(60)").IsRequired();
            b.Property(e => e.EntityType).HasColumnName("entity_type").HasColumnType("varchar(40)");
            b.Property(e => e.EntityKey).HasColumnName("entity_key").HasColumnType("nvarchar(120)");
            b.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasColumnType("uniqueidentifier").HasDefaultValueSql(@"(NEWID())");
            b.Property(e => e.DetailsJson).HasColumnName("details_json").HasColumnType("nvarchar(max)");
            b.Property(e => e.CreatedUtc).HasColumnName("created_utc").HasColumnType("datetime2(3)").HasDefaultValueSql(@"(SYSUTCDATETIME())");
            b.HasOne(e => e.ActorUser).WithMany(u => u.AuditLogs).HasForeignKey(e => e.ActorUserId).HasPrincipalKey(u => u.UserId).OnDelete(DeleteBehavior.NoAction).HasConstraintName("FK_AuditLogs_Users_01");
        });

        modelBuilder.Entity<ShowtimeSalesSummary>(b =>
        {
            b.HasNoKey();
            b.ToView("vw_ShowtimeSales", "dbo");
            b.Property(e => e.ShowtimeId).HasColumnName("showtime_id").HasColumnType("bigint");
            b.Property(e => e.MovieId).HasColumnName("movie_id").HasColumnType("bigint");
            b.Property(e => e.RoomId).HasColumnName("room_id").HasColumnType("bigint");
            b.Property(e => e.StartUtc).HasColumnName("start_utc").HasColumnType("datetime2(3)");
            b.Property(e => e.TicketsSold).HasColumnName("tickets_sold").HasColumnType("bigint");
            b.Property(e => e.RevenueVnd).HasColumnName("revenue_vnd").HasColumnType("decimal(38,0)");
            b.Property(e => e.SellableCapacity).HasColumnName("sellable_capacity").HasColumnType("int");
            b.Property(e => e.OccupancyPct).HasColumnName("occupancy_pct").HasColumnType("decimal(7,2)");
        });
        modelBuilder.Entity<Movie>(b =>
        {
            b.HasIndex(e => e.TmdbId).HasDatabaseName("UX_Movies_TmdbId").IsUnique().HasFilter(@"[tmdb_id] IS NOT NULL");
            b.HasIndex(e => e.ImdbId).HasDatabaseName("UX_Movies_ImdbId").IsUnique().HasFilter(@"[imdb_id] IS NOT NULL");
            b.HasIndex(e => e.WikidataQid).HasDatabaseName("UX_Movies_WikidataQid").IsUnique().HasFilter(@"[wikidata_qid] IS NOT NULL");
        });
        modelBuilder.Entity<Showtime>(b =>
        {
            b.HasIndex(e => new { e.RoomId, e.StartUtc }).HasDatabaseName("IX_Showtimes_Room_Start").IncludeProperties(e => new { e.EndUtc, e.Status, e.BufferMinutes });
            b.HasIndex(e => new { e.MovieId, e.StartUtc }).HasDatabaseName("IX_Showtimes_Movie_Start");
        });
        modelBuilder.Entity<BookingOrder>(b =>
        {
            b.HasIndex(e => new { e.UserId, e.CreatedUtc }).HasDatabaseName("IX_Orders_User_Created").IsDescending(false, true);
            b.HasIndex(e => new { e.Status, e.ExpiresUtc }).HasDatabaseName("IX_Orders_Status_Expiry");
            b.HasIndex(e => e.PaidUtc).HasDatabaseName("IX_Orders_Paid").HasFilter(@"[status] = 'PAID'").IncludeProperties(e => new { e.TotalVnd, e.Status });
        });
        modelBuilder.Entity<ShowtimeSeat>(b =>
        {
            b.HasIndex(e => new { e.OwnerOrderId, e.State }).HasDatabaseName("IX_ShowtimeSeats_Owner_State").HasFilter(@"[owner_order_id] IS NOT NULL");
            b.HasIndex(e => new { e.State, e.HoldExpiresUtc }).HasDatabaseName("IX_ShowtimeSeats_State_Expiry");
        });
        modelBuilder.Entity<Payment>(b =>
        {
            b.HasIndex(e => new { e.Provider, e.ProviderTransactionRef }).HasDatabaseName("UX_Payments_Provider_Ref").IsUnique().HasFilter(@"[provider_transaction_ref] IS NOT NULL");
            b.HasIndex(e => new { e.OrderId, e.CreatedUtc }).HasDatabaseName("IX_Payments_Order_Created").IsDescending(false, true);
        });
        modelBuilder.Entity<PaymentEvent>(b =>
        {
            b.HasIndex(e => new { e.Provider, e.ProviderEventRef }).HasDatabaseName("UX_PaymentEvents_Provider_EventRef").IsUnique().HasFilter(@"[provider_event_ref] IS NOT NULL");
            b.HasIndex(e => new { e.Provider, e.ProviderTransactionRef, e.ReceivedUtc }).HasDatabaseName("IX_PaymentEvents_Transaction");
        });
        modelBuilder.Entity<Ticket>(b =>
        {
            b.HasIndex(e => e.OrderId).HasDatabaseName("IX_Tickets_Order");
        });
        modelBuilder.Entity<TicketScan>(b =>
        {
            b.HasIndex(e => new { e.TicketId, e.ScannedUtc }).HasDatabaseName("IX_TicketScans_Ticket_UTC").IsDescending(false, true);
        });
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasIndex(e => e.CreatedUtc).HasDatabaseName("IX_AuditLogs_Created").IsDescending(true);
            b.HasIndex(e => new { e.ActorUserId, e.CreatedUtc }).HasDatabaseName("IX_AuditLogs_Actor_Created").IsDescending(false, true);
        });
    }
}
