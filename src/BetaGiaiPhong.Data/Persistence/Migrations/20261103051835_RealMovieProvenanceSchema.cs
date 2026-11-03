using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BetaGiaiPhong.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RealMovieProvenanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "age_label_market",
                schema: "dbo",
                table: "Movies",
                type: "varchar(8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "age_label_source",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_attribution_text",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_license_text",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(300)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "artwork_source",
                schema: "dbo",
                table: "Movies",
                type: "varchar(32)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backdrop_url",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "backdrop_w780_url",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cast_json",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "director_names",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(300)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "external_expires_utc",
                schema: "dbo",
                table: "Movies",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "external_synced_utc",
                schema: "dbo",
                table: "Movies",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "imdb_id",
                schema: "dbo",
                table: "Movies",
                type: "varchar(16)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manual_override_mask",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metadata_source",
                schema: "dbo",
                table: "Movies",
                type: "varchar(32)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "official_trailer_youtube_id",
                schema: "dbo",
                table: "Movies",
                type: "varchar(32)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_country",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_language",
                schema: "dbo",
                table: "Movies",
                type: "varchar(16)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_title",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(250)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "poster_w342_url",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rating_fetched_utc",
                schema: "dbo",
                table: "Movies",
                type: "datetime2(0)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rating_scale_max",
                schema: "dbo",
                table: "Movies",
                type: "decimal(4,1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating_source",
                schema: "dbo",
                table: "Movies",
                type: "varchar(32)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "rating_value",
                schema: "dbo",
                table: "Movies",
                type: "decimal(3,1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating_vote_count",
                schema: "dbo",
                table: "Movies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "release_date_vn",
                schema: "dbo",
                table: "Movies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "release_year",
                schema: "dbo",
                table: "Movies",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title_logo_url",
                schema: "dbo",
                table: "Movies",
                type: "nvarchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tmdb_id",
                schema: "dbo",
                table: "Movies",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "wikidata_qid",
                schema: "dbo",
                table: "Movies",
                type: "varchar(16)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_Movies_ImdbId",
                schema: "dbo",
                table: "Movies",
                column: "imdb_id",
                unique: true,
                filter: "[imdb_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Movies_TmdbId",
                schema: "dbo",
                table: "Movies",
                column: "tmdb_id",
                unique: true,
                filter: "[tmdb_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Movies_WikidataQid",
                schema: "dbo",
                table: "Movies",
                column: "wikidata_qid",
                unique: true,
                filter: "[wikidata_qid] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_03",
                schema: "dbo",
                table: "Movies",
                sql: "(release_year IS NULL OR release_year BETWEEN 1888 AND 2100)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_04",
                schema: "dbo",
                table: "Movies",
                sql: "(artwork_source IS NULL OR artwork_source IN ('TMDB','WIKIMEDIA_COMMONS','DISTRIBUTOR','PENDING_TMDB_API_KEY','NONE'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_05",
                schema: "dbo",
                table: "Movies",
                sql: "(rating_source IS NULL OR rating_source IN ('IMDB','TMDB'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_06",
                schema: "dbo",
                table: "Movies",
                sql: "(rating_value IS NULL OR (rating_scale_max IS NOT NULL AND rating_scale_max > 0 AND rating_value >= 0 AND rating_value <= rating_scale_max AND rating_vote_count IS NOT NULL AND rating_vote_count >= 0))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_07",
                schema: "dbo",
                table: "Movies",
                sql: "(cast_json IS NULL OR ISJSON(cast_json) = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_08",
                schema: "dbo",
                table: "Movies",
                sql: "(age_label_market IS NULL OR age_label_market IN ('VN'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Movies_ImdbId",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "UX_Movies_TmdbId",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "UX_Movies_WikidataQid",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_03",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_04",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_05",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_06",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_07",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_08",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "age_label_market",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "age_label_source",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "artwork_attribution_text",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "artwork_license_text",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "artwork_source",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "backdrop_url",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "backdrop_w780_url",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "cast_json",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "director_names",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "external_expires_utc",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "external_synced_utc",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "imdb_id",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "manual_override_mask",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "metadata_source",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "official_trailer_youtube_id",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "origin_country",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "original_language",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "original_title",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "poster_w342_url",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "rating_fetched_utc",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "rating_scale_max",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "rating_source",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "rating_value",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "rating_vote_count",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "release_date_vn",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "release_year",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "title_logo_url",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "tmdb_id",
                schema: "dbo",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "wikidata_qid",
                schema: "dbo",
                table: "Movies");
        }
    }
}
