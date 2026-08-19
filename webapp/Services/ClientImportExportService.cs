using System.Globalization;
using ClosedXML.Excel;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// Backs the consolidated Excel import/export feature (roadmap Phase 5).
// The legacy app actually has four overlapping import mechanisms (Fen_
// Fichier_Client's naive insert-only import, Fen_Update's proper upsert-by-
// Cle_Opl, Fen_Ajout_Depuis_Excel's auto-numbering bulk insert,
// FEN_Modification_Depuis_Excel's "#"-clears-field bulk update) — which one
// staff actually use day to day was never resolved during the functional
// review. Confirmed with Badr: build one consolidated tool modeled on
// Fen_Update's mechanics (match by Cle_Opl, insert if new, update only
// fields that actually changed, report real counts), but — after Badr
// pushed back on the first cut only covering Fen_Update's own 9 fields —
// widened to cover every field Parcours_Client can already edit, not just
// what one legacy screen happened to touch. Import itself is admin-only
// (confirmed with Badr): it's a bulk-write action across the whole client
// table, a materially bigger blast radius than editing one client at a
// time, so it gets the same restriction as the other admin-only actions
// in this app (Utilisateurs, Historique des Connexions) — enforced both in
// the UI (Clients.razor hides the control) and here, as the real boundary.
//
// Deliberately excludes NotePerm: unlike every other field here, nothing
// else in the app can write NotePerm today (it's read-only everywhere,
// including on Parcours_Client) — flagged to Badr rather than silently
// making import the first-ever write path for a field named "permanent".
//
// Columns are a single ordered list (Columns, below) shared by both
// Export and ImportAsync, specifically so the two can't drift out of sync
// by hand — with ~38 columns, indexing each one separately in two places
// was exactly the kind of thing that goes quietly wrong.
public record ImportResult(
    int Added,
    int Modified,
    int Unchanged,
    List<string> SkippedRows,
    Dictionary<string, int> InvalidReferenceCounts);

public class ClientImportExportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ClientImportExportService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // Dates as AAAAMMJJ (yyyyMMdd, no separators) — not a new convention,
    // it's the exact format the app's own saved-filter syntax already uses
    // for dates (docs/Syntaxe_Filtres.txt, "Les dates s'écrivent au format
    // AAAAMMJJ"). Reusing it here instead of inventing a second date
    // format for this one feature. Times as HH:mm, matching how the app
    // already displays them everywhere (Clients.razor, Fichier_Historique).
    // Booleans as VRAI/FAUX (also accepts "1"/"OUI" as true on read, for
    // anyone typing by hand) — written as plain text, never a real Excel
    // date/number type, so there's no ambiguity from Excel's own
    // auto-formatting when the file is reopened or hand-edited.
    private sealed class ColumnDef
    {
        public required string Header { get; init; }
        public required Func<FranceOptique, string> ToText { get; init; }
        public required Action<FranceOptique, string> FromText { get; init; }
        // Change detection compares ToText(entity) against
        // Normalize(rawCellText), not the raw cell text directly. For plain
        // string fields the two are the same thing (a blank cell already
        // reads as ""), so this defaults to the identity function. Bool and
        // date/time columns override it — a blank Bloque cell and an
        // explicit "FAUX" cell both mean false, but ToText(false) is always
        // "FAUX", never "". Without normalizing the incoming side the same
        // way, every blank boolean/date/time cell would look like a change
        // even when it isn't (caught by the "unchanged round-trip" test
        // case during verification — a real bug, not a hypothetical one).
        public Func<string, string> Normalize { get; init; } = s => s;
        // Non-null for the 7 Fichiers-reference-backed fields (Famille,
        // Franchise x4, Pays, Assistante Commerciale) — the standing
        // dropdown rule applies to bulk import exactly as it does to every
        // other save path in this app. Groups Franchise/2/3/4 together
        // since they all validate against the same type_franchise table.
        public string? ReferenceGroup { get; init; }
    }

    private static string? Blank(string s) => s.Length == 0 ? null : s;

    private static bool ParseBool(string s) => s.Trim().ToUpperInvariant() is "VRAI" or "1" or "OUI";

    private static string BoolText(bool b) => b ? "VRAI" : "FAUX";

    private static DateOnly? ParseDate(string s) =>
        DateOnly.TryParseExact(s.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string DateText(DateOnly? d) => d?.ToString("yyyyMMdd") ?? "";

    private static TimeOnly? ParseTime(string s) =>
        TimeOnly.TryParseExact(s.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    private static string TimeText(TimeOnly? t) => t?.ToString("HH:mm") ?? "";

    private static readonly List<ColumnDef> Columns = new()
    {
        new() { Header = "Famille", ReferenceGroup = "Famille",
            ToText = c => c.Famille ?? "", FromText = (c, v) => c.Famille = Blank(v) },
        new() { Header = "Raison Sociale",
            ToText = c => c.RaisonSociale ?? "", FromText = (c, v) => c.RaisonSociale = Blank(v) },
        new() { Header = "Complement",
            ToText = c => c.Complement ?? "", FromText = (c, v) => c.Complement = Blank(v) },
        new() { Header = "Franchise", ReferenceGroup = "Franchise",
            ToText = c => c.Franchise ?? "", FromText = (c, v) => c.Franchise = Blank(v) },
        new() { Header = "Franchise 2", ReferenceGroup = "Franchise",
            ToText = c => c.Franchise2 ?? "", FromText = (c, v) => c.Franchise2 = Blank(v) },
        new() { Header = "Franchise 3", ReferenceGroup = "Franchise",
            ToText = c => c.Franchise3 ?? "", FromText = (c, v) => c.Franchise3 = Blank(v) },
        new() { Header = "Franchise 4", ReferenceGroup = "Franchise",
            ToText = c => c.Franchise4 ?? "", FromText = (c, v) => c.Franchise4 = Blank(v) },
        new() { Header = "Rue",
            ToText = c => c.Rue ?? "", FromText = (c, v) => c.Rue = Blank(v) },
        new() { Header = "Localisation 1",
            ToText = c => c.Localisation1 ?? "", FromText = (c, v) => c.Localisation1 = Blank(v) },
        new() { Header = "Localisation 2",
            ToText = c => c.Localisation2 ?? "", FromText = (c, v) => c.Localisation2 = Blank(v) },
        new() { Header = "Code Postal",
            ToText = c => c.Cp ?? "", FromText = (c, v) => c.Cp = Blank(v) },
        new() { Header = "Ville",
            ToText = c => c.Ville ?? "", FromText = (c, v) => c.Ville = Blank(v) },
        new() { Header = "Pays", ReferenceGroup = "Pays",
            ToText = c => c.Pays ?? "", FromText = (c, v) => c.Pays = Blank(v) },
        new() { Header = "Telephone",
            ToText = c => c.Telephone ?? "", FromText = (c, v) => c.Telephone = Blank(v) },
        new() { Header = "Telephone Bis",
            ToText = c => c.TelBis ?? "", FromText = (c, v) => c.TelBis = Blank(v) },
        new() { Header = "Portable",
            ToText = c => c.Portable ?? "", FromText = (c, v) => c.Portable = Blank(v) },
        new() { Header = "Fax",
            ToText = c => c.Fax ?? "", FromText = (c, v) => c.Fax = Blank(v) },
        new() { Header = "Email",
            ToText = c => c.Email ?? "", FromText = (c, v) => c.Email = Blank(v) },
        new() { Header = "Assistante Commerciale", ReferenceGroup = "Assistante",
            ToText = c => c.AssistanteCommercial ?? "", FromText = (c, v) => c.AssistanteCommercial = Blank(v) },
        new() { Header = "Responsable Achat",
            ToText = c => c.ResponsableAchat ?? "", FromText = (c, v) => c.ResponsableAchat = Blank(v) },
        new() { Header = "Representant",
            ToText = c => c.Representant ?? "", FromText = (c, v) => c.Representant = Blank(v) },
        new() { Header = "Operation En Cours",
            ToText = c => c.OpEnCours ?? "", FromText = (c, v) => c.OpEnCours = Blank(v) },
        new() { Header = "Status Vente",
            ToText = c => c.StatusVente ?? "", FromText = (c, v) => c.StatusVente = Blank(v) },
        new() { Header = "Statuts Clients",
            ToText = c => c.StatutsClients ?? "", FromText = (c, v) => c.StatutsClients = Blank(v) },
        new() { Header = "Etat Client",
            ToText = c => c.EtatClient ?? "", FromText = (c, v) => c.EtatClient = Blank(v) },
        new() { Header = "Production",
            ToText = c => c.Production ?? "", FromText = (c, v) => c.Production = Blank(v) },
        new() { Header = "Magasin Principal",
            ToText = c => c.MagasinPrincipal ?? "", FromText = (c, v) => c.MagasinPrincipal = Blank(v) },
        new() { Header = "Date Saisie",
            ToText = c => DateText(c.DateSaisie), FromText = (c, v) => c.DateSaisie = ParseDate(v),
            Normalize = v => DateText(ParseDate(v)) },
        new() { Header = "Heure Saisie",
            ToText = c => TimeText(c.HeureSaisie), FromText = (c, v) => c.HeureSaisie = ParseTime(v),
            Normalize = v => TimeText(ParseTime(v)) },
        new() { Header = "Date Rappel",
            ToText = c => DateText(c.DateRappel), FromText = (c, v) => c.DateRappel = ParseDate(v),
            Normalize = v => DateText(ParseDate(v)) },
        new() { Header = "Heure Rappel",
            ToText = c => TimeText(c.HeureRappel), FromText = (c, v) => c.HeureRappel = ParseTime(v),
            Normalize = v => TimeText(ParseTime(v)) },
        new() { Header = "Note",
            ToText = c => c.Note ?? "", FromText = (c, v) => c.Note = Blank(v) },
        new() { Header = "Bloque",
            ToText = c => BoolText(c.Bloque), FromText = (c, v) => c.Bloque = ParseBool(v),
            Normalize = v => BoolText(ParseBool(v)) },
        new() { Header = "Rappel RDV",
            ToText = c => BoolText(c.RappelRdv), FromText = (c, v) => c.RappelRdv = ParseBool(v),
            Normalize = v => BoolText(ParseBool(v)) },
        new() { Header = "SIRET",
            ToText = c => c.Siret ?? "", FromText = (c, v) => c.Siret = Blank(v) },
        new() { Header = "SIREN",
            ToText = c => c.Siren ?? "", FromText = (c, v) => c.Siren = Blank(v) },
        new() { Header = "Facturation Electronique",
            ToText = c => BoolText(c.FacturationElectronique), FromText = (c, v) => c.FacturationElectronique = ParseBool(v),
            Normalize = v => BoolText(ParseBool(v)) },
    };

    public byte[] Export(IEnumerable<FranceOptique> clients)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Clients");

        sheet.Cell(1, 1).Value = "Cle Opl";
        for (var i = 0; i < Columns.Count; i++)
        {
            sheet.Cell(1, i + 2).Value = Columns[i].Header;
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var c in clients)
        {
            sheet.Cell(row, 1).Value = c.CleOpl;
            for (var i = 0; i < Columns.Count; i++)
            {
                sheet.Cell(row, i + 2).Value = Columns[i].ToText(c);
            }
            row++;
        }

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Diff-based upsert by Cle_Opl: a field is only written if the file's
    // value actually differs from what's currently in the DB (blank cell
    // means "set it blank", not "leave unchanged" — that skip-on-blank
    // convention belongs to the *other* legacy tool, FEN_Modification_
    // Depuis_Excel, not the one chosen here). The 7 reference-backed
    // columns (Famille, Franchise x4, Pays, Assistante Commerciale) are
    // validated against their real tables — an unrecognized value skips
    // *only* that field for that row (every other field on the row still
    // gets written normally), tallied per reference group rather than
    // silently written or rejecting the whole row.
    public async Task<ImportResult> ImportAsync(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);
        var sheet = workbook.Worksheets.First();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.FranceOptiques.ToDictionaryAsync(c => c.CleOpl);

        var validByGroup = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Famille"] = (await db.TypeFamilles.Select(f => f.NomFamille).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["Franchise"] = (await db.TypeFranchises.Select(f => f.NomFranchise).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["Pays"] = (await db.Pays.Select(p => p.NomPays).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["Assistante"] = (await db.Assistantes.Select(a => a.PrenomNom).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

        int added = 0, modified = 0, unchanged = 0;
        var invalidCounts = new Dictionary<string, int> { ["Famille"] = 0, ["Franchise"] = 0, ["Pays"] = 0, ["Assistante"] = 0 };
        var skipped = new List<string>();

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var row = sheet.Row(rowNum);
            if (row.IsEmpty()) continue;

            var cleOplText = row.Cell(1).GetString().Trim();
            if (!long.TryParse(cleOplText, out var cleOpl) || cleOpl <= 0)
            {
                skipped.Add($"Ligne {rowNum} : Cle Opl manquant ou invalide (\"{cleOplText}\")");
                continue;
            }

            var isNew = !existing.TryGetValue(cleOpl, out var entity);
            if (isNew)
            {
                entity = new FranceOptique { CleOpl = cleOpl };
            }

            var changed = false;
            for (var i = 0; i < Columns.Count; i++)
            {
                var col = Columns[i];
                var raw = row.Cell(i + 2).GetString().Trim();

                if (col.ReferenceGroup is not null && raw.Length > 0 && !validByGroup[col.ReferenceGroup].Contains(raw))
                {
                    invalidCounts[col.ReferenceGroup]++;
                    continue;
                }

                if (col.ToText(entity!) != col.Normalize(raw))
                {
                    col.FromText(entity!, raw);
                    changed = true;
                }
            }

            if (isNew)
            {
                db.FranceOptiques.Add(entity!);
                existing[cleOpl] = entity!;
                added++;
            }
            else if (changed)
            {
                modified++;
            }
            else
            {
                unchanged++;
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsForeignKeyViolation(ex, out _))
        {
            throw new InvalidOperationException(
                "Une valeur du fichier ne correspond à aucune référence existante (Pays, Famille…).");
        }

        return new ImportResult(added, modified, unchanged, skipped, invalidCounts);
    }
}
