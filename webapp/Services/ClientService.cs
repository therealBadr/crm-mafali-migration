using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MafaliCrm.Web.Services;

// The old REST backend queried a narrow 31-column projection (GRID_SELECT)
// specifically to avoid shipping unused columns over HTTP as JSON. That
// reason doesn't exist here: Blazor Server never puts this data on the
// wire as JSON at all — the FranceOptique objects live entirely on the
// server, only rendered HTML diffs cross the SignalR connection. So the
// list query below just reads the full entity; the .razor markup decides
// what to actually display, same separation of "fetch" vs "render" you'd
// have in any server-side app, just without a payload-shaping reason to
// narrow the query itself.
public class ClientInput
{
    public string RaisonSociale { get; set; } = string.Empty;
    public string? Complement { get; set; }
    public string? Rue { get; set; }
    public string? Localisation1 { get; set; }
    public string? Localisation2 { get; set; }
    public string? Cp { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; }
    public string? Telephone { get; set; }
    public string? Portable { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? ResponsableAchat { get; set; }
    public string? Representant { get; set; }
}

// Uses IDbContextFactory instead of an injected AppDbContext — confirmed by
// hitting the bug directly: a failed SaveChanges (FK-blocked delete) leaves
// its entity stuck in the tracker as "Deleted", and since Blazor Server's DI
// scope lasts the whole browser session (not one request), that leftover
// state silently broke the *next*, unrelated, otherwise-valid delete in the
// same session. A fresh context per operation means nothing carries over.
public class ClientService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClientService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // AsNoTracking(): we're not going to mutate these rows in place (edits
    // happen on a separate form page, which loads its own tracked copy), so
    // there's no reason to pay for EF Core's change-tracking bookkeeping on
    // ~489 rows just to read them.
    public async Task<List<FranceOptique>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques
            .AsNoTracking()
            .OrderBy(c => c.RaisonSociale)
            .ToListAsync();
    }

    public async Task<FranceOptique?> GetAsync(long cleOpl)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CleOpl == cleOpl);
    }

    // cle_opl is ValueGeneratedNever() — unlike an identity column, EF Core
    // won't assign it for us. Same as the old backend's raw
    // `SELECT nextval(...)`, just via EF Core's typed raw-SQL scalar query
    // instead of Prisma's $queryRaw.
    public async Task<FranceOptique> CreateAsync(ClientInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var cleOpl = (await db.Database
            .SqlQuery<long>($"SELECT nextval('france_optique_cle_opl_seq')")
            .ToListAsync())[0];

        var entity = new FranceOptique { CleOpl = cleOpl };
        ApplyInput(entity, input);
        db.FranceOptiques.Add(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (TryGetForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }

        return entity;
    }

    public async Task UpdateAsync(long cleOpl, ClientInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");
        ApplyInput(entity, input);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (TryGetForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }
    }

    // fk_historique_num_client / fk_ca_cle_opl are ON DELETE RESTRICT —
    // unlike the lookup tables, a client with interaction history or
    // revenue rows can't be deleted, matching the old backend's behavior
    // (and unlike the legacy WinDev app itself, which let this through
    // with no protection at all).
    public async Task DeleteAsync(long cleOpl)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl);
        if (entity is null) return;
        db.FranceOptiques.Remove(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (TryGetForeignKeyViolation(ex, out _))
        {
            throw new InvalidOperationException(
                "Ce client a des interactions ou du chiffre d'affaires enregistrés et ne peut pas être supprimé.");
        }
    }

    private static void ApplyInput(FranceOptique entity, ClientInput input)
    {
        entity.RaisonSociale = input.RaisonSociale;
        entity.Complement = input.Complement;
        entity.Rue = input.Rue;
        entity.Localisation1 = input.Localisation1;
        entity.Localisation2 = input.Localisation2;
        entity.Cp = input.Cp;
        entity.Ville = input.Ville;
        entity.Pays = input.Pays;
        entity.Telephone = input.Telephone;
        entity.Portable = input.Portable;
        entity.Fax = input.Fax;
        entity.Email = input.Email;
        entity.ResponsableAchat = input.ResponsableAchat;
        entity.Representant = input.Representant;
    }

    // Npgsql's PostgresException exposes ConstraintName directly — more
    // precise than the old backend's string-matching on Prisma's nested
    // error shape, but the same underlying job: turn "23503" into a
    // message naming which field actually failed.
    private static bool TryGetForeignKeyViolation(Exception ex, out string? constraintName)
    {
        var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
        if (pgEx is { SqlState: "23503" })
        {
            constraintName = pgEx.ConstraintName;
            return true;
        }
        constraintName = null;
        return false;
    }

    private static string FriendlyForeignKeyMessage(string? constraintName) =>
        constraintName?.Contains("pays") == true
            ? "Pays doit correspondre à un pays existant."
            : "Cette valeur doit correspondre à une référence existante.";
}
