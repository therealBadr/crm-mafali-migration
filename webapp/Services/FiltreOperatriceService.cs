using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

public class FiltreOperatriceInput
{
    public string NomOperateur { get; set; } = string.Empty;
    public string NomFiltre { get; set; } = string.Empty;
    public string FiltreReel { get; set; } = string.Empty;

    public static FiltreOperatriceInput FromEntity(FiltreOperatrice f) => new()
    {
        NomOperateur = f.NomOperateur,
        NomFiltre = f.NomFiltre,
        FiltreReel = f.FiltreReel ?? string.Empty,
    };
}

// Backs both Fen_Fichier_Filtre (list/Nouveau/Modifier/Supprimer) and
// Fen_Detail_Filtre's "Enregistrer le Filtre en Cours" flow from
// Fen_Definir_Filtre. FiltreReel is genuine free text here, exactly like the
// legacy — confirmed by the real migrated data (hand-typed condition
// strings with inconsistent spacing/casing between rows), and per Badr's
// explicit direction to keep it that way rather than a structured format.
public class FiltreOperatriceService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly CurrentUserService _currentUser;

    public FiltreOperatriceService(IDbContextFactory<AppDbContext> dbFactory, CurrentUserService currentUser)
    {
        _dbFactory = dbFactory;
        _currentUser = currentUser;
    }

    public async Task<List<FiltreOperatrice>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FiltreOperatrices
            .OrderBy(f => f.NomOperateur).ThenBy(f => f.NomFiltre)
            .ToListAsync();
    }

    // Backs Fen_Filtre's "Filtre :" combo — legacy scopes that combo to the
    // logged-in operator's own filters only (Req_Filtre_Operatrice). We have
    // no auth yet, so the page asks the user to pick who they are first
    // (Badr's explicit stand-in until real auth/permissions land) and scopes
    // from there.
    //
    // Case/whitespace-insensitive match, not a strict ==: nom_operateur has
    // no FK to users.login or assistantes.prenom_nom (free text, same as
    // FiltreReel above) — confirmed live, 2026-09-22: a Commercial account
    // ("Bruno") could see his own filters fine on one machine but not
    // another, same code and same role on both, only explained by the two
    // databases' nom_operateur values having drifted apart in case/spacing
    // over time (hand-entered, nothing ever enforced them matching). A
    // strict == is exactly the kind of check that silently breaks on data
    // like that; trimming and lower-casing both sides survives it.
    public async Task<List<FiltreOperatrice>> ListByOperatorAsync(string nomOperateur)
    {
        var needle = nomOperateur.Trim().ToLower();
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FiltreOperatrices
            .Where(f => f.NomOperateur.Trim().ToLower() == needle)
            .OrderBy(f => f.NomFiltre)
            .ToListAsync();
    }

    // Matches the legacy's real behavior: Btn_Enreg_Filtre calls HRAZ()
    // before opening Fen_Detail_Filtre, and Fen_Fichier_Filtre's Nouveau
    // does the same — both mean NouvelEnregistrement is always True, so
    // Valider always does HAjoute (a strict insert), never HModifie. A
    // duplicate (nom_operateur, nom_filtre) should error, not silently
    // overwrite.
    //
    // Admin-only, enforced here rather than only at the "Enregistrer le
    // Filtre en Cours" / "Nouveau" buttons that call it (both of which
    // funnel through this one method via FiltreDetailModal, its only
    // caller) — hiding a button controls what a user sees, not what the
    // method underneath will actually do for anyone who reaches it by any
    // other path. Définir un Filtre itself has no page-level [Authorize
    // (Roles=...)] (unlike Utilisateurs/HistoriqueConnexions) — every
    // logged-in role lands on that exact page daily to search, so the
    // "the page itself keeps non-admins out" argument that holds for
    // those two doesn't apply here.
    public async Task CreateAsync(string nomOperateur, string nomFiltre, string filtreReel)
    {
        if (!await _currentUser.IsAdminAsync())
        {
            throw new InvalidOperationException("Seuls les administrateurs peuvent enregistrer de nouveaux filtres.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        db.FiltreOperatrices.Add(new FiltreOperatrice
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

    public async Task<FiltreOperatrice?> GetAsync(string nomOperateur, string nomFiltre)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FiltreOperatrices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.NomOperateur == nomOperateur && f.NomFiltre == nomFiltre);
    }

    private static readonly Dictionary<string, string> FiltreFieldLabels = new()
    {
        [nameof(FiltreOperatriceInput.NomOperateur)] = "Assistante Commerciale",
        [nameof(FiltreOperatriceInput.NomFiltre)] = "Nom du Filtre",
        [nameof(FiltreOperatriceInput.FiltreReel)] = "Filtre Réel",
    };

    // Both halves of the composite key are editable in Fen_Detail_Filtre, so
    // this uses ExecuteUpdateAsync like every other rename in this app —
    // EF Core never allows mutating key properties via a tracked entity.
    // No auth yet means every operator can see and edit every filter (see
    // ListAsync's own comment), which makes a genuine concurrent edit here
    // more plausible than most of the other screens in this app.
    public async Task<ApplyResult> UpdateWithConflictCheckAsync(string originalNomOperateur, string originalNomFiltre, FiltreOperatriceInput baseline, FiltreOperatriceInput current, bool force = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var fresh = await db.FiltreOperatrices.AsNoTracking()
            .FirstOrDefaultAsync(f => f.NomOperateur == originalNomOperateur && f.NomFiltre == originalNomFiltre)
            ?? throw new InvalidOperationException("Filtre introuvable.");
        var merged = FiltreOperatriceInput.FromEntity(fresh);
        var conflicts = ConflictCheck.Apply(baseline, current, merged, force, FiltreFieldLabels);

        if (conflicts.Count > 0)
        {
            return new ApplyResult { Success = false, Conflicts = conflicts };
        }

        int affected;
        try
        {
            affected = await db.FiltreOperatrices
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
        var entity = await db.FiltreOperatrices.FindAsync(nomOperateur, nomFiltre);
        if (entity is null) return;
        db.FiltreOperatrices.Remove(entity);
        await db.SaveChangesAsync();
    }
}
