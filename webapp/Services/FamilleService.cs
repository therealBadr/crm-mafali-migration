using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MafaliCrm.Web.Services;

public class FamilleService
{
    private readonly AppDbContext _db;

    public FamilleService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<string>> ListAsync()
    {
        return await _db.TypeFamilles
            .OrderBy(f => f.NomFamille)
            .Select(f => f.NomFamille)
            .ToListAsync();
    }

    public async Task CreateAsync(string nomFamille)
    {
        _db.TypeFamilles.Add(new TypeFamille { NomFamille = nomFamille });
        await SaveOrThrowFriendly();
    }

    // fk_france_optique_famille is ON UPDATE CASCADE, so renaming here
    // (changing the primary key's value) automatically updates every client
    // row that references it — same guarantee the old Prisma-based
    // familles.ts PATCH relied on, same underlying SQL mechanism (a plain
    // UPDATE on the key column that Postgres cascades for us).
    //
    // NOTE: this can't be "load the entity, mutate NomFamille, SaveChanges" —
    // confirmed by actually hitting it — because EF Core's change tracker
    // never allows mutating a primary key property that way, full stop.
    // (Originally assumed this was specifically about TypeFamille's tracked
    // FranceOptiques relationship — later disproven by hitting the exact
    // same error on Assistante, which has zero relationships. The rule is
    // simpler and unconditional: key properties, period.)
    // ExecuteUpdateAsync issues a direct SQL UPDATE instead, bypassing the
    // change tracker (and its relationship-consistency concerns) entirely.
    public async Task RenameAsync(string currentName, string newName)
    {
        int affected;
        try
        {
            affected = await _db.TypeFamilles
                .Where(f => f.NomFamille == currentName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.NomFamille, newName));
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette famille existe déjà.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Famille introuvable.");
        }
    }

    // fk_france_optique_famille is ON DELETE SET NULL, not RESTRICT — unlike
    // deleting a client, deleting a lookup value here just clears the field
    // on any client that referenced it, no blocking, no extra handling.
    public async Task DeleteAsync(string nomFamille)
    {
        var entity = await _db.TypeFamilles.FindAsync(nomFamille);
        if (entity is null) return;
        _db.TypeFamilles.Remove(entity);
        await _db.SaveChangesAsync();
    }

    private async Task SaveOrThrowFriendly()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette famille existe déjà.");
        }
    }

    // SaveChangesAsync wraps the driver error as DbUpdateException.InnerException;
    // ExecuteUpdateAsync executes outside that wrapping and can surface the
    // PostgresException directly. Checking both shapes covers either path.
    private static bool IsUniqueViolation(Exception ex) =>
        ex is PostgresException { SqlState: "23505" } ||
        ex.InnerException is PostgresException { SqlState: "23505" };
}
