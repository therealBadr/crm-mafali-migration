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
public class PaysService
{
    private readonly AppDbContext _db;

    public PaysService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Pays>> ListAsync()
    {
        return await _db.Pays
            .OrderBy(p => p.NomPays)
            .ToListAsync();
    }

    public async Task CreateAsync(string nomPays, string? indicatif, string? masque)
    {
        _db.Pays.Add(new Pays { NomPays = nomPays, Indicatif = indicatif, Masque = masque });
        await SaveOrThrowFriendly();
    }

    public async Task UpdateAsync(long idPays, string nomPays, string? indicatif, string? masque)
    {
        int affected;
        try
        {
            affected = await _db.Pays
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
        var entity = await _db.Pays.FindAsync(idPays);
        if (entity is null) return;
        _db.Pays.Remove(entity);
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
            throw new InvalidOperationException("Ce pays existe déjà.");
        }
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is PostgresException { SqlState: "23505" } ||
        ex.InnerException is PostgresException { SqlState: "23505" };
}
