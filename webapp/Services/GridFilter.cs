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
// additive there), on every grid that already has sortable columns.
//
// Originally one column's filter active at a time (closing the box left it
// applied, but opening a different column's box dropped whatever was
// there). Badr, 2026-09-28: "filter again that result with another
// column" — confirmed via AskUserQuestion: any number of columns can each
// hold their own active filter simultaneously, a row must match ALL of
// them (AND, not OR — distinct from the Franchise-boxes task done just
// before this, which was OR; don't assume the two share semantics), plus a
// single "clear all" action. Rebuilt around a column -> query dictionary
// instead of one ambient (Column, Query) pair.
//
// A separate _labels dict (column -> the human-readable header text, e.g.
// "Ville") was added right after, backing GridFilterClearAll's chip row —
// Badr found the original single "Effacer les filtres (N)" link
// undiscoverable ("I didn't find it") and asked for Excel/Airtable-style
// removable chips naming each active filter instead. Labels live here
// rather than being re-derived from Column (which is a snake_case DB key,
// not display text) because ColumnFilterIcon already sits right next to
// each column's real label in the markup — easiest to just pass it down
// once instead of maintaining a second column-key -> label map somewhere.
public class GridFilterState
{
    private readonly Dictionary<string, string> _queries = new();
    private readonly Dictionary<string, string> _labels = new();
    private string? OpenColumn;

    public bool IsOpen(string column) => OpenColumn == column;

    public bool HasAny => _queries.Count > 0;

    public int ActiveCount => _queries.Count;

    public IReadOnlyDictionary<string, string> ActiveFilters => _queries;

    // Ordered by insertion so chips don't reshuffle as filters are added —
    // Dictionary's enumeration order matches insertion order in practice
    // (no entries ever removed-then-reinserted ahead of others here) but
    // that's not a documented guarantee, so this is read, not relied on,
    // for anything beyond "stable enough for a UI chip row".
    public IEnumerable<(string Column, string Label, string Query)> ActiveChips =>
        _queries.Select(kv => (kv.Key, _labels.TryGetValue(kv.Key, out var l) ? l : kv.Key, kv.Value));

    public string QueryFor(string column) => _queries.TryGetValue(column, out var q) ? q : "";

    public string CssClass(string column) =>
        _queries.ContainsKey(column) ? "filterable filtered" : "filterable";

    // Toggling a column's box open no longer touches any other column's
    // already-applied filter — only ever affects which box is currently
    // visible.
    public void ToggleOpen(string column) => OpenColumn = OpenColumn == column ? null : column;

    public void SetQuery(string column, string text, string label)
    {
        if (text.Length > 0)
        {
            _queries[column] = text;
            _labels[column] = label;
        }
        else
        {
            _queries.Remove(column);
            _labels.Remove(column);
        }
    }

    // Escape: clear whatever was typed for THIS column and close, distinct
    // from just closing (Enter, or clicking the icon again) which keeps
    // the filter applied — matches Excel's own Esc-cancels-the-edit
    // behavior. Other columns' active filters are untouched.
    public void Cancel(string column)
    {
        OpenColumn = null;
        _queries.Remove(column);
        _labels.Remove(column);
    }

    // A chip's own ✕ — removes just that column's filter without touching
    // any other active column, same end state as Cancel but reachable
    // without the column's input box being open at all.
    public void RemoveColumn(string column)
    {
        _queries.Remove(column);
        _labels.Remove(column);
        if (OpenColumn == column) OpenColumn = null;
    }

    public void ClearAll()
    {
        _queries.Clear();
        _labels.Clear();
        OpenColumn = null;
    }
}

public static class GridFilter
{
    // Same getText contract as GridSort.Apply — each page's own cell-text
    // getter, so filtering matches exactly what's rendered (a French date
    // like "18/01/2019", not the underlying DateOnly), not a second,
    // possibly-inconsistent notion of a column's value. Every active
    // column filter must match (AND) — a row survives only if it matches
    // every column currently filtered, narrowing further with each one
    // set rather than replacing the previous column's filter.
    public static List<T> Apply<T>(List<T> items, GridFilterState state, Func<T, string, string> getText)
    {
        if (!state.HasAny) return items;
        var active = state.ActiveFilters;
        return items.Where(item =>
            active.All(kv => getText(item, kv.Key).Contains(kv.Value, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }
}
