using System.Linq.Dynamic.Core;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// Historique's half of Outils > Définir un Filtre (the legacy splits that
// menu into "Client" and "Historique", each opening the same kind of window
// against a different table — see ClientFilterService for the Client half,
// which this mirrors closely). Same Condition/Operator vocabulary, same
// filtre_reel text shape — Badr confirmed the syntax should read identically
// between the two, just against Historique's own columns.
//
// Field list excludes id_histo (surface identity, not a search criterion —
// same reasoning as Client's own Cle_Opl actually being included there
// though; unlike Cle_Opl, id_histo is an internal identity assigned by this
// app's IDENTITY column, never something staff would search by), fic_stk /
// fic_stk_nom (binary attachment and its filename — not exposed as
// searchable on the Client side either, no filter builder precedent for
// binary columns), and deleted_at (soft-delete bookkeeping, not a real
// business field — SearchAsync below always excludes deleted rows
// unconditionally instead, matching HistoriqueService's own convention).
public class HistoriqueFilterService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HistoriqueFilterService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public static readonly List<FilterField> Fields = new()
    {
        new("num_client", "N° Client", FieldType.Number),
        new("date_saisie", "Date Saisie", FieldType.Date),
        new("heure_saisie", "Heure Saisie", FieldType.Time),
        new("assistante_commercial", "Assistante Commerciale", FieldType.Text),
        new("date_rappel", "Date Rappel", FieldType.Date),
        new("heure_rappel", "Heure Rappel", FieldType.Time),
        new("operation", "Opération", FieldType.Text),
        new("status_vente", "Status Vente", FieldType.Text),
        new("note", "Note", FieldType.Text),
        new("franchise", "Franchise", FieldType.Text),
        new("raison_sociale", "Raison Sociale", FieldType.Text),
        new("cp", "Code Postal", FieldType.Text),
        new("ville", "Ville", FieldType.Text),
        new("statuts_clients", "Statuts Clients", FieldType.Text),
        new("magasin_principal", "Magasin Principal", FieldType.Text),
    };

    internal static readonly Dictionary<string, string> PropertyName = new()
    {
        ["num_client"] = "NumClient",
        ["date_saisie"] = "DateSaisie",
        ["heure_saisie"] = "HeureSaisie",
        ["assistante_commercial"] = "AssistanteCommercial",
        ["date_rappel"] = "DateRappel",
        ["heure_rappel"] = "HeureRappel",
        ["operation"] = "Operation",
        ["status_vente"] = "StatusVente",
        ["note"] = "Note",
        ["franchise"] = "Franchise",
        ["raison_sociale"] = "RaisonSociale",
        ["cp"] = "Cp",
        ["ville"] = "Ville",
        ["statuts_clients"] = "StatutsClients",
        ["magasin_principal"] = "MagasinPrincipal",
    };

    // Same HFSQL naming convention as ClientFilterService.LegacyFieldName —
    // reused verbatim for every column both tables share (Date_Saisie,
    // Assistante_Commerciale, Status_Vente, etc. are the same field on both
    // sides of the schema). Not verified against a real legacy screenshot
    // (Badr's doc only covered the Client filter window) — Num_Client and
    // Operation specifically are inferred from this same convention rather
    // than confirmed, same caveat FiltreReelParser already carries for its
    // own unverified Time/Boolean handling.
    internal static readonly Dictionary<string, string> LegacyFieldName = new()
    {
        ["num_client"] = "Num_Client",
        ["date_saisie"] = "Date_Saisie",
        ["heure_saisie"] = "Heure_Saisie",
        ["assistante_commercial"] = "Assistante_Commerciale",
        ["date_rappel"] = "Date_Rappel",
        ["heure_rappel"] = "Heure_Rappel",
        ["operation"] = "Operation",
        ["status_vente"] = "Status_Vente",
        ["note"] = "Note",
        ["franchise"] = "Franchise",
        ["raison_sociale"] = "Raison_Sociale",
        ["cp"] = "CP",
        ["ville"] = "Ville",
        ["statuts_clients"] = "Statuts_Clients",
        ["magasin_principal"] = "Magasin_Principal",
    };

    public async Task<(List<Historique> Rows, int Count)> SearchAsync(List<Condition> conditions)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        IQueryable<Historique> query = db.Historiques.AsNoTracking().Where(h => h.DeletedAt == null);

        foreach (var fieldGroup in conditions.GroupBy(c => c.Field))
        {
            query = ApplyFieldConditions(query, fieldGroup.ToList());
        }

        var rows = await query.OrderByDescending(h => h.IdHisto).ToListAsync();
        return (rows, rows.Count);
    }

    public async Task<(List<Historique> Rows, int Count)> SearchByFiltreReelAsync(string filtreReel)
    {
        var (predicate, args) = FiltreReelParser.ParseHistorique(filtreReel);
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Historiques.AsNoTracking()
            .Where(h => h.DeletedAt == null)
            .Where(predicate, args)
            .OrderByDescending(h => h.IdHisto)
            .ToListAsync();
        return (rows, rows.Count);
    }

    public static string BuildLegacyFiltreReel(List<Condition> conditions)
    {
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

        if (c.Values.Count > 1 && ClientFilterService.SecondValueLiaison.ContainsKey(c.Operator))
        {
            var liaison = c.Liaison ?? ClientFilterService.SecondValueLiaison[c.Operator];
            var parts = c.Values.Select(v => $"{name}{ApplyTemplate(c.Operator, v)}");
            return $"({string.Join($" {liaison} ", parts)})";
        }

        return $"({name}{ApplyTemplate(c.Operator, c.Values[0])})";
    }

    private static string ApplyTemplate(Operator op, string value) =>
        LegacyOperatorTemplate[op].Replace("%1", value);

    // Leading space on every entry — see ClientFilterService's own copy of
    // this dictionary for why it's required, not cosmetic (a word-starting
    // template glues onto the bare field name into one unparseable
    // identifier otherwise). Same bug, same fix, both discovered together
    // (2026-09-02) while verifying this Historique builder live.
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

    private static IQueryable<Historique> ApplyFieldConditions(IQueryable<Historique> query, List<Condition> sameFieldConditions)
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

    private static string BuildConditionClause(FilterField meta, string prop, Condition c, List<object> args)
    {
        if (c.Operator == Operator.Between)
        {
            var i1 = args.Count; args.Add(CastValue(meta.Type, c.Values[0]));
            var i2 = args.Count; args.Add(CastValue(meta.Type, c.Values[1]));
            return $"{prop} >= @{i1} && {prop} <= @{i2}";
        }

        if (c.Values.Count > 1 && ClientFilterService.SecondValueLiaison.ContainsKey(c.Operator))
        {
            var liaison = c.Liaison ?? ClientFilterService.SecondValueLiaison[c.Operator];
            var joiner = liaison == "ou" ? "||" : "&&";
            var parts = c.Values.Select(v =>
            {
                var idx = args.Count; args.Add(CastValue(meta.Type, v));
                return $"({ClientFilterService.OperatorTemplate(prop, c.Operator, $"@{idx}", meta.Type, args)})";
            });
            return string.Join($" {joiner} ", parts);
        }

        var index = args.Count; args.Add(CastValue(meta.Type, c.Values[0]));
        return ClientFilterService.OperatorTemplate(prop, c.Operator, $"@{index}", meta.Type, args);
    }

    private static object CastValue(FieldType type, string raw) => type switch
    {
        FieldType.Number => long.Parse(raw),
        FieldType.Date => DateOnly.Parse(raw),
        FieldType.Time => TimeOnly.Parse(raw),
        FieldType.Boolean => bool.Parse(raw),
        _ => raw,
    };
}
