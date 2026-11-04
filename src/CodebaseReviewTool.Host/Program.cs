using CodebaseReviewTool.Core;
using CodebaseReviewTool.Host.Api;
using CodebaseReviewTool.Host.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReviewCore(builder.Configuration);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
