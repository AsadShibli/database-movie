using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReactionVideoAggregator.Migrations
{
    /// <inheritdoc />
    public partial class AddRssAndActiveColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Reactors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRssCheckAt",
                table: "Reactors",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Reactors");

            migrationBuilder.DropColumn(
                name: "LastRssCheckAt",
                table: "Reactors");
        }
    }
}
