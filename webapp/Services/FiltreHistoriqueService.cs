using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

public class FiltreHistoriqueInput
{
    public string NomOperateur { get; set; } = string.Empty;
    public string NomFiltre { get; set; } = string.Empty;
    public string FiltreReel { get; set; } = string.Empty;

    public static FiltreHistoriqueInput FromEntity(FiltreHistorique f) => new()
    {
        NomOperateur = f.NomOperateur,
        NomFiltre = f.NomFiltre,
        FiltreReel = f.FiltreReel ?? string.Empty,
    };
}

// Historique's half of Fen_Fichier_Filtre / Fen_Detail_Filtre — mirrors
// FiltreOperatriceService exactly (same composite key shape, same
// always-strict-insert Nouveau/Enregistrer semantics, same conflict-check
// update path), just against filtre_historique instead of
// filtre_operatrice. See that file's own comments for the reasoning behind
// each piece; not repeated here since it's identical.
public class FiltreHistoriqueService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly CurrentUserService _currentUser;

    public FiltreHistoriqueService(IDbContextFactory<AppDbContext> dbFactory, CurrentUserService currentUser)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
    }

    public async Task<List<FiltreHistorique>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FiltreHistoriques
            .OrderBy(f => f.NomOperateur).ThenBy(f => f.NomFiltre)
            .ToListAsync();
    }

    // Case/whitespace-insensitive match — see FiltreOperatriceService's own
    // ListByOperatorAsync for why a strict == isn't safe here.
    public async Task<List<FiltreHistorique>> ListByOperatorAsync(string nomOperateur)
    {
        var needle = nomOperateur.Trim().ToLower();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FiltreHistoriques
            .Where(f => f.NomOperateur.Trim().ToLower() == needle)
            .OrderBy(f => f.NomFiltre)
            .ToListAsync();
    }

    public async Task CreateAsync(string nomOperateur, string nomFiltre, string filtreReel)
    {
        if (!await _currentUser.IsAdminAsync())
        {
            throw new InvalidOperationException("Seuls les administrateurs peuvent enregistrer de nouveaux filtres.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.FiltreHistoriques.Add(new FiltreHistorique
        {
            NomOperateur = nomOperateur,
            NomFiltre = nomFiltre,
            FiltreReel = filtreReel,
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Un filtre avec ce nom existe déjà pour cet opérateur.");
        }
    }

    public async Task<FiltreHistorique?> GetAsync(string nomOperateur, string nomFiltre)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FiltreHistoriques.AsNoTracking()
            .FirstOrDefaultAsync(f => f.NomOperateur == nomOperateur && f.NomFiltre == nomFiltre);
    }

    private static readonly Dictionary<string, string> FiltreFieldLabels = new()
    {
        [nameof(FiltreHistoriqueInput.NomOperateur)] = "Assistante Commerciale",
        [nameof(FiltreHistoriqueInput.NomFiltre)] = "Nom du Filtre",
        [nameof(FiltreHistoriqueInput.FiltreReel)] = "Filtre Réel",
    };

    public async Task<ApplyResult> UpdateWithConflictCheckAsync(string originalNomOperateur, string originalNomFiltre, FiltreHistoriqueInput baseline, FiltreHistoriqueInput current, bool force = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fresh = await db.FiltreHistoriques.AsNoTracking()
            .FirstOrDefaultAsync(f => f.NomOperateur == originalNomOperateur && f.NomFiltre == originalNomFiltre)
            ?? throw new InvalidOperationException("Filtre introuvable.");
        var merged = FiltreHistoriqueInput.FromEntity(fresh);
        var conflicts = ConflictCheck.Apply(baseline, current, merged, force, FiltreFieldLabels);

        if (conflicts.Count > 0)
        {
            return new ApplyResult { Success = false, Conflicts = conflicts };
        }

        int affected;
        try
        {
            affected = await db.FiltreHistoriques
                .Where(f => f.NomOperateur == originalNomOperateur && f.NomFiltre == originalNomFiltre)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.NomOperateur, merged.NomOperateur)
                    .SetProperty(f => f.NomFiltre, merged.NomFiltre)
                    .SetProperty(f => f.FiltreReel, merged.FiltreReel));
        }
        catch (Exception ex) when (FriendlyError.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Un filtre avec ce nom existe déjà pour cet opérateur.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Filtre introuvable.");
        }

        return new ApplyResult { Success = true };
    }

    public async Task DeleteAsync(string nomOperateur, string nomFiltre)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FiltreHistoriques.FindAsync(nomOperateur, nomFiltre);
        if (entity is null) return;
        db.FiltreHistoriques.Remove(entity);
        await db.SaveChangesAsync();
    }
}
