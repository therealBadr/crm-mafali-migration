namespace MafaliCrm.Web.Services;

// Per-column header search — same blanket reach as SortState/GridSort
// (GridSort.cs), same "one instance per grid" ownership, deliberately built
// as a sibling to it rather than folded in: sorting and filtering are
// independent axes (Apply chains them — filter first, then GridSort.Apply
// on the result, same as Clients.razor's pre-existing toolbar search already
// did before this). Badr, 2026-09-22: "we can sort in both directions, but
// we cant search in that exact column like in excel" — confirmed via
// AskUserQuestion: a text (contains) search box per column, inline in the
// header next to the sort arrow (not a separate toolbar dropdown — Clients.
// razor already had one of those, kept as-is per Badr's call, this is
// additive there), on every grid that already has sortable columns, one
// column's filter active at a time.
//
// Column (the actively-applied filter) and OpenColumn (which column's input
// box is currently visible) are deliberately separate: closing the input
// box (click the icon again) hides the input but leaves the filter applied
// — same as Excel's own column filter dropdown, which keeps filtering after
// you close it. The icon itself carries a "filtered" visual state
// (CssClass) so a collapsed, still-active filter stays visible.
public class GridFilterState
{
    public string? Column { get; private set; }
    public string Query { get; private set; } = "";
    private string? OpenColumn;

    public bool IsOpen(string column) => OpenColumn == column;

    public string CssClass(string column) =>
        Column == column && Query.Length > 0 ? "filterable filtered" : "filterable";

    // Toggling a column's box open switches which column is being filtered
    // — only one at a time, so opening a different column's box drops
    // whatever was previously active rather than stacking.
    public void ToggleOpen(string column)
    {
        if (OpenColumn == column)
        {
            OpenColumn = null;
            return;
        }

        OpenColumn = column;
        if (Column != column)
        {
            Column = null;
            Query = "";
        }
    }

    public void SetQuery(string column, string text)
    {
        Column = text.Length > 0 ? column : null;
        Query = text;
    }

    // Escape: clear whatever was typed and close, distinct from just
    // closing (Enter, or clicking the icon again) which keeps the filter
    // applied — matches Excel's own Esc-cancels-the-edit behavior.
    public void Cancel(string column)
    {
        OpenColumn = null;
        if (Column == column)
        {
            Column = null;
            Query = "";
        }
    }
}

public static class GridFilter
{
    // Same getText contract as GridSort.Apply — each page's own cell-text
    // getter, so filtering matches exactly what's rendered (a French date
    // like "18/01/2019", not the underlying DateOnly), not a second,
    // possibly-inconsistent notion of a column's value.
    public static List<T> Apply<T>(List<T> items, GridFilterState state, Func<T, string, string> getText)
    {
        if (state.Column is null || state.Query.Length == 0) return items;
        var column = state.Column;
        var query = state.Query;
        return items.Where(item => getText(item, column).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
