using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// Backs the new Rappels screen (roadmap Phase 4's "daily reminder view" —
// the only piece of that phase not already covered by the CA panel inside
// Parcours_Client). Deliberately not a faithful port of the legacy's
// REQ_RappelRDV: that query is an exact Date_Rappel = today match, with no
// rollup for a missed reminder — confirmed, from the functional-spec
// review, to be a real documented shortcoming, not a design worth copying.
// This uses <= today instead, so an unhandled reminder keeps surfacing
// until someone actually clears it (see below) rather than silently
// disappearing the day after it was due.
//
// "Handled" has no new column: RappelRdv already exists on FranceOptique
// and is already user-editable via ParcoursClientPage's own checkbox
// (wired up before this screen existed) — unchecking it there is what
// removes a client from this list, same single write path as every other
// FranceOptique field, not a second mechanism bolted on here.
//
// Deliberately no per-user (Assistante_Commerciale) filtering: real data
// checked before building this — only 3 distinct values exist across all
// of france_optique.assistante_commercial today, none of them clean
// exact matches for most real user logins (case variants like "Bruno" /
// "bruno bruno" / "Bruno Bruno"). Filtering to "my reminders" on data this
// sparse would show most users an empty list even when real reminders
// exist for them, which is worse than showing everyone the same shared
// list for now.
public class RappelService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public RappelService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<FranceOptique>> ListDueAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        return await db.FranceOptiques.AsNoTracking()
            .Where(c => c.RappelRdv && c.DateRappel != null && c.DateRappel <= today)
            .OrderBy(c => c.DateRappel)
            .ThenBy(c => c.HeureRappel)
            .ToListAsync();
    }

    // Backs the admin overview's "Rappels en attente" stat card — same
    // WHERE clause as ListDueAsync, but COUNT instead of fetching full rows.
    public async Task<int> CountDueAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        return await db.FranceOptiques
            .Where(c => c.RappelRdv && c.DateRappel != null && c.DateRappel <= today)
            .CountAsync();
    }
}
