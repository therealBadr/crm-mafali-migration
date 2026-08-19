using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

public enum LoginResult
{
    Success,
    InvalidCredentials,
    Inactive,
}

public enum ChangePasswordResult
{
    Success,
    WrongCurrentPassword,
}

public class UserService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPasswordHasher<User> _hasher;

    public UserService(IDbContextFactory<AppDbContext> dbFactory, IPasswordHasher<User> hasher)
    {
        _dbFactory = dbFactory;
        _hasher = hasher;
    }

    // `timestamp without time zone` columns reject a Kind=Utc DateTime —
    // confirmed by hitting exactly that Npgsql error during the login build.
    // The value is still UTC, just stored the way the column expects.
    private static DateTime NowForDb() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    // Seeded users (see database/05_users_and_roles.sql) have an empty
    // password_hash placeholder — nobody has a real password yet. Treat that
    // exactly like the legacy GPWLogin's MotPasseASaisir flow: first login
    // for that account sets the password instead of checking one.
    public async Task<(LoginResult Result, User? User)> LoginAsync(string login, string password)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Login == login);
        if (user is null) return (LoginResult.InvalidCredentials, null);
        if (!user.IsActive) return (LoginResult.Inactive, null);

        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
            user.MustChangePassword = false;
            user.LastLoginAt = NowForDb();
            db.LoginHistories.Add(new LoginHistory { Login = user.Login, LoggedInAt = NowForDb() });
            await db.SaveChangesAsync();
            return (LoginResult.Success, user);
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed) return (LoginResult.InvalidCredentials, null);

        user.LastLoginAt = NowForDb();
        db.LoginHistories.Add(new LoginHistory { Login = user.Login, LoggedInAt = NowForDb() });
        await db.SaveChangesAsync();
        return (LoginResult.Success, user);
    }

    public async Task<List<User>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.OrderBy(u => u.Login).ToListAsync();
    }

    // Duplicate login should error, not silently overwrite — same reasoning
    // as FiltreOperatriceService's own create path.
    public async Task CreateAsync(string login, string role)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Users.Add(new User
        {
            Login = login,
            Role = role,
            PasswordHash = "",
            MustChangePassword = true,
            IsActive = true,
            CreatedAt = NowForDb(),
        });
        await db.SaveChangesAsync();
    }

    public async Task UpdateRoleAsync(string login, string role)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Users.Where(u => u.Login == login).ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, role));
    }

    public async Task SetActiveAsync(string login, bool isActive)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Users.Where(u => u.Login == login).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, isActive));
    }

    // Clears the password rather than setting one Claude would then know —
    // same first-login-sets-it flow as a brand new account (see LoginAsync).
    public async Task ForcePasswordResetAsync(string login)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Users.Where(u => u.Login == login).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.PasswordHash, "")
            .SetProperty(u => u.MustChangePassword, true));
    }

    public async Task<ChangePasswordResult> ChangeOwnPasswordAsync(string login, string currentPassword, string newPassword)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(u => u.Login == login);

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword);
        if (verify == PasswordVerificationResult.Failed) return ChangePasswordResult.WrongCurrentPassword;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        await db.SaveChangesAsync();
        return ChangePasswordResult.Success;
    }

    public async Task<List<LoginHistory>> GetLoginHistoryAsync(int limit = 200)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.LoginHistories
            .OrderByDescending(h => h.LoggedInAt)
            .Take(limit)
            .ToListAsync();
    }

    // assistantes and users are two separate tables linked only by matching
    // name string, not a real foreign key (assistantes predates auth
    // entirely) — nothing stops them drifting apart. Surfaced as a warning
    // on the Utilisateurs page rather than built out as its own screen.
    public async Task<(List<string> AssistantesWithoutLogin, List<string> LoginsWithoutAssistanteProfile)> GetIdentityDriftAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var assistanteNames = await db.Assistantes.Select(a => a.PrenomNom).ToListAsync();
        var loginNames = await db.Users.Select(u => u.Login).ToListAsync();

        var withoutLogin = assistanteNames.Except(loginNames).OrderBy(n => n).ToList();
        var withoutProfile = loginNames.Except(assistanteNames).OrderBy(n => n).ToList();
        return (withoutLogin, withoutProfile);
    }
}
