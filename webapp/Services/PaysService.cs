using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

public class PaysInput
{
    public string NomPays { get; set; } = string.Empty;
    public string? Indicatif { get; set; }
    public string? Masque { get; set; }

    public static PaysInput FromEntity(Pays p) => new()
    {
        NomPays = p.NomPays,
        Indicatif = p.Indicatif,
        Masque = p.Masque,
    };
}

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

    private static readonly Dictionary<string, string> PaysFieldLabels = new()
    {
        [nameof(PaysInput.NomPays)] = "Nom Pays",
        [nameof(PaysInput.Indicatif)] = "Indicatif",
        [nameof(PaysInput.Masque)] = "Masque N° Téléphone",
    };

    // ExecuteUpdateAsync means there's no tracked entity to compare against
    // (that's the whole reason it's used — see the class comment), so the
    // freshness check needs its own explicit read first: fetch the real
    // current row, three-way merge against baseline/current, and only issue
    // the SQL UPDATE if nothing the caller touched was also changed by
    // someone else since baseline.
    public async Task<ApplyResult> UpdateWithConflictCheckAsync(long idPays, PaysInput baseline, PaysInput current, bool force = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fresh = await db.Pays.AsNoTracking().FirstOrDefaultAsync(p => p.IdPays == idPays)
            ?? throw new InvalidOperationException("Pays introuvable.");
        var merged = PaysInput.FromEntity(fresh);
        var conflicts = ConflictCheck.Apply(baseline, current, merged, force, PaysFieldLabels);

        if (conflicts.Count > 0)
        {
            return new ApplyResult { Success = false, Conflicts = conflicts };
        }

        int affected;
        try
        {
            affected = await db.Pays
                .Where(p => p.IdPays == idPays)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.NomPays, merged.NomPays)
                    .SetProperty(p => p.Indicatif, merged.Indicatif)
                    .SetProperty(p => p.Masque, merged.Masque));
        }
        catch (Exception ex) when (FriendlyError.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ce pays existe déjà.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Pays introuvable.");
        }

        return new ApplyResult { Success = true };
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
        catch (Exception ex) when (FriendlyError.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Ce pays existe déjà.");
        }
    }
}
