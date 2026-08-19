using Npgsql;

namespace MafaliCrm.Web.Services;

// Central place to turn a caught exception into something a non-technical
// staff member can actually read — in French, never raw EF Core/Postgres/
// .NET text. Every Razor page's catch blocks route through ToUserMessage;
// services that need a more specific message (e.g. "Cette famille existe
// déjà." instead of the generic "Cette valeur existe déjà.") still catch
// that one case explicitly and throw a plain InvalidOperationException —
// ToUserMessage passes those through unchanged, since they're already
// friendly French written by us. Also replaces what used to be five
// near-identical private IsUniqueViolation copies (one per lookup-table
// service) and two different-shaped FK-violation checks (ClientService's
// TryGetForeignKeyViolation, HistoriqueService's IsForeignKeyViolation).
public static class FriendlyError
{
    public static bool IsUniqueViolation(Exception ex) =>
        ex is PostgresException { SqlState: "23505" } ||
        ex.InnerException is PostgresException { SqlState: "23505" };

    public static bool IsForeignKeyViolation(Exception ex, out string? constraintName)
    {
        var pgEx = ex as PostgresException ?? ex.InnerException as PostgresException;
        if (pgEx is { SqlState: "23503" })
        {
            constraintName = pgEx.ConstraintName;
            return true;
        }
        constraintName = null;
        return false;
    }

    // Last-resort translation for whatever a page's catch block didn't
    // already turn into a specific message — the safety net that makes
    // sure nothing technical ever reaches the screen, even for exception
    // types nobody anticipated when writing a given catch block.
    public static string ToUserMessage(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        FormatException => "Le format saisi n'est pas reconnu. Vérifiez les valeurs saisies et réessayez.",
        OverflowException => "Une valeur saisie est trop grande ou invalide.",
        _ when IsUniqueViolation(ex) => "Cette valeur existe déjà.",
        _ when IsForeignKeyViolation(ex, out _) => "Cette valeur doit correspondre à une référence existante.",
        _ => "Une erreur inattendue s'est produite. Veuillez réessayer ou contacter le support si le problème persiste.",
    };
}
