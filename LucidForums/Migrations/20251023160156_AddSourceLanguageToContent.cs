using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LucidForums.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceLanguageToContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceLanguage",
                table: "Threads",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "SourceLanguage",
                table: "Messages",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<string>(
                name: "SourceLanguage",
                table: "Forums",
                type: "text",
                nullable: false,
                defaultValue: "en");

            migrationBuilder.CreateTable(
                name: "ContentTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TranslatedText = table.Column<string>(type: "text", nullable: false),
                    SourceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    AiModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentTranslations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TranslationStrings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultText = table.Column<string>(type: "text", nullable: false),
                    Context = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranslationStrings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Translations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TranslationStringId = table.Column<int>(type: "integer", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TranslatedText = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    AiModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Translations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Translations_TranslationStrings_TranslationStringId",
                        column: x => x.TranslationStringId,
                        principalTable: "TranslationStrings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Charters",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Behaviors", "Rules" },
                values: new object[] { new List<string> { "Assume good intent", "Use evidence and cite sources", "Welcome newcomers" }, new List<string> { "Be respectful", "No hate speech", "No spam or advertising" } });

            migrationBuilder.UpdateData(
                table: "Charters",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "Behaviors", "Rules" },
                values: new object[] { new List<string> { "Encourage collaboration", "Offer actionable suggestions", "Celebrate progress" }, new List<string> { "Show your work", "Be constructive in feedback", "Tag NSFW content appropriately" } });

            migrationBuilder.CreateIndex(
                name: "IX_ContentTranslations_ContentType_ContentId",
                table: "ContentTranslations",
                columns: new[] { "ContentType", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentTranslations_ContentType_ContentId_FieldName_Languag~",
                table: "ContentTranslations",
                columns: new[] { "ContentType", "ContentId", "FieldName", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Translations_TranslationStringId_LanguageCode",
                table: "Translations",
                columns: new[] { "TranslationStringId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranslationStrings_Category",
                table: "TranslationStrings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_TranslationStrings_Key",
                table: "TranslationStrings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentTranslations");

            migrationBuilder.DropTable(
                name: "Translations");

            migrationBuilder.DropTable(
                name: "TranslationStrings");

            migrationBuilder.DropColumn(
                name: "SourceLanguage",
                table: "Threads");

            migrationBuilder.DropColumn(
                name: "SourceLanguage",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SourceLanguage",
                table: "Forums");

            migrationBuilder.UpdateData(
                table: "Charters",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Behaviors", "Rules" },
                values: new object[] { new List<string> { "Assume good intent", "Use evidence and cite sources", "Welcome newcomers" }, new List<string> { "Be respectful", "No hate speech", "No spam or advertising" } });

            migrationBuilder.UpdateData(
                table: "Charters",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "Behaviors", "Rules" },
                values: new object[] { new List<string> { "Encourage collaboration", "Offer actionable suggestions", "Celebrate progress" }, new List<string> { "Show your work", "Be constructive in feedback", "Tag NSFW content appropriately" } });
        }
    }
}
