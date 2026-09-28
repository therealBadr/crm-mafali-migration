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
// Per-user (Assistante_Commerciale) filtering, added 2026-09-02 once the
// real full data was migrated. Originally deliberately omitted — the old
// 489-row test subset only had 3 distinct assistant values, too sparse to
// filter meaningfully. The real data has 634 distinct values with real
// per-person volume (hundreds of overdue reminders each for the busiest
// assistants), so filtering is now genuinely useful, not just noise.
//
// Matched case-insensitively (ILIKE, no wildcards — exact match ignoring
// case) against the logged-in user's login, not a strict ==. Real data
// has casing/spacing inconsistencies for the same person (e.g. "Bruno" /
// "bruno bruno" / "Bruno Bruno"); this catches the pure-casing variants,
// though not spacing/word-count differences — a client whose assistant
// field was typed as a genuinely different string than the login won't
// match. Applies to every role, including admin — confirmed with Badr
// this is intentionally different from CanViewAllFiltersAsync's
// admin/assistant-see-all pattern used for saved filters; he wants his
// own reminders filtered too, not a shared everyone-sees-all list.
public class RappelService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public RappelService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<FranceOptique>> ListDueAsync(string login)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        return await db.FranceOptiques.AsNoTracking()
            .Where(c => c.RappelRdv && c.DateRappel != null && c.DateRappel <= today
                        && c.AssistanteCommercial != null && EF.Functions.ILike(c.AssistanteCommercial, login))
            .OrderBy(c => c.DateRappel)
            .ThenBy(c => c.HeureRappel)
            .ToListAsync();
    }

    // Backs the admin overview's "Rappels en attente" stat card — a
    // system-wide monitoring number for the admin-only "Aperçu" tab, not a
    // personal task list, so this one deliberately stays unfiltered
    // (login=null) even though ListDueAsync now filters per-user. Same
    // WHERE clause otherwise, COUNT instead of fetching full rows.
    public async Task<int> CountDueAsync(string? login = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var query = db.FranceOptiques
            .Where(c => c.RappelRdv && c.DateRappel != null && c.DateRappel <= today);
        if (login is not null)
        {
            query = query.Where(c => c.AssistanteCommercial != null && EF.Functions.ILike(c.AssistanteCommercial, login));
        }
        return await query.CountAsync();
    }
}
