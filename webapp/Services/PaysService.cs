using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MafaliCrm.Web.Services;

// Unlike Familles/Franchises, Pays has a real surrogate primary key
// (IdPays, identity-generated) — NomPays/Indicatif/Masque are just regular
// editable fields, not the key itself. But NomPays is configured as an
// alternate key (HasPrincipalKey, what fk_france_optique_pays actually
// points to), and EF Core's "no mutating key properties via a tracked
// entity" restriction is unconditional for any key — primary or alternate,
// relationships or not (confirmed the "relationships" part was never the
// real trigger — see AssistanteService for how that got disproven).
// ExecuteUpdateAsync is used for the whole update, not just NomPays, to
// keep the write path uniform rather than conditional.
//
// Also uses IDbContextFactory, not an injected AppDbContext — see
// ClientService for why: a scoped AppDbContext lives for the whole Blazor
// Server session, so stale tracked state from a failed operation can
// corrupt a later unrelated one. A fresh context per method call avoids it.
public class PaysService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PaysService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Pays>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Pays
            .OrderBy(p => p.NomPays)
            .ToListAsync();
    }

    public async Task CreateAsync(string nomPays, string? indicatif, string? masque)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Pays.Add(new Pays { NomPays = nomPays, Indicatif = indicatif, Masque = masque });
        await SaveOrThrowFriendly(db);
    }

    public async Task UpdateAsync(long idPays, string nomPays, string? indicatif, string? masque)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        int affected;
        try
        {
            affected = await db.Pays
                .Where(p => p.IdPays == idPays)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.NomPays, nomPays)
                    .SetProperty(p => p.Indicatif, indicatif)
                    .SetProperty(p => p.Masque, masque));
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ce pays existe déjà.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Pays introuvable.");
        }
    }

    public async Task DeleteAsync(long idPays)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Pays.FindAsync(idPays);
        if (entity is null) return;
        db.Pays.Remove(entity);
        await db.SaveChangesAsync();
    }

    private static async Task SaveOrThrowFriendly(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ce pays existe déjà.");
        }
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is PostgresException { SqlState: "23505" } ||
        ex.InnerException is PostgresException { SqlState: "23505" };
}
