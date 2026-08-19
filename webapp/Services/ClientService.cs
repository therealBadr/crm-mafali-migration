using System.Reflection;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// The old REST backend queried a narrow 31-column projection (GRID_SELECT)
// specifically to avoid shipping unused columns over HTTP as JSON. That
// reason doesn't exist here: Blazor Server never puts this data on the
// wire as JSON at all — the FranceOptique objects live entirely on the
// server, only rendered HTML diffs cross the SignalR connection. So the
// list query below just reads the full entity; the .razor markup decides
// what to actually display, same separation of "fetch" vs "render" you'd
// have in any server-side app, just without a payload-shaping reason to
// narrow the query itself.
public class ClientInput
{
    public string RaisonSociale { get; set; } = string.Empty;
    public string? Complement { get; set; }
    public string? Rue { get; set; }
    public string? Localisation1 { get; set; }
    public string? Localisation2 { get; set; }
    public string? Cp { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; }
    public string? Telephone { get; set; }
    public string? Portable { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? ResponsableAchat { get; set; }
    public string? Representant { get; set; }

    // Same read-modify-write starting point ParcoursClientInput.FromEntity
    // gives Fen_Parcours_Client/Fen_Recherche_Client — used by ClientForm's
    // own edit mode (baseline + conflict check) below.
    public static ClientInput FromEntity(FranceOptique c) => new()
    {
        RaisonSociale = c.RaisonSociale ?? string.Empty,
        Complement = c.Complement,
        Rue = c.Rue,
        Localisation1 = c.Localisation1,
        Localisation2 = c.Localisation2,
        Cp = c.Cp,
        Ville = c.Ville,
        Pays = c.Pays,
        Telephone = c.Telephone,
        Portable = c.Portable,
        Fax = c.Fax,
        Email = c.Email,
        ResponsableAchat = c.ResponsableAchat,
        Representant = c.Representant,
    };
}

// Fen_Parcours_Client's own field scope — much wider than Detail_Client's
// ClientInput above (family, franchises, operation/status, reminders,
// bloqué...). Kept as a separate type rather than widening ClientInput so
// ClientForm's existing ApplyInput can't accidentally null out fields it
// was never built to know about.
public class ParcoursClientInput
{
    public string? Famille { get; set; }
    public string? RaisonSociale { get; set; }
    public string? Complement { get; set; }
    public string? Franchise { get; set; }
    public string? Franchise2 { get; set; }
    public string? Franchise3 { get; set; }
    public string? Franchise4 { get; set; }
    public string? Rue { get; set; }
    public string? Localisation1 { get; set; }
    public string? Localisation2 { get; set; }
    public string? Cp { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; }
    public string? Telephone { get; set; }
    public string? TelBis { get; set; }
    public string? Portable { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? AssistanteCommercial { get; set; }
    public string? ResponsableAchat { get; set; }
    public string? OpEnCours { get; set; }
    public string? StatusVente { get; set; }
    public string? StatutsClients { get; set; }
    public string? EtatClient { get; set; }
    public string? Production { get; set; }
    public string? MagasinPrincipal { get; set; }
    public DateOnly? DateSaisie { get; set; }
    public TimeOnly? HeureSaisie { get; set; }
    public DateOnly? DateRappel { get; set; }
    public TimeOnly? HeureRappel { get; set; }
    public string? Note { get; set; }
    public bool Bloque { get; set; }
    public bool RappelRdv { get; set; }
    public string? Siret { get; set; }
    public string? Siren { get; set; }
    public bool FacturationElectronique { get; set; }

    // Shared by Fen_Parcours_Client (writes every field) and
    // Fen_Recherche_Client's "Enregistrer Modification" (loads via this,
    // overlays just the fields present on its own narrower form, writes
    // back the merged result) — both need a read-modify-write starting
    // point, not just Parcours_Client.
    public static ParcoursClientInput FromEntity(FranceOptique c) => new()
    {
        Famille = c.Famille,
        RaisonSociale = c.RaisonSociale,
        Complement = c.Complement,
        Franchise = c.Franchise,
        Franchise2 = c.Franchise2,
        Franchise3 = c.Franchise3,
        Franchise4 = c.Franchise4,
        Rue = c.Rue,
        Localisation1 = c.Localisation1,
        Localisation2 = c.Localisation2,
        Cp = c.Cp,
        Ville = c.Ville,
        Pays = c.Pays,
        Telephone = c.Telephone,
        TelBis = c.TelBis,
        Portable = c.Portable,
        Fax = c.Fax,
        Email = c.Email,
        AssistanteCommercial = c.AssistanteCommercial,
        ResponsableAchat = c.ResponsableAchat,
        OpEnCours = c.OpEnCours,
        StatusVente = c.StatusVente,
        StatutsClients = c.StatutsClients,
        EtatClient = c.EtatClient,
        Production = c.Production,
        MagasinPrincipal = c.MagasinPrincipal,
        DateSaisie = c.DateSaisie,
        HeureSaisie = c.HeureSaisie,
        DateRappel = c.DateRappel,
        HeureRappel = c.HeureRappel,
        Note = c.Note,
        Bloque = c.Bloque,
        RappelRdv = c.RappelRdv,
        Siret = c.Siret,
        Siren = c.Siren,
        FacturationElectronique = c.FacturationElectronique,
    };
}

// Uses IDbContextFactory instead of an injected AppDbContext — confirmed by
// hitting the bug directly: a failed SaveChanges (FK-blocked delete) leaves
// its entity stuck in the tracker as "Deleted", and since Blazor Server's DI
// scope lasts the whole browser session (not one request), that leftover
// state silently broke the *next*, unrelated, otherwise-valid delete in the
// same session. A fresh context per operation means nothing carries over.
public class ClientService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClientService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // AsNoTracking(): we're not going to mutate these rows in place (edits
    // happen on a separate form page, which loads its own tracked copy), so
    // there's no reason to pay for EF Core's change-tracking bookkeeping on
    // ~489 rows just to read them.
    public async Task<List<FranceOptique>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques
            .AsNoTracking()
            .OrderBy(c => c.RaisonSociale)
            .ToListAsync();
    }

    public async Task<FranceOptique?> GetAsync(long cleOpl)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CleOpl == cleOpl);
    }

    // Backs the admin overview's "Clients" stat card — a real COUNT query,
    // not ListAsync().Count, so it doesn't pull all ~489 rows just to read
    // a number.
    public async Task<int> CountAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.CountAsync();
    }

    // Backs Fen_Recherche_Client's "Ouvrir le Phoning with every field
    // blank" case — a cheap, single-row lookup (ORDER BY the primary key,
    // not the full ListAsync()) rather than fetching all 487 rows just to
    // read the first one.
    public async Task<long?> GetFirstCleOplAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.FranceOptiques.AsNoTracking()
            .OrderBy(c => c.CleOpl)
            .Select(c => (long?)c.CleOpl)
            .FirstOrDefaultAsync();
    }

    // cle_opl is ValueGeneratedNever() — unlike an identity column, EF Core
    // won't assign it for us. Same as the old backend's raw
    // `SELECT nextval(...)`, just via EF Core's typed raw-SQL scalar query
    // instead of Prisma's $queryRaw.
    public async Task<FranceOptique> CreateAsync(ClientInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var cleOpl = (await db.Database
            .SqlQuery<long>($"SELECT nextval('france_optique_cle_opl_seq')")
            .ToListAsync())[0];

        var entity = new FranceOptique { CleOpl = cleOpl };
        ApplyInput(entity, input);
        db.FranceOptiques.Add(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }

        return entity;
    }

    public async Task UpdateAsync(long cleOpl, ClientInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");
        ApplyInput(entity, input);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }
    }

    private static readonly Dictionary<string, string> ClientFieldLabels = new()
    {
        [nameof(ClientInput.RaisonSociale)] = "Raison Sociale",
        [nameof(ClientInput.Complement)] = "Complément",
        [nameof(ClientInput.Rue)] = "Rue",
        [nameof(ClientInput.Localisation1)] = "Localisation 1",
        [nameof(ClientInput.Localisation2)] = "Localisation 2",
        [nameof(ClientInput.Cp)] = "Code Postal",
        [nameof(ClientInput.Ville)] = "Ville",
        [nameof(ClientInput.Pays)] = "Pays",
        [nameof(ClientInput.Telephone)] = "Téléphone",
        [nameof(ClientInput.Portable)] = "Portable",
        [nameof(ClientInput.Fax)] = "Fax",
        [nameof(ClientInput.Email)] = "Email",
        [nameof(ClientInput.ResponsableAchat)] = "Responsable Achat",
        [nameof(ClientInput.Representant)] = "Représentant",
    };

    // ClientForm's edit-mode save path — same three-way merge as
    // Fen_Parcours_Client's UpdateFullWithConflictCheckAsync below, scaled
    // down to ClientInput's narrower field set. No DateSaisie/HeureSaisie
    // here (ClientInput has no auto-stamped audit fields), so unlike that
    // method there's nothing to exclude from the conflict check.
    public async Task<ApplyResult> UpdateWithConflictCheckAsync(long cleOpl, ClientInput baseline, ClientInput current, bool force = false)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");
        var merged = ClientInput.FromEntity(entity);
        var conflicts = ConflictCheck.Apply(baseline, current, merged, force, ClientFieldLabels);

        if (conflicts.Count > 0)
        {
            return new ApplyResult { Success = false, Conflicts = conflicts };
        }

        ApplyInput(entity, merged);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }

        return new ApplyResult { Success = true };
    }

    // French labels for FieldConflict display — keyed with nameof() so a
    // property rename breaks the build instead of silently losing its label.
    private static readonly Dictionary<string, string> ParcoursFieldLabels = new()
    {
        [nameof(ParcoursClientInput.Famille)] = "Famille",
        [nameof(ParcoursClientInput.RaisonSociale)] = "Raison Sociale",
        [nameof(ParcoursClientInput.Complement)] = "Complément",
        [nameof(ParcoursClientInput.Franchise)] = "Franchise",
        [nameof(ParcoursClientInput.Franchise2)] = "Franchise 2",
        [nameof(ParcoursClientInput.Franchise3)] = "Franchise 3",
        [nameof(ParcoursClientInput.Franchise4)] = "Franchise 4",
        [nameof(ParcoursClientInput.Rue)] = "Rue",
        [nameof(ParcoursClientInput.Localisation1)] = "Localisation 1",
        [nameof(ParcoursClientInput.Localisation2)] = "Localisation 2",
        [nameof(ParcoursClientInput.Cp)] = "Code Postal",
        [nameof(ParcoursClientInput.Ville)] = "Ville",
        [nameof(ParcoursClientInput.Pays)] = "Pays",
        [nameof(ParcoursClientInput.Telephone)] = "Téléphone",
        [nameof(ParcoursClientInput.TelBis)] = "Téléphone Bis",
        [nameof(ParcoursClientInput.Portable)] = "Portable",
        [nameof(ParcoursClientInput.Fax)] = "Fax",
        [nameof(ParcoursClientInput.Email)] = "Email",
        [nameof(ParcoursClientInput.AssistanteCommercial)] = "Assistante Commerciale",
        [nameof(ParcoursClientInput.ResponsableAchat)] = "Responsable Achat",
        [nameof(ParcoursClientInput.OpEnCours)] = "Opération en Cours",
        [nameof(ParcoursClientInput.StatusVente)] = "Status de la Vente",
        [nameof(ParcoursClientInput.StatutsClients)] = "Statuts Clients",
        [nameof(ParcoursClientInput.EtatClient)] = "Etat Client",
        [nameof(ParcoursClientInput.Production)] = "Production",
        [nameof(ParcoursClientInput.MagasinPrincipal)] = "Magasin Principal",
        [nameof(ParcoursClientInput.DateRappel)] = "Date Rappel",
        [nameof(ParcoursClientInput.HeureRappel)] = "Heure Rappel",
        [nameof(ParcoursClientInput.Note)] = "Note",
        [nameof(ParcoursClientInput.Bloque)] = "Bloqué",
        [nameof(ParcoursClientInput.RappelRdv)] = "Rappel RDV",
        [nameof(ParcoursClientInput.Siret)] = "SIRET",
        [nameof(ParcoursClientInput.Siren)] = "SIREN",
        [nameof(ParcoursClientInput.FacturationElectronique)] = "Facturation Électronique",
    };

    // DateSaisie/HeureSaisie are stamped to "now" by the caller on every
    // single Appliquer — an audit timestamp, not user-entered data — so
    // they'd look "touched" on every call and never meaningfully conflict.
    // Excluded from conflict detection; they still always save the
    // caller's value, same as every other touched field.
    private static readonly HashSet<string> ExcludedFromConflictCheck = new()
    {
        nameof(ParcoursClientInput.DateSaisie),
        nameof(ParcoursClientInput.HeureSaisie),
    };

    // Fen_Parcours_Client's save path. Three-way merge, not a blind
    // overwrite: `baseline` is what this tab loaded before the user
    // started editing, `current` is what they're trying to save now, and
    // the row is re-fetched fresh here as the actual current DB state.
    // A field only gets the caller's value if the caller actually changed
    // it (vs baseline) — every untouched field keeps whatever's fresh in
    // the DB right now, not the caller's stale copy of it (this is what
    // protects a concurrent edit to a *different* field from ever being
    // reverted). If a field the caller touched was *also* changed by
    // someone else since baseline, that's a real conflict — reported back
    // instead of silently picking a winner, unless force=true (the
    // explicit "overwrite anyway" retry after the caller has seen the
    // conflict and chosen to proceed).
    // excludeFromConflictCheck defaults to Parcours_Client's own audit-stamp
    // fields (DateSaisie/HeureSaisie, always "now" on that page's every
    // save) — a caller whose DateSaisie/HeureSaisie are real user-provided
    // data instead (Fen_Recherche_Client's search form, which only sets
    // them when explicitly filled in) should pass an empty set so those
    // fields get the same protection as everything else.
    public async Task<ApplyResult> UpdateFullWithConflictCheckAsync(long cleOpl, ParcoursClientInput baseline, ParcoursClientInput current, bool force = false, HashSet<string>? excludeFromConflictCheck = null)
    {
        excludeFromConflictCheck ??= ExcludedFromConflictCheck;
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");
        var merged = ParcoursClientInput.FromEntity(entity);
        var conflicts = ConflictCheck.Apply(baseline, current, merged, force, ParcoursFieldLabels, excludeFromConflictCheck);

        if (conflicts.Count > 0)
        {
            return new ApplyResult { Success = false, Conflicts = conflicts };
        }

        entity.Famille = merged.Famille;
        entity.RaisonSociale = merged.RaisonSociale;
        entity.Complement = merged.Complement;
        entity.Franchise = merged.Franchise;
        entity.Franchise2 = merged.Franchise2;
        entity.Franchise3 = merged.Franchise3;
        entity.Franchise4 = merged.Franchise4;
        entity.Rue = merged.Rue;
        entity.Localisation1 = merged.Localisation1;
        entity.Localisation2 = merged.Localisation2;
        entity.Cp = merged.Cp;
        entity.Ville = merged.Ville;
        entity.Pays = merged.Pays;
        entity.Telephone = merged.Telephone;
        entity.TelBis = merged.TelBis;
        entity.Portable = merged.Portable;
        entity.Fax = merged.Fax;
        entity.Email = merged.Email;
        entity.AssistanteCommercial = merged.AssistanteCommercial;
        entity.ResponsableAchat = merged.ResponsableAchat;
        entity.OpEnCours = merged.OpEnCours;
        entity.StatusVente = merged.StatusVente;
        entity.StatutsClients = merged.StatutsClients;
        entity.EtatClient = merged.EtatClient;
        entity.Production = merged.Production;
        entity.MagasinPrincipal = merged.MagasinPrincipal;
        entity.DateSaisie = merged.DateSaisie;
        entity.HeureSaisie = merged.HeureSaisie;
        entity.DateRappel = merged.DateRappel;
        entity.HeureRappel = merged.HeureRappel;
        entity.Note = merged.Note;
        entity.Bloque = merged.Bloque;
        entity.RappelRdv = merged.RappelRdv;
        entity.Siret = merged.Siret;
        entity.Siren = merged.Siren;
        entity.FacturationElectronique = merged.FacturationElectronique;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }

        return new ApplyResult { Success = true };
    }

    // Fen_Recherche_Client's "Enregistrer Modification" — a direct write,
    // no conflict check. Unlike Parcours_Client, this isn't a long-lived
    // editing session against a remembered baseline: the caller already
    // did its own fetch-fresh-then-overlay-only-non-blank-fields merge in
    // one shot immediately before calling this, so `input` here already
    // is the fully-resolved value to write, not a diff to reconcile.
    public async Task UpdateFullAsync(long cleOpl, ParcoursClientInput input)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl)
            ?? throw new InvalidOperationException("Client introuvable.");

        entity.Famille = input.Famille;
        entity.RaisonSociale = input.RaisonSociale;
        entity.Complement = input.Complement;
        entity.Franchise = input.Franchise;
        entity.Franchise2 = input.Franchise2;
        entity.Franchise3 = input.Franchise3;
        entity.Franchise4 = input.Franchise4;
        entity.Rue = input.Rue;
        entity.Localisation1 = input.Localisation1;
        entity.Localisation2 = input.Localisation2;
        entity.Cp = input.Cp;
        entity.Ville = input.Ville;
        entity.Pays = input.Pays;
        entity.Telephone = input.Telephone;
        entity.TelBis = input.TelBis;
        entity.Portable = input.Portable;
        entity.Fax = input.Fax;
        entity.Email = input.Email;
        entity.AssistanteCommercial = input.AssistanteCommercial;
        entity.ResponsableAchat = input.ResponsableAchat;
        entity.OpEnCours = input.OpEnCours;
        entity.StatusVente = input.StatusVente;
        entity.StatutsClients = input.StatutsClients;
        entity.EtatClient = input.EtatClient;
        entity.Production = input.Production;
        entity.MagasinPrincipal = input.MagasinPrincipal;
        entity.DateSaisie = input.DateSaisie;
        entity.HeureSaisie = input.HeureSaisie;
        entity.DateRappel = input.DateRappel;
        entity.HeureRappel = input.HeureRappel;
        entity.Note = input.Note;
        entity.Bloque = input.Bloque;
        entity.RappelRdv = input.RappelRdv;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out var constraintName))
        {
            throw new InvalidOperationException(FriendlyForeignKeyMessage(constraintName));
        }
    }

    // fk_historique_num_client / fk_ca_cle_opl are ON DELETE RESTRICT —
    // unlike the lookup tables, a client with interaction history or
    // revenue rows can't be deleted, matching the old backend's behavior
    // (and unlike the legacy WinDev app itself, which let this through
    // with no protection at all).
    public async Task DeleteAsync(long cleOpl)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.FranceOptiques.FindAsync(cleOpl);
        if (entity is null) return;
        db.FranceOptiques.Remove(entity);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out _))
        {
            throw new InvalidOperationException(
                "Ce client a des interactions ou du chiffre d'affaires enregistrés et ne peut pas être supprimé.");
        }
    }

    private static void ApplyInput(FranceOptique entity, ClientInput input)
    {
        entity.RaisonSociale = input.RaisonSociale;
        entity.Complement = input.Complement;
        entity.Rue = input.Rue;
        entity.Localisation1 = input.Localisation1;
        entity.Localisation2 = input.Localisation2;
        entity.Cp = input.Cp;
        entity.Ville = input.Ville;
        entity.Pays = input.Pays;
        entity.Telephone = input.Telephone;
        entity.Portable = input.Portable;
        entity.Fax = input.Fax;
        entity.Email = input.Email;
        entity.ResponsableAchat = input.ResponsableAchat;
        entity.Representant = input.Representant;
    }

    // The constraint-name-aware message is specific enough to this entity
    // (Client -> Pays) to keep here; the detection itself now lives in the
    // shared FriendlyError helper, not duplicated.
    private static string FriendlyForeignKeyMessage(string? constraintName) =>
        constraintName?.Contains("pays") == true
            ? "Pays doit correspondre à un pays existant."
            : "Cette valeur doit correspondre à une référence existante.";
}
