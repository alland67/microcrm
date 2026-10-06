using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MicroCrm.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CreateTodos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Todos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ContactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDone = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Todos", x => x.Id);
                    table.CheckConstraint("CK_Todos_DoneState", "(\"IsDone\" = 0 AND \"CompletedAt\" IS NULL) OR (\"IsDone\" = 1 AND \"CompletedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Todos_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Todos_ContactId",
                table: "Todos",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Todos_IsDone_DueDate",
                table: "Todos",
                columns: new[] { "IsDone", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Todos");
        }
    }
}
