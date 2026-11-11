using Infrastructure.Data;
using WebApp.Blazor.Components;
using WebApp.Blazor.Endpoints;
using WebApp.Blazor.Extensions;

DotEnvLoader.LoadIfPresent();

var builder = WebApplication.CreateBuilder(args);

var connectionString = DotEnvLoader.ResolveValue("DB_CONNECTION_STRING")
    ?? builder.Configuration["DB_CONNECTION_STRING"];

if (string.IsNullOrWhiteSpace(connectionString) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "A variável de ambiente DB_CONNECTION_STRING é obrigatória fora do ambiente de desenvolvimento.");
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddInfrastructureData(connectionString);
builder.Services.AddApplicationServices();
builder.Services.AddBlazorIdentityServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapAccountEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
