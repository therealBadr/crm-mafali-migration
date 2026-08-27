using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// Every field from Fen_Detail_Historique except Num_Client — that one is
// only ever set on create (HistoriqueService.CreateAsync's own parameter),
// never on update. Confirmed with Badr: the legacy form lets Num_Client be
// freely re-typed on an existing row (re-pointing history to a different
// client with zero validation, exactly Risk Register #13's complaint) — we
// lock it instead, deliberately not a faithful replication of that part.
public class HistoriqueInput
{
    public DateOnly? DateSaisie { get; set; }
    public TimeOnly? HeureSaisie { get; set; }
    public string? Operation { get; set; }
    public string? StatusVente { get; set; }
    public string? Note { get; set; }
    public DateOnly? DateRappel { get; set; }
    public TimeOnly? HeureRappel { get; set; }
    public string? Franchise { get; set; }
    public string? RaisonSociale { get; set; }
    public string? Cp { get; set; }
    public string? Ville { get; set; }
    public string? AssistanteCommercial { get; set; }
    public string? MagasinPrincipal { get; set; }
    public string? StatutsClients { get; set; }
    public byte[]? FicStk { get; set; }
    public string? FicStkNom { get; set; }

    public static HistoriqueInput FromEntity(Historique h) => new()
    {
        DateSaisie = h.DateSaisie,
        HeureSaisie = h.HeureSaisie,
        Operation = h.Operation,
        StatusVente = h.StatusVente,
        Note = h.Note,
        DateRappel = h.DateRappel,
        HeureRappel = h.HeureRappel,
        Franchise = h.Franchise,
        RaisonSociale = h.RaisonSociale,
        Cp = h.Cp,
        Ville = h.Ville,
        AssistanteCommercial = h.AssistanteCommercial,
        MagasinPrincipal = h.MagasinPrincipal,
        StatutsClients = h.StatutsClients,
        FicStk = h.FicStk,
        FicStkNom = h.FicStkNom,
    };
}

// Backs both Table_Histo (the read-only master-detail panel on Fen_Filtre)
// and Fen_Fichier_Historique / Fen_Detail_Historique (full CRUD, this
// session's build). Soft delete, not the legacy's hard delete — deliberate
// architecture change per Risk Register #13, confirmed with Badr.
//
// Performance: the real Historique data (still blocked on a password for
// the actual export) is 1M+ rows, not the ~2,100 currently migrated. Every
// read here is built to never load more than one page's worth of rows into
// memory — GetPagedAsync backs Fichier_Historique's <Virtualize
// ItemsProvider> with real keyset (seek) pagination, not an in-memory
// .Skip().Take() over a fully-loaded list (see GetPagedAsync's own comment
// for why OFFSET/LIMIT specifically doesn't work at this scale).
// GetCountAsync is a real SQL COUNT(*), same reasoning.
public class HistoriqueService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HistoriqueService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // Most-recent-first — the legacy doc doesn't specify Req_Histo's sort
    // order, so this is an inferred default (most useful for "what's the
    // latest on this client" at a glance), not a documented behavior.
    public async Task<List<Historique>> GetForClientAsync(long numClient)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Historiques.AsNoTracking()
            .Where(h => h.NumClient == numClient && h.DeletedAt == null)
            .OrderByDescending(h => h.DateSaisie)
            .ThenByDescending(h => h.HeureSaisie)
            .Select(WithoutFicStk)
            .ToListAsync();
    }

    // Keyset (seek) pagination, not OFFSET/LIMIT — measured directly against
    // a 1M-row synthetic table before shipping this: OFFSET 500,000 took
    // ~870ms because Postgres has to walk and discard every row before the
    // offset, even with an index on the sort column (confirmed via EXPLAIN
    // ANALYZE — "Index Scan Backward ... actual rows=500050" before the
    // Limit trims it down). Cost scales with how deep you page, which fails
    // outright at the real ~1M-row target. Seeking "WHERE id_histo <= @cursor"
    // instead jumps straight there via the primary key index — cost stays
    // flat regardless of depth. `fromId: null` fetches the first page, same
    // as before; the caller (FichierHistoriquePage) keeps a small cache of
    // "startIndex -> cursor" anchors seen so far, converting Virtualize's
    // index-based requests into cursor seeks. `fromId` is inclusive — it's
    // the id_histo of whatever row should come first in the returned page.
    public async Task<List<Historique>> GetPagedAsync(long? fromId, int count)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Historiques.AsNoTracking().Where(h => h.DeletedAt == null);
        if (fromId is not null)
        {
            query = query.Where(h => h.IdHisto <= fromId.Value);
        }
        return await query
            .OrderByDescending(h => h.IdHisto)
            .Take(count)
            .Select(WithoutFicStk)
            .ToListAsync();
    }

    // Grid views never display the attachment itself, only whether one
    // exists (via FicStkNom) — excluding the actual bytea here keeps a
    // page of history rows cheap even once real attachments (up to 20 MB
    // each, see ParcoursClientPage's MaxAttachmentFileSize) start
    // accumulating. GetByIdAsync deliberately does NOT use this — it's
    // the one path that needs the real bytes, for download.
    private static readonly System.Linq.Expressions.Expression<Func<Historique, Historique>> WithoutFicStk = h => new Historique
    {
        IdHisto = h.IdHisto,
        NumClient = h.NumClient,
        DateSaisie = h.DateSaisie,
        HeureSaisie = h.HeureSaisie,
        AssistanteCommercial = h.AssistanteCommercial,
        DateRappel = h.DateRappel,
        HeureRappel = h.HeureRappel,
        Operation = h.Operation,
        StatusVente = h.StatusVente,
        Note = h.Note,
        Franchise = h.Franchise,
        RaisonSociale = h.RaisonSociale,
        Cp = h.Cp,
        Ville = h.Ville,
        StatutsClients = h.StatutsClients,
        MagasinPrincipal = h.MagasinPrincipal,
        FicStkNom = h.FicStkNom,
        DeletedAt = h.DeletedAt,
    };

    public async Task<int> GetCountAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Historiques.AsNoTracking().CountAsync(h => h.DeletedAt == null);
    }

    public async Task<Historique?> GetByIdAsync(long idHisto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Historiques.AsNoTracking()
            .FirstOrDefaultAsync(h => h.IdHisto == idHisto && h.DeletedAt == null);
    }

    public async Task<long> CreateAsync(long numClient, HistoriqueInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = new Historique { NumClient = numClient };
        ApplyInput(entity, input);
        db.Historiques.Add(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out _))
        {
            throw new InvalidOperationException("Ce numéro de client n'existe pas.");
        }

        return entity.IdHisto;
    }

    public async Task UpdateAsync(long idHisto, HistoriqueInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Historiques.FirstOrDefaultAsync(h => h.IdHisto == idHisto && h.DeletedAt == null)
            ?? throw new InvalidOperationException("Historique introuvable.");
        ApplyInput(entity, input);
        await db.SaveChangesAsync();
    }

    private static readonly Dictionary<string, string> HistoriqueFieldLabels = new()
    {
        [nameof(HistoriqueInput.DateSaisie)] = "Date de Saisie",
        [nameof(HistoriqueInput.HeureSaisie)] = "Heure de Saisie",
        [nameof(HistoriqueInput.Operation)] = "Opération",
        [nameof(HistoriqueInput.StatusVente)] = "Status Vente",
        [nameof(HistoriqueInput.Note)] = "Notes",
        [nameof(HistoriqueInput.DateRappel)] = "Date de Rappel",
        [nameof(HistoriqueInput.HeureRappel)] = "Heure de Rappel",
        [nameof(HistoriqueInput.Franchise)] = "Franchise",
        [nameof(HistoriqueInput.RaisonSociale)] = "Raison Sociale",
        [nameof(HistoriqueInput.Cp)] = "Code Postal",
        [nameof(HistoriqueInput.Ville)] = "Ville",
        [nameof(HistoriqueInput.AssistanteCommercial)] = "Assistante Commerciale",
        [nameof(HistoriqueInput.MagasinPrincipal)] = "Magasin",
        [nameof(HistoriqueInput.StatutsClients)] = "Statut Client",
    };

    public async Task<ApplyResult> UpdateWithConflictCheckAsync(long idHisto, HistoriqueInput baseline, HistoriqueInput current, bool force = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Historiques.FirstOrDefaultAsync(h => h.IdHisto == idHisto && h.DeletedAt == null)
            ?? throw new InvalidOperationException("Historique introuvable.");
        var merged = HistoriqueInput.FromEntity(entity);
        var conflicts = ConflictCheck.Apply(baseline, current, merged, force, HistoriqueFieldLabels);

        if (conflicts.Count > 0)
        {
            return new ApplyResult { Success = false, Conflicts = conflicts };
        }

        ApplyInput(entity, merged);
        await db.SaveChangesAsync();
        return new ApplyResult { Success = true };
    }

    public async Task DeleteAsync(long idHisto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Historiques.FirstOrDefaultAsync(h => h.IdHisto == idHisto && h.DeletedAt == null);
        if (entity is null) return;
        entity.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static void ApplyInput(Historique entity, HistoriqueInput input)
    {
        entity.DateSaisie = input.DateSaisie;
        entity.HeureSaisie = input.HeureSaisie;
        entity.Operation = input.Operation;
        entity.StatusVente = input.StatusVente;
        entity.Note = input.Note;
        entity.DateRappel = input.DateRappel;
        entity.HeureRappel = input.HeureRappel;
        entity.Franchise = input.Franchise;
        entity.RaisonSociale = input.RaisonSociale;
        entity.Cp = input.Cp;
        entity.Ville = input.Ville;
        entity.AssistanteCommercial = input.AssistanteCommercial;
        entity.MagasinPrincipal = input.MagasinPrincipal;
        entity.StatutsClients = input.StatutsClients;
        entity.FicStk = input.FicStk;
        entity.FicStkNom = input.FicStkNom;
    }
}
