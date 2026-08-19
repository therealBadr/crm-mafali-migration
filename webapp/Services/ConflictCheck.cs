using System.Reflection;

namespace MafaliCrm.Web.Services;

public class FieldConflict
{
    public string Field { get; set; } = string.Empty;
    public string MyValue { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
}

public class ApplyResult
{
    public bool Success { get; set; }
    public List<FieldConflict> Conflicts { get; set; } = new();
}

// Shared three-way merge used by every "load once, edit over time, save
// later" screen in this app. `baseline` is what the caller started from,
// `current` is what they're trying to save, `merged` starts as a fresh copy
// of the real current row and gets mutated in place with the result: a
// field only moves from `merged` to `current`'s value if the caller
// actually touched it (baseline != current) AND nobody else changed that
// same field since baseline (baseline == merged), unless force is set.
// Untouched fields are left as whatever `merged` already had — the real
// current value, not the caller's possibly-stale copy of it.
public static class ConflictCheck
{
    public static string FormatForDisplay(object? value) => value switch
    {
        null => "(vide)",
        string s when string.IsNullOrEmpty(s) => "(vide)",
        bool b => b ? "Oui" : "Non",
        _ => value.ToString() ?? "(vide)",
    };

    public static List<FieldConflict> Apply<T>(
        T baseline, T current, T merged, bool force,
        IReadOnlyDictionary<string, string> fieldLabels,
        HashSet<string>? exclude = null)
    {
        exclude ??= new HashSet<string>();
        var conflicts = new List<FieldConflict>();
        foreach (var prop in typeof(T).GetProperties())
        {
            var baselineVal = prop.GetValue(baseline);
            var currentVal = prop.GetValue(current);
            if (Equals(baselineVal, currentVal)) continue; // not touched — keep merged's fresh value

            var freshVal = prop.GetValue(merged);
            if (!exclude.Contains(prop.Name) && !Equals(baselineVal, freshVal) && !force)
            {
                conflicts.Add(new FieldConflict
                {
                    Field = fieldLabels.TryGetValue(prop.Name, out var label) ? label : prop.Name,
                    MyValue = FormatForDisplay(currentVal),
                    CurrentValue = FormatForDisplay(freshVal),
                });
                continue;
            }
            prop.SetValue(merged, currentVal);
        }
        return conflicts;
    }
}
