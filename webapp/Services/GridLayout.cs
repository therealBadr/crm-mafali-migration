namespace MafaliCrm.Web.Services;

// Every .header-row/.row grid in this app (Clients, Rappels, Recherche
// Client, Fichier Historique, Définir un Filtre's results pane, Filtres
// Prédéfinis, Parcours Client's history grid) lays out columns with a
// fixed-width wrapper div sized to the sum of column widths, so a
// horizontal scrollbar appears instead of columns reflowing when the
// window is narrower than the table — deliberate, since these are
// Excel-style resizable-column grids. The bug this fixes: every cell used
// `flex: 0 0 Npx` (never grows), so on any screen wider than the column
// total, the grid stopped short of the right edge with a blank gap
// instead of filling the available width. Fixed once, centrally, instead
// of per-page: the wrapper uses `width: 100%; min-width: TotalWidthpx`
// (fills when there's room, scrolls when there isn't) and the last
// column gets `flex: 1 1 Npx` (grows from its width as a starting point)
// while every other column keeps `flex: 0 0 Npx`.
public static class GridLayout
{
    public static string WrapperStyle(int totalWidth) => $"width: 100%; min-width: {totalWidth}px;";

    public static string ColStyle(int width, bool isLast) =>
        isLast ? $"flex: 1 1 {width}px;" : $"flex: 0 0 {width}px;";
}
