using System.Security.Claims;
using MafaliCrm.Web.Components;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using MafaliCrm.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Some tooling harnesses (browser preview tools, PaaS-style hosts) assign a
// port via a plain PORT env var rather than the .NET-native conventions
// (ASPNETCORE_URLS/ASPNETCORE_HTTP_PORTS) — bind to it here if present, so
// this app runs under that convention too without per-tool Kestrel config.
// Falls through to normal launch-profile/appsettings behavior when unset.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
// Real LAN deployment (the self-contained Windows build, launched by
// double-clicking the .exe — no launch profile, no PORT env var, and
// ASPNETCORE_ENVIRONMENT defaults to "Production" when nothing else sets
// it). Bind to every network interface, not just localhost, so other
// machines on the office network can reach it at
// http://<this machine's IP>:5298 — otherwise Kestrel's own default
// (localhost-only) would make it unreachable from anywhere but the one
// machine running it. Plain HTTP, deliberately: a real TLS cert isn't
// practical for an internal LAN app (a self-signed one means trusting it
// by hand on every machine), and UseHttpsRedirection() below is a no-op
// here anyway with no HTTPS endpoint configured — confirmed live, it just
// logs a "can't determine HTTPS port" warning and passes the request
// through, not a redirect loop. Port 5298 kept fixed across builds
// (previously set externally in an appsettings.Production.json that
// isn't tracked in git — moved here so it can't quietly go missing from
// a future deployment package) so the Windows Firewall rule and
// everyone's bookmarked URL don't need to change build to build.
else if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://0.0.0.0:5298");
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Without this, ASP.NET Core generates a fresh in-memory DataProtection
// key on every process start — which silently invalidates every existing
// auth cookie (the cookie's encryption key is gone), logging everyone out
// on every restart even though nothing about their session actually
// expired. Confirmed as the exact cause of a real disruption during Bon
// de Commande testing (2026-08-26, see PROGRESS.md) — every `dotnet run`
// restart to pick up a code change forced a fresh login. Same AppData
// folder convention as BonCommandeTemplateService (outside wwwroot,
// gitignored via the existing `webapp/AppData/` entry).
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "AppData", "DataProtectionKeys")))
    .SetApplicationName("MafaliCrm");

// Cookie auth, not the legacy's plaintext-password GPW component (see
// database/05_users_and_roles.sql and PROGRESS.md for the full reasoning).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        // Was "/login" (same as LoginPath) — meant a logged-in user with
        // the wrong role for a page landed back on the login form, which
        // reads as "you're not logged in" rather than "you don't have
        // access". Real page at /access-denied (AccessDenied.razor) now
        // gives that case its own clear message.
        options.AccessDeniedPath = "/access-denied";
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
builder.Services.AddScoped<ClientValidationService>();
builder.Services.AddScoped<ClientFilterService>();
builder.Services.AddScoped<FiltreOperatriceService>();
builder.Services.AddScoped<HistoriqueService>();
builder.Services.AddScoped<HistoriqueFilterService>();
builder.Services.AddScoped<FiltreHistoriqueService>();
builder.Services.AddScoped<CaService>();
builder.Services.AddScoped<ClientImportExportService>();
builder.Services.AddScoped<RappelService>();
builder.Services.AddScoped<BonCommandeTemplateService>();
builder.Services.AddScoped<BonCommandeEnCoursService>();

// Rappel pop-ups, step 2 (Badr, 2026-10-08). The pairing here is the whole
// point, so the two lifetimes are deliberate rather than incidental:
//
//   * the registry is a Singleton — one shared table of who is connected,
//     reachable both from a user's circuit and from the background scanner
//     thread that has no circuit at all;
//   * the handler is Scoped — the framework creates one per circuit and
//     calls its lifecycle methods, which is how each circuit reports itself
//     into that shared table.
//
// Registered against the CircuitHandler base type, not the concrete class:
// that's the type Blazor resolves when it looks for circuit observers, and
// registering the concrete type alone would silently never be called.
builder.Services.AddSingleton<ConnectedUserRegistry>();
builder.Services.AddScoped<CircuitHandler, ConnectedUserCircuitHandler>();

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
        var reason = result switch
        {
            LoginResult.Inactive => "inactive",
            LoginResult.LockedOut => "locked",
            _ => "invalid",
        };
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
