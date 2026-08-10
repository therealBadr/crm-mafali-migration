using MafaliCrm.Web.Components;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// AddDbContextFactory, not AddDbContext: in Blazor Server a DI "scope" lasts
// the whole browser session (the circuit), not one request like a typical
// API — so a directly-injected scoped AppDbContext would hold one
// change tracker for the entire session, letting a failed SaveChanges on one
// operation corrupt an unrelated later one (confirmed by hitting exactly
// that: a blocked delete's leftover tracked state broke the next, valid
// delete). The factory gives each service a fresh context per operation
// instead.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<FamilleService>();
builder.Services.AddScoped<FranchiseService>();
builder.Services.AddScoped<PaysService>();
builder.Services.AddScoped<AssistanteService>();
builder.Services.AddScoped<ClientService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
