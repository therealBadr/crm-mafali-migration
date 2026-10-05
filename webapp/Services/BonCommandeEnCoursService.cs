using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// Backs the dashboard's "Bon de commande en cours" section and its own
// "Voir tout" page — same shape as RappelService (per-user, ILIKE match
// against the logged-in Assistante_Commerciale, admin-only unfiltered
// count for the Aperçu stat tiles). "En cours" is the exact StatusVente
// value the Status de la Vente dropdown itself labels "Bon de commande"
// (ParcoursClientPage.razor) — not every non-terminal status in that
// dropdown, just this one step of the pipeline, confirmed with Badr.
public class BonCommandeEnCoursService
{
    public const string StatusValue = "Vente (Bon de Commande)";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public BonCommandeEnCoursService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<FranceOptique>> ListAsync(string login)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.AsNoTracking()
            .Where(c => c.StatusVente == StatusValue
                        && c.AssistanteCommercial != null && EF.Functions.ILike(c.AssistanteCommercial, login))
            .OrderBy(c => c.DateSaisie)
            .ThenBy(c => c.HeureSaisie)
            .ToListAsync();
    }

    public async Task<int> CountAsync(string? login = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.FranceOptiques.Where(c => c.StatusVente == StatusValue);
        if (login is not null)
        {
            query = query.Where(c => c.AssistanteCommercial != null && EF.Functions.ILike(c.AssistanteCommercial, login));
        }
        return await query.CountAsync();
    }
}
