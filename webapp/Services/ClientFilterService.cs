using System.Linq.Dynamic.Core;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

public enum FieldType { Text, Number, Date, Time, Boolean }

public record FilterField(string Field, string Label, FieldType Type);

public enum Operator { Eq, Ne, Gt, Gte, Lt, Lte, Between, StartsWith, NotStartsWith, Contains, NotContains }

// Values holds however many values this condition actually needs:
// exactly 1 for a plain comparison, exactly 2 for Between (min, max — a
// range never has more than two ends), or 1..N for the operators in
// SecondValueLiaison (Eq/Ne/StartsWith/NotStartsWith/Contains/
// NotContains), each combined with Liaison — "more values" only ever
// means more alternatives on the latter, not a longer range.
//
// Liaison ("ou"/"et") used to be auto-picked per operator and fixed —
// Badr wants manual control instead, since he understands (and accepts)
// that the "wrong" pairing produces a condition that matches everything
// or nothing (Egal à + et is always false, Différent de + ou is always
// true — see SecondValueLiaison's own comment). Null/unset falls back to
// that same auto-picked default, so callers that never set it (Recherche
// Client's single-value conditions, chiefly) are unaffected.
// GroupLiaison ("ou"/"et", default "ou") only matters when the same Field
// appears as more than one Condition in a single request — e.g. "Famille
// = X" OU "Famille <> Y", which needs two Conditions on "famille" since
// each has its own Operator. It says how THIS Condition combines with the
// PREVIOUS Condition for the same field; meaningless (and ignored) on a
// field's first/only Condition. Distinct from Liaison above, which only
// ever combines multiple Values within one Condition's own Operator.
public class Condition
{
    public string Field { get; set; } = string.Empty;
    public Operator Operator { get; set; }
    public List<string> Values { get; set; } = new();
    public string? Liaison { get; set; }
    public string? GroupLiaison { get; set; }
}

// Mirrors the legacy Fen_Definir_Filtre's RempliRubriques(), which dynamically
// enumerated every non-binary column of France_Optique via HListeRubrique().
// We don't have runtime HFSQL introspection, so this list is the static
// equivalent — every scalar column of france_optique, excluding relations.
// Field keys match the Prisma-era snake_case names 1:1 (kept from the old
// stack for continuity), mapped to the actual PascalCase C# property names
// via PropertyName below, which is what the dynamic query string uses.
public class ClientFilterService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClientFilterService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public static readonly List<FilterField> Fields = new()
    {
        new("famille", "Famille", FieldType.Text),
        new("raison_sociale", "Raison Sociale", FieldType.Text),
        new("complement", "Complément", FieldType.Text),
        new("franchise", "Franchise", FieldType.Text),
        new("franchise2", "Franchise 2", FieldType.Text),
        new("franchise3", "Franchise 3", FieldType.Text),
        new("franchise4", "Franchise 4", FieldType.Text),
        new("rue", "Rue", FieldType.Text),
        new("localisation_1", "Localisation 1", FieldType.Text),
        new("localisation_2", "Localisation 2", FieldType.Text),
        new("cp", "Code Postal", FieldType.Text),
        new("ville", "Ville", FieldType.Text),
        new("pays", "Pays", FieldType.Text),
        new("telephone", "Téléphone", FieldType.Text),
        new("tel_bis", "Téléphone Bis", FieldType.Text),
        new("portable", "Portable", FieldType.Text),
        new("email", "Email", FieldType.Text),
        new("assistante_commercial", "Assistante Commerciale", FieldType.Text),
        new("responsable_achat", "Responsable Achat", FieldType.Text),
        new("representant", "Représentant", FieldType.Text),
        new("op_en_cours", "Opération en Cours", FieldType.Text),
        new("status_vente", "Status Vente", FieldType.Text),
        new("statuts_clients", "Statuts Clients", FieldType.Text),
        new("etat_client", "État Client", FieldType.Text),
        new("production", "Production", FieldType.Text),
        new("magasin_principal", "Magasin Principal", FieldType.Text),
        new("date_saisie", "Date Saisie", FieldType.Date),
        new("heure_saisie", "Heure Saisie", FieldType.Time),
        new("date_rappel", "Date Rappel", FieldType.Date),
        new("heure_rappel", "Heure Rappel", FieldType.Time),
        new("note", "Note", FieldType.Text),
        new("note_perm", "Note Permanente", FieldType.Text),
        new("bloque", "Bloqué", FieldType.Boolean),
        new("rappel_rdv", "Rappel RDV", FieldType.Boolean),
        new("cle_opl", "Cle Opl", FieldType.Number),
    };

    // internal, not private: FiltreReelParser reuses this to resolve legacy
    // field names to entity property names when interpreting saved raw
    // filter text (see FiltreReelParser.cs).
    internal static readonly Dictionary<string, string> PropertyName = new()
    {
        ["famille"] = "Famille",
        ["raison_sociale"] = "RaisonSociale",
        ["complement"] = "Complement",
        ["franchise"] = "Franchise",
        ["franchise2"] = "Franchise2",
        ["franchise3"] = "Franchise3",
        ["franchise4"] = "Franchise4",
        ["rue"] = "Rue",
        ["localisation_1"] = "Localisation1",
        ["localisation_2"] = "Localisation2",
        ["cp"] = "Cp",
        ["ville"] = "Ville",
        ["pays"] = "Pays",
        ["telephone"] = "Telephone",
        ["tel_bis"] = "TelBis",
        ["portable"] = "Portable",
        ["email"] = "Email",
        ["assistante_commercial"] = "AssistanteCommercial",
        ["responsable_achat"] = "ResponsableAchat",
        ["representant"] = "Representant",
        ["op_en_cours"] = "OpEnCours",
        ["status_vente"] = "StatusVente",
        ["statuts_clients"] = "StatutsClients",
        ["etat_client"] = "EtatClient",
        ["production"] = "Production",
        ["magasin_principal"] = "MagasinPrincipal",
        ["date_saisie"] = "DateSaisie",
        ["heure_saisie"] = "HeureSaisie",
        ["date_rappel"] = "DateRappel",
        ["heure_rappel"] = "HeureRappel",
        ["note"] = "Note",
        ["note_perm"] = "NotePerm",
        ["bloque"] = "Bloque",
        ["rappel_rdv"] = "RappelRdv",
        ["cle_opl"] = "CleOpl",
    };

    // The legacy exposed all 11 operators against every field regardless of
    // type (it just built a raw SQL string). Our query is typed, so
    // LIKE-style operators only make sense on text columns.
    public static readonly Dictionary<FieldType, Operator[]> OperatorsByType = new()
    {
        [FieldType.Text] = new[] { Operator.Eq, Operator.Ne, Operator.StartsWith, Operator.NotStartsWith, Operator.Contains, Operator.NotContains, Operator.Gt, Operator.Gte, Operator.Lt, Operator.Lte, Operator.Between },
        [FieldType.Number] = new[] { Operator.Eq, Operator.Ne, Operator.Gt, Operator.Gte, Operator.Lt, Operator.Lte, Operator.Between },
        [FieldType.Date] = new[] { Operator.Eq, Operator.Ne, Operator.Gt, Operator.Gte, Operator.Lt, Operator.Lte, Operator.Between },
        [FieldType.Time] = new[] { Operator.Eq, Operator.Ne, Operator.Gt, Operator.Gte, Operator.Lt, Operator.Lte, Operator.Between },
        [FieldType.Boolean] = new[] { Operator.Eq, Operator.Ne },
    };

    // Matches the legacy's "Sélection d'une ligne de CONDITION" auto-liaison
    // exactly for the operators where it assigns a real value. For
    // gt/gte/lt/lte the legacy leaves LIAISON blank, which — since it just
    // concatenates raw SQL text — produces a malformed, effectively broken
    // double-clause. We don't reproduce that: a second value is only offered
    // for the operators below, matching the legacy's own working cases.
    public static readonly Dictionary<Operator, string> SecondValueLiaison = new()
    {
        [Operator.Eq] = "ou",
        [Operator.Ne] = "et",
        [Operator.StartsWith] = "ou",
        [Operator.NotStartsWith] = "et",
        [Operator.Contains] = "ou",
        [Operator.NotContains] = "et",
    };

    // Real column names from the RESULTAT grid's "Liaison des données" lines
    // in the legacy documentation, used only for building the WinDev-style
    // filtre_reel text (see BuildLegacyFiltreReel) — the actual query above
    // uses PropertyName (the C# entity property names) instead, since that's
    // what EF Core/System.Linq.Dynamic.Core need.
    // internal, not private: FiltreReelParser builds its legacy-name lookup
    // (case-insensitive) from this same map, so the two stay in lockstep —
    // any field added here is automatically parseable too.
    internal static readonly Dictionary<string, string> LegacyFieldName = new()
    {
        ["famille"] = "Famille",
        ["raison_sociale"] = "Raison_Sociale",
        ["complement"] = "Complement",
        ["franchise"] = "Franchise",
        ["franchise2"] = "Franchise2",
        ["franchise3"] = "Franchise3",
        ["franchise4"] = "Franchise4",
        ["rue"] = "Rue",
        ["localisation_1"] = "Localisation_1",
        ["localisation_2"] = "Localisation_2",
        ["cp"] = "CP",
        ["ville"] = "Ville",
        ["pays"] = "Pays",
        ["telephone"] = "Telephone",
        ["tel_bis"] = "Tel_Bis",
        ["portable"] = "Portable",
        ["email"] = "Email",
        ["assistante_commercial"] = "Assistante_Commerciale",
        ["responsable_achat"] = "Responsable_Achat",
        ["representant"] = "Représentant",
        ["op_en_cours"] = "Op_En_Cours",
        ["status_vente"] = "Status_Vente",
        ["statuts_clients"] = "Statuts_Clients",
        ["etat_client"] = "Etat_Client",
        ["production"] = "Production",
        ["magasin_principal"] = "Magasin_Principal",
        ["date_saisie"] = "Date_Saisie",
        ["heure_saisie"] = "Heure_Saisie",
        ["date_rappel"] = "Date_Rappel",
        ["heure_rappel"] = "Heure_Rappel",
        ["note"] = "Note",
        ["note_perm"] = "Note_Perm",
        ["bloque"] = "Bloque",
        ["rappel_rdv"] = "Rappel_RDV",
        ["cle_opl"] = "Cle_Opl",
    };

    // Leading space on every entry, not just cosmetic: BuildSingleConditionClause
    // concatenates this directly onto the bare field name with no separator
    // (e.g. "Franchise" + this), and FiltreReelParser's tokenizer has no
    // notion of a word boundary other than whitespace/punctuation — a
    // word-starting template like "LIKE ..." or "BETWEEN ..." glues onto the
    // field name into one unparseable identifier ("FranchiseLIKE") without
    // it. Confirmed live: a builder-generated Contains filter saved and
    // reloaded via Filtres Prédéfinis/Fichier Historique's "Filtre :" picker
    // threw "Unknown field FranchiseLIKE" before this fix. The
    // punctuation-leading templates (=, <>, <, etc.) didn't need the space to
    // parse correctly, but it's added there too for consistency with the
    // legacy doc's own convention ("Famille =", "Telephone LIKE").
    private static readonly Dictionary<Operator, string> LegacyOperatorTemplate = new()
    {
        [Operator.Eq] = " ='%1'",
        [Operator.Ne] = " <>'%1'",
        [Operator.Lt] = " <'%1'",
        [Operator.Lte] = " <='%1'",
        [Operator.Gt] = " >'%1'",
        [Operator.Gte] = " >='%1'",
        [Operator.Contains] = " LIKE '%%1%'",
        [Operator.StartsWith] = " LIKE '%1%'",
        [Operator.NotStartsWith] = " NOT LIKE '%1%'",
        [Operator.NotContains] = " NOT LIKE '%%1%'",
        [Operator.Between] = " BETWEEN '%1' AND '%2'",
    };

    // Badr's explicit direction: filtre_reel should read the same way it
    // does in real WinDev — matches the real migrated data's style
    // (hand-typed condition text), not a structured format like JSON.
    // FiltreReelParser's grammar was already unlimited (real saved data has
    // three-way "et" chains on one field, e.g. "Note <>'9999' et Note
    // <>'5555' et Note <>'4444'") — this just stops capping the *builder*
    // at two values when the text format underneath never was.
    public static string BuildLegacyFiltreReel(List<Condition> conditions)
    {
        // Grouped by field for the same reason as ApplyFieldConditions above:
        // several Conditions on one field (different Operators, combined via
        // their own GroupLiaison) read as one parenthesized group here too,
        // e.g. "(Famille='X' ou Famille<>'Y')" — valid input to
        // FiltreReelParser unchanged, since its grammar already allows
        // repeating a field with different operators.
        var fieldClauses = conditions.GroupBy(c => c.Field).Select(group =>
        {
            var list = group.ToList();
            if (list.Count == 1) return BuildSingleConditionClause(list[0]);

            var combined = BuildSingleConditionClause(list[0]);
            for (var i = 1; i < list.Count; i++)
            {
                var liaison = list[i].GroupLiaison ?? "ou";
                combined = $"{combined} {liaison} {BuildSingleConditionClause(list[i])}";
            }
            return $"({combined})";
        });
        return string.Join(" et ", fieldClauses);
    }

    private static string BuildSingleConditionClause(Condition c)
    {
        var name = LegacyFieldName[c.Field];

        if (c.Operator == Operator.Between)
        {
            var clause = $"{name}{ApplyTemplate(c.Operator, c.Values[0])}".Replace("%2", c.Values[1]);
            return $"({clause})";
        }

        if (c.Values.Count > 1 && SecondValueLiaison.ContainsKey(c.Operator))
        {
            var liaison = c.Liaison ?? SecondValueLiaison[c.Operator];
            var parts = c.Values.Select(v => $"{name}{ApplyTemplate(c.Operator, v)}");
            return $"({string.Join($" {liaison} ", parts)})";
        }

        return $"({name}{ApplyTemplate(c.Operator, c.Values[0])})";
    }

    private static string ApplyTemplate(Operator op, string value) =>
        LegacyOperatorTemplate[op].Replace("%1", value);

    // Powers the autocomplete inputs on Fen_Recherche_Client — suggests real
    // values already present in france_optique rather than a blind free-text
    // field or a rigid dropdown, per Badr's direction ("suggesting how to
    // complete depending on what we have in our db"). Explicit switch over
    // strongly-typed EF Core Select() rather than a Dynamic LINQ projection —
    // Dynamic LINQ is already used for the structured-filter query below, but
    // its single-property Select() has version-dependent wrapping behavior
    // that isn't worth the uncertainty for something this simple.
    public async Task<List<string>> SuggestValuesAsync(string field, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return new();

        await using var db = await _dbFactory.CreateDbContextAsync();
        IQueryable<string?> query = field switch
        {
            "famille" => db.FranceOptiques.Select(c => c.Famille),
            "raison_sociale" => db.FranceOptiques.Select(c => c.RaisonSociale),
            "complement" => db.FranceOptiques.Select(c => c.Complement),
            "franchise" => db.FranceOptiques.Select(c => c.Franchise),
            "franchise2" => db.FranceOptiques.Select(c => c.Franchise2),
            "franchise3" => db.FranceOptiques.Select(c => c.Franchise3),
            "franchise4" => db.FranceOptiques.Select(c => c.Franchise4),
            "rue" => db.FranceOptiques.Select(c => c.Rue),
            "localisation_1" => db.FranceOptiques.Select(c => c.Localisation1),
            "localisation_2" => db.FranceOptiques.Select(c => c.Localisation2),
            "cp" => db.FranceOptiques.Select(c => c.Cp),
            "ville" => db.FranceOptiques.Select(c => c.Ville),
            "pays" => db.FranceOptiques.Select(c => c.Pays),
            "magasin_principal" => db.FranceOptiques.Select(c => c.MagasinPrincipal),
            "telephone" => db.FranceOptiques.Select(c => c.Telephone),
            "portable" => db.FranceOptiques.Select(c => c.Portable),
            "tel_bis" => db.FranceOptiques.Select(c => c.TelBis),
            "email" => db.FranceOptiques.Select(c => c.Email),
            "assistante_commercial" => db.FranceOptiques.Select(c => c.AssistanteCommercial),
            "responsable_achat" => db.FranceOptiques.Select(c => c.ResponsableAchat),
            "note" => db.FranceOptiques.Select(c => c.Note),
            _ => Enumerable.Empty<string?>().AsQueryable(),
        };

        // ILike (Npgsql's translation of Postgres's native case-insensitive
        // ILIKE), not Contains — confirmed by testing directly that plain
        // Contains() here compiles to a case-sensitive LIKE on this setup,
        // which would mean typing "aff" suggests nothing until you happen to
        // capitalize it "Aff". Left the structured Condition-based search
        // below (and FiltreReelParser, which shares the same
        // OperatorTemplate) as case-sensitive, matching their existing,
        // already-relied-upon behavior elsewhere in the app — this method is
        // new and only used here, so tightening it doesn't risk regressing
        // anything else.
        return await query
            .Where(v => v != null && EF.Functions.ILike(v!, $"%{prefix}%"))
            .Distinct()
            .OrderBy(v => v)
            .Take(20)
            .Select(v => v!)
            .ToListAsync();
    }

    public async Task<(List<FranceOptique> Rows, int Count)> SearchAsync(List<Condition> conditions)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        IQueryable<FranceOptique> query = db.FranceOptiques.AsNoTracking();

        // Grouped by field first: two Conditions on the same field (e.g.
        // "Famille = X" + "Famille <> Y", one Condition per Operator) combine
        // via their own GroupLiaison into one predicate, not two independent
        // AND'd .Where() calls — GroupBy preserves each group's original
        // relative order, so GroupLiaison (which only makes sense relative to
        // "the previous condition for this field") lines up correctly.
        // Different fields still always AND together, unchanged.
        foreach (var fieldGroup in conditions.GroupBy(c => c.Field))
        {
            query = fieldGroup.Key == FranchiseAnyField
                ? ApplyFranchiseAnyConditions(query, fieldGroup.ToList())
                : ApplyFieldConditions(query, fieldGroup.ToList());
        }

        var rows = await query.OrderBy(c => c.CleOpl).ToListAsync();
        return (rows, rows.Count);
    }

    // Backs Fen_Filtre's "Filtre :" combo — runs an already-saved
    // filtre_reel (raw WinDev-style text, parsed by FiltreReelParser)
    // instead of a structured Condition list. Lets a FormatException from
    // the parser propagate — the caller is expected to show it, not swallow
    // it, since a filter that can't be parsed genuinely can't be run yet.
    public async Task<(List<FranceOptique> Rows, int Count)> SearchByFiltreReelAsync(string filtreReel)
    {
        var (predicate, args) = FiltreReelParser.Parse(filtreReel);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.FranceOptiques.AsNoTracking()
            .Where(predicate, args)
            .OrderBy(c => c.CleOpl)
            .ToListAsync();
        return (rows, rows.Count);
    }

    // Recherche Client's 4 Franchise boxes (Badr, 2026-09-28): a filled box
    // should match if its value appears in ANY of the client's 4 real
    // franchise columns, not just its own same-numbered one — so it no
    // longer matters which of the four slots a client's franchise happens
    // to be recorded under. Multiple filled boxes combine via OR (confirmed:
    // a client with EITHER value anywhere across their 4 columns matches,
    // including a client that has both).
    //
    // Deliberately not a real entry in the public Fields list — it isn't a
    // real column, and has no business showing up in Définir un Filtre's
    // generic field picker (DefinirFiltrePage.razor, the only other place
    // that enumerates Fields). Only RechercheClientPage's own
    // BuildConditions() ever constructs a Condition with this Field value.
    public const string FranchiseAnyField = "franchise_any";

    private static readonly string[] FranchiseProps = { "Franchise", "Franchise2", "Franchise3", "Franchise4" };

    private static IQueryable<FranceOptique> ApplyFranchiseAnyConditions(IQueryable<FranceOptique> query, List<Condition> conditions)
    {
        var args = new List<object>();
        var valueClauses = new List<string>();

        foreach (var c in conditions)
        {
            var idx = args.Count;
            args.Add(CastValue(FieldType.Text, c.Values[0]));
            var perColumn = FranchiseProps.Select(p => OperatorTemplate(p, Operator.Eq, $"@{idx}", FieldType.Text, args));
            valueClauses.Add($"({string.Join(" || ", perColumn)})");
        }

        return query.Where(string.Join(" || ", valueClauses), args.ToArray());
    }

    // Applies every Condition already known to share one Field as a single
    // combined .Where() — needed (rather than one .Where() per Condition)
    // because Dynamic LINQ's "@0, @1, ..." placeholders are positional
    // across one whole predicate string, so multi-condition placeholders
    // have to be numbered globally as they're built, not restart at 0 per
    // condition.
    private static IQueryable<FranceOptique> ApplyFieldConditions(IQueryable<FranceOptique> query, List<Condition> sameFieldConditions)
    {
        var meta = Fields.First(f => f.Field == sameFieldConditions[0].Field);
        var prop = PropertyName[sameFieldConditions[0].Field];
        var args = new List<object>();
        var clauses = new List<string>();

        foreach (var c in sameFieldConditions)
        {
            clauses.Add(BuildConditionClause(meta, prop, c, args));
        }

        var combined = clauses[0];
        for (var i = 1; i < clauses.Count; i++)
        {
            var joiner = (sameFieldConditions[i].GroupLiaison ?? "ou") == "ou" ? "||" : "&&";
            combined = $"({combined}) {joiner} ({clauses[i]})";
        }

        return query.Where(combined, args.ToArray());
    }

    // Builds one Condition's predicate fragment, appending its values to the
    // shared args list (so placeholder numbers stay globally correct when
    // ApplyFieldConditions strings several of these together) and returning
    // just the fragment text — same logic the old single-condition
    // ApplyCondition used, just no longer calling .Where() itself.
    private static string BuildConditionClause(FilterField meta, string prop, Condition c, List<object> args)
    {
        if (c.Operator == Operator.Between)
        {
            var i1 = args.Count; args.Add(CastValue(meta.Type, c.Values[0]));
            var i2 = args.Count; args.Add(CastValue(meta.Type, c.Values[1]));
            return $"{prop} >= @{i1} && {prop} <= @{i2}";
        }

        if (c.Values.Count > 1 && SecondValueLiaison.ContainsKey(c.Operator))
        {
            var liaison = c.Liaison ?? SecondValueLiaison[c.Operator];
            var joiner = liaison == "ou" ? "||" : "&&";
            var parts = c.Values.Select(v =>
            {
                var idx = args.Count; args.Add(CastValue(meta.Type, v));
                return $"({OperatorTemplate(prop, EffectiveOperator(c.Operator, v), $"@{idx}", meta.Type, args)})";
            });
            return string.Join($" {joiner} ", parts);
        }

        var index = args.Count; args.Add(CastValue(meta.Type, c.Values[0]));
        return OperatorTemplate(prop, EffectiveOperator(c.Operator, c.Values[0]), $"@{index}", meta.Type, args);
    }

    // HFSQL's SYSDATE is a moment in time ("right now"), not a calendar
    // date — comparing a date-only field against it with "<" still needs
    // to include a reminder dated exactly today, since midnight-today is
    // always earlier than the current moment. Confirmed against real
    // data: Bruno's "Optique Luxembourg à rappeler" filter
    // (Date_Rappel < SYSDATE) returned 151 here (today excluded) vs the
    // legacy's 209 — the exact 58-row gap was every "exactly today" row
    // that also had a real phone number (see BuildBlankClause below),
    // meaning the legacy really does treat "today" as already due under
    // strict "<". Only Lt is adjusted (to Lte) — Lte/Gt already produce
    // the right boundary unadjusted, and Gte/Eq have no real saved-filter
    // usage of SYSDATE to verify against, so they're deliberately left
    // alone rather than guessed at.
    internal static Operator EffectiveOperator(Operator op, string raw) =>
        op == Operator.Lt && raw == "SYSDATE" ? Operator.Lte : op;

    // HFSQL compares text case- and accent-insensitively by default (a real
    // client on both sides confirmed this: legacy vs. this app's counts only
    // ever diverged on filters where the saved text's casing/accents didn't
    // exactly match the stored data — e.g. "PAYS = 'FRANCE'" against a
    // stored "France" returned 0 here but 7,088 in the legacy system, and
    // "Optic 2000" against a stored "OPTIC 2000" returned 0 here but 123
    // there; both now match exactly). Plain .ToLower() handles casing;
    // accents are folded via explicit .Replace() calls for the actual
    // accented characters found in the real data — not full Unicode
    // normalization (no NFD/combining-mark stripping), but this covers every
    // character that appears in this app's own French business data, and
    // every replacement is a bound parameter (@N), never a literal accented
    // character spliced into the predicate string itself.
    private static readonly (string From, string To)[] AccentFolds =
    {
        ("é", "e"), ("è", "e"), ("ê", "e"), ("ë", "e"),
        ("à", "a"), ("â", "a"), ("ä", "a"),
        ("ù", "u"), ("û", "u"), ("ü", "u"),
        ("ô", "o"), ("ö", "o"),
        ("î", "i"), ("ï", "i"),
        ("ç", "c"),
        ("œ", "oe"),
        ("ñ", "n"),
    };

    private static string Fold(string expr, List<object> args)
    {
        var result = $"{expr}.ToLower()";
        foreach (var (from, to) in AccentFolds)
        {
            var fromIndex = args.Count; args.Add(from);
            var toIndex = args.Count; args.Add(to);
            result = $"{result}.Replace(@{fromIndex}, @{toIndex})";
        }
        return result;
    }

    // internal, not private: FiltreReelParser reuses this so parsed raw
    // filter text produces the exact same null-safe, case/accent-folded
    // predicate shape as the structured builder. `type`/`args` are only
    // used to fold Text comparisons (see Fold above) — every other field
    // type compares exactly as before, casing/accents being meaningless for
    // numbers/dates/times/booleans. The null guard on StartsWith/Contains
    // still checks the raw, unfolded `prop` — lower-casing/replacing a NULL
    // string still yields NULL, so the check is equivalent either way, and
    // keeping it on `prop` avoids relying on that being true.
    internal static string OperatorTemplate(string prop, Operator op, string placeholder, FieldType type, List<object> args)
    {
        var lhs = type == FieldType.Text ? Fold(prop, args) : prop;
        var rhs = type == FieldType.Text ? Fold(placeholder, args) : placeholder;

        // HFSQL stores empty text as "" and empty dates/numbers as 0 — the
        // minimum of the type — not NULL. Comparisons therefore treat an
        // empty value as "anything": "" <> '9999' matches, 0 < SYSDATE (today)
        // matches, but "" = 'X' and 0 > SYSDATE never do. Postgres NULLs are
        // the migrated form of those empties (see migration/csv_common.py),
        // so the same semantics are reproduced here: < <= and <>/NOT-type
        // comparisons include NULL, while = > >= BETWEEN (and the
        // positive Contains/StartsWith, which an empty string can't contain
        // anything) exclude it. Both sides are folded for Text so empties
        // stay empties through the fold.
        return op switch
        {
            Operator.Eq => $"({lhs} != null && {lhs} == {rhs})",
            Operator.Ne => $"({lhs} == null || {lhs} != {rhs})",
            Operator.Gt => $"({lhs} != null && {lhs} > {rhs})",
            Operator.Gte => $"({lhs} != null && {lhs} >= {rhs})",
            Operator.Lt => $"({lhs} == null || {lhs} < {rhs})",
            Operator.Lte => $"({lhs} == null || {lhs} <= {rhs})",
            Operator.StartsWith => $"({prop} != null && {lhs}.StartsWith({rhs}))",
            Operator.NotStartsWith => $"({prop} == null || !{lhs}.StartsWith({rhs}))",
            Operator.Contains => $"({prop} != null && {lhs}.Contains({rhs}))",
            Operator.NotContains => $"({prop} == null || !{lhs}.Contains({rhs}))",
            _ => throw new InvalidOperationException($"Unsupported operator: {op}"),
        };
    }

    // "SYSDATE" (DefinirFiltrePage's "Aujourd'hui" checkbox, or a saved
    // filtre_reel containing the keyword) resolves to today, same special
    // case FiltreReelParser.CastLegacyValue already applies when re-running
    // a saved filter — needed here too since this path backs Définir un
    // Filtre's own live "Rechercher" preview, before anything is saved.
    private static object CastValue(FieldType type, string raw) =>
        (type, raw) switch
        {
            (FieldType.Date, "SYSDATE") => DateOnly.FromDateTime(DateTime.Today),
            (FieldType.Number, _) => long.Parse(raw),
            (FieldType.Date, _) => DateOnly.Parse(raw),
            (FieldType.Time, _) => TimeOnly.Parse(raw),
            (FieldType.Boolean, _) => bool.Parse(raw),
            _ => raw,
        };
}
