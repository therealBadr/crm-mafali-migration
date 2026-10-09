using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// Replaces ClientProposalService (deleted) after the 2026-10-05 design
// change: a Commercial's add is a real add, not a proposal. The row goes
// straight into france_optique and is immediately usable; Admin/Assistant
// validate it afterward as a quality check. See
// database/19_france_optique_validation.sql for the column design and why
// 'refused' is a state rather than a delete.
//
// There is no copy step anymore — validating a client doesn't move data
// between tables, it just records who checked it. That removed the entire
// reason the old design needed two parallel column lists kept in sync.
public class ClientValidationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClientValidationService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // `timestamp without time zone` columns reject a Kind=Utc DateTime —
    // same constraint UserService.NowForDb documents after hitting exactly
    // that Npgsql error during the login build.
    private static DateTime NowForDb() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

    // The validation queue. addedBy filters to one Commercial's additions
    // (the per-Commercial view from the confirmed decision); null means all
    // of them. Oldest first — this is a worklist, so the longest-waiting
    // client should be the first one an Assistant sees.
    public async Task<List<FranceOptique>> ListPendingAsync(string? addedBy = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.FranceOptiques.AsNoTracking()
            .Where(c => c.ValidationStatus == "pending");

        if (!string.IsNullOrWhiteSpace(addedBy))
            query = query.Where(c => c.AddedBy == addedBy);

        return await query
            .OrderBy(c => c.DateSaisie)
            .ThenBy(c => c.HeureSaisie)
            .ToListAsync();
    }

    // Powers the "filter by Commercial" dropdown on the queue. Returns only
    // people who actually have something pending, with counts, so the
    // dropdown never offers a choice that leads to an empty list.
    public async Task<List<(string AddedBy, int Count)>> ListPendingAddersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.FranceOptiques.AsNoTracking()
            .Where(c => c.ValidationStatus == "pending" && c.AddedBy != null)
            .GroupBy(c => c.AddedBy!)
            .Select(g => new { AddedBy = g.Key, Count = g.Count() })
            .OrderBy(g => g.AddedBy)
            .ToListAsync();

        return rows.Select(r => (r.AddedBy, r.Count)).ToList();
    }

    // A Commercial's own additions, every status including refused — this is
    // the one place a refused client stays visible, because its whole purpose
    // is telling the person who added it what happened to it.
    public async Task<List<FranceOptique>> ListByAdderAsync(string addedBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.AddedBy == addedBy)
            .OrderByDescending(c => c.DateSaisie)
            .ThenByDescending(c => c.HeureSaisie)
            .ToListAsync();
    }

    // The validation screen has to render an already-refused client to show
    // what happened to it — ClientService.GetAsync would return null for one,
    // since the global query filter hides refused rows from every normal read.
    public async Task<FranceOptique?> GetIncludingRefusedAsync(long cleOpl)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CleOpl == cleOpl);
    }

    public async Task<int> CountPendingAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.CountAsync(c => c.ValidationStatus == "pending");
    }

    // edited is whatever the reviewer's form currently holds — unchanged if
    // they just clicked Valider, corrected if they fixed something first.
    // One codepath covers both, same as the retired approve flow did.
    public async Task ValidateAsync(long cleOpl, ClientInput edited, string validatedBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");
        if (entity.ValidationStatus != "pending")
            throw new InvalidOperationException("Ce client a déjà été traité.");

        ClientService.ApplyInputTo(entity, edited);
        entity.ValidationStatus = "validated";
        entity.ValidatedBy = validatedBy;
        entity.ValidatedAt = NowForDb();

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out _))
        {
            throw new InvalidOperationException("Cette valeur doit correspondre à une référence existante.");
        }
    }

    // Not a delete. The client already exists and may already have a logged
    // call or CA against it, and historique/ca are ON DELETE RESTRICT, so a
    // delete would simply fail in the normal case — see the migration header.
    // This hides it from the app's screens while keeping who/when/why.
    public async Task RefuseAsync(long cleOpl, string validatedBy, string reason)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");
        if (entity.ValidationStatus != "pending")
            throw new InvalidOperationException("Ce client a déjà été traité.");

        entity.ValidationStatus = "refused";
        entity.ValidatedBy = validatedBy;
        entity.ValidatedAt = NowForDb();
        entity.RefusalReason = reason;
        await db.SaveChangesAsync();
    }
}
