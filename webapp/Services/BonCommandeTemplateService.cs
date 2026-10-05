using ClosedXML.Excel;
using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

public enum BonCommandeTemplateType
{
    Optique,
    Revendeur,
}

public class BonCommandeTemplateInfo
{
    public bool Exists { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? LastModifiedUtc { get; set; }
}

// Plain files on disk, not the database — confirmed with Badr (the
// historique.fic_stk precedent would've been the consistent choice, but he
// wants these on the filesystem instead). Lives outside wwwroot so the raw
// .xlsx templates are never web-reachable by URL; only server-side code
// touches this folder. Fixed, well-known filenames per type — an admin
// upload replaces the file in place, there's no history/versioning, same
// "one current value" shape as every other piece of reference config in
// this app (Pays, Familles, etc.), just file-shaped instead of row-shaped.
// "Last updated" is read straight off the file's own last-write time
// rather than a new DB column — nothing else needs to query it.
public class BonCommandeTemplateService
{
    private readonly string _folder;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public BonCommandeTemplateService(IWebHostEnvironment env, IDbContextFactory<AppDbContext> dbFactory)
    {
        _folder = Path.Combine(env.ContentRootPath, "AppData", "BonCommandeTemplates");
        Directory.CreateDirectory(_folder);
        _dbFactory = dbFactory;
    }

    private string PathFor(BonCommandeTemplateType type) => Path.Combine(_folder, FileNameFor(type));

    private static string FileNameFor(BonCommandeTemplateType type) => type switch
    {
        BonCommandeTemplateType.Optique => "modele_bc_optique.xlsx",
        BonCommandeTemplateType.Revendeur => "modele_bc_revendeur.xlsx",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public BonCommandeTemplateInfo GetInfo(BonCommandeTemplateType type)
    {
        var path = PathFor(type);
        var exists = File.Exists(path);
        return new BonCommandeTemplateInfo
        {
            Exists = exists,
            FileName = FileNameFor(type),
            LastModifiedUtc = exists ? File.GetLastWriteTimeUtc(path) : null,
        };
    }

    public async Task<byte[]?> GetBytesAsync(BonCommandeTemplateType type)
    {
        var path = PathFor(type);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path);
    }

    public async Task SaveAsync(BonCommandeTemplateType type, Stream content)
    {
        await using var fileStream = File.Create(PathFor(type));
        await content.CopyToAsync(fileStream);
    }

    // Real N° bon de commande = nextval() of a dedicated Postgres sequence,
    // same pattern as france_optique_cle_opl_seq (see
    // 03_cle_opl_sequence.sql) — atomic, no risk of two commerçants
    // generating a BC at the same moment and getting the same number.
    public async Task<long> GetNextBonCommandeNumberAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return (await db.Database
            .SqlQuery<long>($"SELECT nextval('bon_commande_number_seq')")
            .ToListAsync())[0];
    }

    // Shared by Coordonnées client (B12), Adresse de Facturation (F12), and
    // Adresse de Livraison (F19) — confirmed with Badr they're all the same
    // underlying address, not three different inputs. Order is Raison
    // Sociale, Rue, Localisation 1, CP+Ville (2026-09-04 — was CP+Ville
    // then Localisation 1 before; Badr wants Localisation 1 ahead of the
    // postal line). Localisation 1 is only inserted when it's actually
    // set, not as a blank line — same rule as before, just moved earlier
    // in the list.
    private static List<string> BuildAddressLines(FranceOptique client)
    {
        var lines = new List<string>
        {
            client.RaisonSociale ?? "",
            client.Rue ?? "",
        };
        if (!string.IsNullOrWhiteSpace(client.Localisation1))
        {
            lines.Add(client.Localisation1);
        }
        lines.Add($"{client.Cp} {client.Ville}".Trim());
        return lines;
    }

    // Layout confirmed cell-by-cell against the real templates Badr sent
    // (BC A COMPLETER.xlsx — the one with the 11 yellow fill-in cells,
    // despite its name suggesting otherwise; MODELE BC OPTIQUE 2026.xlsx is
    // the clean/blank copy with the same layout). C22 "Contact" was left
    // blank originally (Badr hadn't decided its content yet) — resolved
    // 2026-09-04: Contact = Responsable Achats. L6 remains deliberately
    // untouched, still undecided. The big title (merged C1:L4) used to get
    // the franchise too — Badr moved that to E6 instead and said the title
    // itself "should stay like that", so C1 is no longer touched at all.
    public async Task<(byte[] Bytes, long BcNumber)> FillAsync(byte[] templateBytes, FranceOptique client, BonCommandeTemplateType type)
    {
        var bcNumber = await GetNextBonCommandeNumberAsync();

        using var input = new MemoryStream(templateBytes);
        using var workbook = new XLWorkbook(input);
        var sheet = workbook.Worksheets.First();

        if (type == BonCommandeTemplateType.Optique)
        {
            FillOptique(sheet, client, bcNumber);
        }
        else
        {
            FillRevendeur(sheet, client, bcNumber);
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return (output.ToArray(), bcNumber);
    }

    private static void FillOptique(IXLWorksheet sheet, FranceOptique client, long bcNumber)
    {
        sheet.Cell("E6").Value = client.Franchise ?? "";
        // Both cells inherit "#,##0" (thousands-separator) from the raw
        // template — fine for a quantity, wrong for an ID/reference number
        // (183861 rendering as "183,861"). Overridden to a plain integer
        // format, no grouping.
        sheet.Cell("H6").Value = client.CleOpl;
        sheet.Cell("H6").Style.NumberFormat.Format = "0";
        sheet.Cell("B10").Value = bcNumber;
        sheet.Cell("B10").Style.NumberFormat.Format = "0";
        sheet.Cell("C10").Value = DateTime.Today;
        sheet.Cell("D10").Value = client.AssistanteCommercial ?? "";

        // Coordonnées client (merged B12:E21) — plain address, no TVA line.
        sheet.Cell("B12").Value = string.Join("\n", BuildAddressLines(client));

        // Adresse de Facturation (merged F12:L17) — same address, plus a
        // blank line then "TVA: ..." — but only when there's a TVA value to
        // show; omitted entirely (not left as a dangling blank line + empty
        // label) when the client has none set, same rule as Localisation 1.
        var facturationLines = BuildAddressLines(client);
        if (!string.IsNullOrWhiteSpace(client.Tva))
        {
            facturationLines.Add("");
            facturationLines.Add($"TVA: {client.Tva}");
        }
        sheet.Cell("F12").Value = string.Join("\n", facturationLines);

        // Adresse de Livraison (merged F19:L24) — same address, no TVA line.
        sheet.Cell("F19").Value = string.Join("\n", BuildAddressLines(client));

        sheet.Cell("C22").Value = client.ResponsableAchat ?? "";
        sheet.Cell("C23").Value = client.Telephone ?? "";
        sheet.Cell("C24").Value = client.Email ?? "";

        // Contact/Tel/Email (C22-C24) are plain single cells, left-aligned
        // flush against the cell border by default — Badr: make the text
        // "fully visible and not stuck to the edges". D22:D24/E22:E24 are
        // empty in the template, so Excel already lets overflow text spill
        // rightward on its own (confirmed against the real template before
        // relying on it) — the only real gap was the cramped left edge, so
        // a small indent is the actual fix, not a wrap/column-width change.
        foreach (var coord in new[] { "C22", "C23", "C24" })
        {
            sheet.Cell(coord).Style.Alignment.Indent = 1;
        }
    }

    // Layout confirmed cell-by-cell against MODELE BC REVENDEUR 2026.xlsx.
    // Different grid from Optique (no Franchise/TVA cells on this
    // template at all) but same rules Badr confirmed apply here too.
    // Contact (C25) resolved 2026-09-04, same as Optique's C22 — Responsable
    // Achats. Fields with no FranceOptique equivalent (Identifiant "Nouveau
    // client" I7, "Date de Validation" F11, "Date de Départ Usine" I11,
    // "E-mail Facturation" C28) and the PRODUITS COMMANDES / print-production
    // sections (rows 29-102) are still left untouched — same "not decided
    // yet, don't guess" treatment as Optique's L6.
    private static void FillRevendeur(IXLWorksheet sheet, FranceOptique client, long bcNumber)
    {
        sheet.Cell("H7").Value = client.CleOpl;
        sheet.Cell("H7").Style.NumberFormat.Format = "0";
        sheet.Cell("B11").Value = bcNumber;
        sheet.Cell("B11").Style.NumberFormat.Format = "0";
        sheet.Cell("C11").Value = DateTime.Today;
        sheet.Cell("D11").Value = client.AssistanteCommercial ?? "";

        // Coordonnées client (merged B13:E24) — plain address, no TVA line
        // (this template has no TVA cell at all).
        sheet.Cell("B13").Value = string.Join("\n", BuildAddressLines(client));

        // Adresse de Facturation (merged F13:L20) — same address, no TVA
        // line (no TVA field on this template to append).
        sheet.Cell("F13").Value = string.Join("\n", BuildAddressLines(client));

        // Adresse de Livraison (merged F22:L28) — same address.
        sheet.Cell("F22").Value = string.Join("\n", BuildAddressLines(client));

        sheet.Cell("C25").Value = client.ResponsableAchat ?? "";
        sheet.Cell("C26").Value = client.Telephone ?? "";
        sheet.Cell("C27").Value = client.Email ?? "";

        // Same "not stuck to the edges" indent fix as Optique's C22-C24.
        foreach (var coord in new[] { "C25", "C26", "C27" })
        {
            sheet.Cell(coord).Style.Alignment.Indent = 1;
        }
    }
}
