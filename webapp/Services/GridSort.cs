namespace MafaliCrm.Web.Services;

// Click-to-sort state for every .header-row grid in the app — same blanket
// reach as GridLayout.cs (column widths) and mafaliColumnResize (column
// drag), just for ordering instead of layout. One instance per grid; a page
// with two grids (Filtres Prédéfinis, Parcours Client) owns two.
//
// Three-state per column, cycling on repeated clicks of the same header —
// ascending, then descending, then back to the grid's own natural/default
// order — matching Excel's own header-click cycle rather than a plain
// two-state toggle.
public class SortState
{
    public string? Column { get; private set; }
    public bool Descending { get; private set; }

    public void Toggle(string column)
    {
        if (Column != column)
        {
            Column = column;
            Descending = false;
        }
        else if (!Descending)
        {
            Descending = true;
        }
        else
        {
            Column = null;
            Descending = false;
        }
    }

    // " ▲"/" ▼" (leading space, not a CSS margin) so it flows as part of
    // the header label text itself and gets selected/measured the same way
    // as the rest of the label wherever that already happens (e.g. the
    // auto-fit width measurement in mafaliColumnResize.js reads the header
    // cell's actual rendered text).
    public string ArrowFor(string column) =>
        Column != column ? "" : Descending ? " ▼" : " ▲";

    public string CssClass(string column) =>
        Column == column ? "sortable sorted" : "sortable";
}

public static class GridSort
{
    // Sorts by each page's own display-string getter (the same method that
    // already renders each cell, e.g. Clients.razor's GetCellText) rather
    // than asking every page to also maintain a separate typed selector per
    // column. Correctness for the fields that would otherwise sort wrong as
    // plain text — numbers ("9" before "80"), dd/MM/yyyy dates, HH:mm
    // times — comes from trying those parses first; anything else falls
    // back to a case-insensitive ordinal string compare (matching this
    // app's existing case-insensitive search-bar behavior).
    public static List<T> Apply<T>(List<T> items, SortState state, Func<T, string, string> getText)
    {
        if (state.Column is null) return items;
        var column = state.Column;
        var ordered = items.OrderBy(item => getText(item, column), Comparer<string>.Create(CompareCells));
        return (state.Descending ? ordered.Reverse() : (IEnumerable<T>)ordered).ToList();
    }

    private static int CompareCells(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
            return na.CompareTo(nb);

        if (DateOnly.TryParseExact(a, "dd/MM/yyyy", out var da) && DateOnly.TryParseExact(b, "dd/MM/yyyy", out var db))
            return da.CompareTo(db);

        if (TimeOnly.TryParseExact(a, "HH:mm", out var ta) && TimeOnly.TryParseExact(b, "HH:mm", out var tb))
            return ta.CompareTo(tb);

        if (DateTime.TryParseExact(a, "dd/MM/yyyy HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dta) &&
            DateTime.TryParseExact(b, "dd/MM/yyyy HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out var dtb))
            return dta.CompareTo(dtb);

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
