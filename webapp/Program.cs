using System.Security.Claims;
using MafaliCrm.Web.Components;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using MafaliCrm.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cookie auth, not the legacy's plaintext-password GPW component (see
// database/05_users_and_roles.sql and PROGRESS.md for the full reasoning).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });

// FallbackPolicy requires every endpoint to be authenticated by default,
// rather than adding [Authorize] to every existing page individually —
// Login.razor's own [AllowAnonymous] (and the /login POST endpoint's
// .AllowAnonymous() below) are the explicit, deliberate exceptions.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<CurrentUserService>();

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
builder.Services.AddScoped<ClientFilterService>();
builder.Services.AddScoped<FiltreOperatriceService>();
builder.Services.AddScoped<HistoriqueService>();
builder.Services.AddScoped<CaService>();
builder.Services.AddScoped<ClientImportExportService>();
builder.Services.AddScoped<RappelService>();

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

// Must come after UseAntiforgery/before MapRazorComponents, per the standard
// ASP.NET Core Core pipeline ordering: authentication establishes who the
// request is from, authorization (added later, once real pages start using
// [Authorize]) decides what they're allowed to do.
app.UseAuthentication();
app.UseAuthorization();

// Plain minimal API endpoints, not Blazor components, because they need to
// call HttpContext.SignInAsync/SignOutAsync — which needs a real, writable
// HTTP response. Once the Blazor Server SignalR circuit takes over (which
// happens as soon as a page goes interactive), that response is long gone,
// so this can't happen from inside a normal Blazor event handler. Login.razor
// submits to this with a genuine, non-Blazor <form method="post"> instead.
app.MapPost("/login", async (
    [FromForm] string login,
    [FromForm] string password,
    [FromForm] string? returnUrl,
    HttpContext http,
    UserService userService) =>
{
    var (result, user) = await userService.LoginAsync(login, password);
    if (result != LoginResult.Success || user is null)
    {
        var reason = result == LoginResult.Inactive ? "inactive" : "invalid";
        return Results.Redirect($"/login?error={reason}");
    }

    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, user.Login), new Claim(ClaimTypes.Role, user.Role)],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

    return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
})
.DisableAntiforgery() // see Login.razor's comment — antiforgery tokens are bound to
                       // the identity active when generated, which breaks revisiting
                       // /login while already logged in.
.AllowAnonymous(); // must be reachable by someone who isn't logged in yet — that's
                   // the whole point of this endpoint.

app.MapPost("/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
