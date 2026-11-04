using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CodebaseReviewTool.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlowSteps",
                columns: table => new
                {
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptText = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    TimeoutMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlowSteps", x => x.Step);
                });

            migrationBuilder.CreateTable(
                name: "PullRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    RepositoryKey = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", nullable: false),
                    SourceBranch = table.Column<string>(type: "TEXT", nullable: false),
                    TargetBranch = table.Column<string>(type: "TEXT", nullable: false),
                    SourceCommit = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsOpen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PullRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    RepositoryKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourceCommit = table.Column<string>(type: "TEXT", nullable: false),
                    MergeBaseCommit = table.Column<string>(type: "TEXT", nullable: false),
                    WorktreePath = table.Column<string>(type: "TEXT", nullable: false),
                    WorktreeRemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QueuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reviews_PullRequests_PullRequestId",
                        column: x => x.PullRequestId,
                        principalTable: "PullRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReviewId = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    File = table.Column<string>(type: "TEXT", nullable: false),
                    Line = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Suggestion = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issues_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewStepRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReviewId = table.Column<int>(type: "INTEGER", nullable: false),
                    Step = table.Column<int>(type: "INTEGER", nullable: false),
                    RenderedPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    CommandLine = table.Column<string>(type: "TEXT", nullable: false),
                    RawOutputJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultText = table.Column<string>(type: "TEXT", nullable: false),
                    StdErr = table.Column<string>(type: "TEXT", nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    AgentSessionId = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: true),
                    CostUsd = table.Column<double>(type: "REAL", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewStepRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewStepRuns_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ReviewId",
                table: "Issues",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_PullRequests_RepositoryKey",
                table: "PullRequests",
                column: "RepositoryKey");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_PullRequestId",
                table: "Reviews",
                column: "PullRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewStepRuns_ReviewId",
                table: "ReviewStepRuns",
                column: "ReviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlowSteps");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "ReviewStepRuns");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "PullRequests");
        }
    }
}
