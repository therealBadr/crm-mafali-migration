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
// Matched case-insensitively AND ignoring leading/trailing whitespace,
// against the logged-in user's login — lower(btrim(...)) on both sides,
// the same idiom FiltreOperatriceService already uses for Nom_Operateur.
// Real data has both kinds of inconsistency for the same person: "Bruno",
// " Bruno" with a leading space, "bruno bruno", "Bruno Bruno".
//
// The trim is new as of 2026-10-08 (Badr's call while building the rappel
// pop-up). This was ILIKE with no trim before, which silently cost login
// "Bruno" the 11 reminders whose assistant field had been typed " Bruno".
// Applied here, not only in the pop-up path, deliberately: a pop-up for a
// reminder that doesn't appear on this very list would be impossible to
// explain to the person seeing it.
//
// Still NOT matched, by design: word-count differences. "Bruno Bruno"
// (3 rows) does not reach login "Bruno", and an assistant field holding a
// genuinely different string than any login reaches nobody at all — 647 of
// 2410 RappelRdv rows as of 2026-10-08. Checked against the real data: all
// but a handful are former staff with nothing written since 2019-2024, but
// "..." (30 rappels, 4058 clients, still being written to) is live junk
// rather than a person. Applies to every role, including admin — confirmed with Badr
// this is intentionally different from CanViewAllFiltersAsync's
// admin/assistant-see-all pattern used for saved filters; he wants his
// own reminders filtered too, not a shared everyone-sees-all list.
// A rappel whose stored Date_Rappel + Heure_Rappel has been resolved to the
// single absolute instant it names, via the client's own Pays timezone.
// Moment is carried alongside the client so the caller doesn't have to redo
// the conversion to know when to fire, or to sort by it.
public record DueRappel(FranceOptique Client, DateTimeOffset Moment);

public class RappelService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public RappelService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // The one definition of the key two sides are compared on when deciding
    // whether a rappel belongs to a user. Used for the needle in both
    // queries below, and by the pop-up scanner for its in-memory matching,
    // so the Rappels screen and the pop-up can't quietly drift apart.
    //
    // Only ever applied to the login side here: the column side has to be
    // written inline as c.AssistanteCommercial.Trim().ToLower() because EF
    // translates that into SQL, and a call to this method inside an
    // expression tree could not be translated at all.
    public static string OwnerKey(string value) => value.Trim().ToLower();

    public async Task<List<FranceOptique>> ListDueAsync(string login)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var needle = OwnerKey(login);
        return await db.FranceOptiques.AsNoTracking()
            .Where(c => c.RappelRdv && c.DateRappel != null && c.DateRappel <= today
                        && c.AssistanteCommercial != null && c.AssistanteCommercial.Trim().ToLower() == needle)
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
            var needle = OwnerKey(login);
            query = query.Where(c => c.AssistanteCommercial != null && c.AssistanteCommercial.Trim().ToLower() == needle);
        }
        return await query.CountAsync();
    }

    // Rappels whose exact moment falls inside the pop-up warning window
    // right now — backs the "pop-up 2 minutes before a rappel RDV" feature
    // (Badr, 2026-10-08). Separate from ListDueAsync above rather than a
    // variant of it: that one is the pull-based Rappels screen, a date-only
    // "<= today" list of everything outstanding, whereas this is a
    // to-the-minute check of what is about to happen. They answer different
    // questions and deliberately don't share a WHERE clause.
    //
    // 'now' is a parameter, not DateTime.Now read inside: it keeps the whole
    // thing a pure function of its inputs, so the window logic can be
    // checked by passing a chosen instant instead of waiting for a real
    // reminder to come due.
    //
    // Not filtered by login, unlike the two methods above. The scanner
    // (step 3) matches owners itself against the set of users actually
    // connected at that moment, so one query per tick covers everybody
    // rather than one query per connected user. Ownership stays the same
    // Assistante_Commercial-vs-login rule ListDueAsync uses, applied by the
    // caller.
    public async Task<List<DueRappel>> ListDueSoonAsync(DateTimeOffset now, int leadMinutes = RappelDueTime.DefaultLeadMinutes)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var (from, to) = RappelDueTime.CandidateDateRange(now);

        // Heure_Rappel null is skipped: with no time there is no moment to be
        // 2 minutes before. Such a rappel still shows on the Rappels screen
        // (ListDueAsync doesn't require a time), it just can't be a pop-up.
        //
        // The zone comes through a projection rather than Include so the
        // query reads one string per row instead of materialising a whole
        // Pays entity; PaysNavigation is a left join, so a client with no
        // Pays — or one naming a pays row that doesn't exist — arrives here
        // as a null Zone and takes RappelDueTime's Maroc fallback.
        var candidates = await db.FranceOptiques.AsNoTracking()
            .Where(c => c.RappelRdv
                        && c.DateRappel != null && c.DateRappel >= from && c.DateRappel <= to
                        && c.HeureRappel != null)
            .Select(c => new { Client = c, Zone = c.PaysNavigation!.FuseauHoraire })
            .ToListAsync();

        // The window test runs in C#, not SQL, on purpose. Postgres could do
        // the conversion itself with AT TIME ZONE, but it would use its own
        // copy of the timezone database rather than the host's ICU data that
        // TimeInput already converts through — two sources that can disagree
        // at a DST boundary. Doing it here means a pop-up fires for exactly
        // the instant the Parcours Client screen shows for that rappel. The
        // candidate set is a day either side of now, so this is tens of rows.
        var due = new List<DueRappel>();
        foreach (var row in candidates)
        {
            var moment = RappelDueTime.ToInstant(row.Client.DateRappel!.Value, row.Client.HeureRappel!.Value, row.Zone);
            if (RappelDueTime.IsDueSoon(moment, now, leadMinutes))
            {
                due.Add(new DueRappel(row.Client, moment));
            }
        }

        return due.OrderBy(d => d.Moment).ToList();
    }
}
