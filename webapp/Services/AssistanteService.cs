using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MafaliCrm.Web.Services;

// Correction from what the Familles/Pays comments originally claimed: this
// was tried first with plain mutate-tracked-entity-then-SaveChanges, on the
// theory that it'd work here since Assistante (unlike TypeFamille/Pays) has
// zero relationships pointing at it. It didn't — same "part of a key and so
// cannot be modified" error. So the real rule is simpler and more absolute
// than "protects relationship integrity": EF Core's change tracker never
// allows mutating a primary key property through the normal tracked-entity
// path, full stop, relationships or not. ExecuteUpdateAsync (a direct SQL
// UPDATE, bypassing the change tracker) is the fix every time the key value
// itself can change.
//
// Also uses IDbContextFactory, not an injected AppDbContext — see
// ClientService for why: a scoped AppDbContext lives for the whole Blazor
// Server session, so stale tracked state from a failed operation can
// corrupt a later unrelated one. A fresh context per method call avoids it.
public class AssistanteService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public AssistanteService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Assistante>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Assistantes
            .OrderBy(a => a.PrenomNom)
            .ToListAsync();
    }

    public async Task CreateAsync(string prenomNom, string? service, bool allFiltres, string? commentaire)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Assistantes.Add(new Assistante
        {
            PrenomNom = prenomNom,
            Service = service,
            AllFiltres = allFiltres,
            Commentaire = commentaire,
        });
        await SaveOrThrowFriendly(db);
    }

    public async Task UpdateAsync(string currentPrenomNom, string newPrenomNom, string? service, bool allFiltres, string? commentaire)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        int affected;
        try
        {
            affected = await db.Assistantes
                .Where(a => a.PrenomNom == currentPrenomNom)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(a => a.PrenomNom, newPrenomNom)
                    .SetProperty(a => a.Service, service)
                    .SetProperty(a => a.AllFiltres, allFiltres)
                    .SetProperty(a => a.Commentaire, commentaire));
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette assistante existe déjà.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Assistante introuvable.");
        }
    }

    public async Task DeleteAsync(string prenomNom)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.Assistantes.FindAsync(prenomNom);
        if (entity is null) return;
        db.Assistantes.Remove(entity);
        await db.SaveChangesAsync();
    }

    private static async Task SaveOrThrowFriendly(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette assistante existe déjà.");
        }
    }

    private static bool IsUniqueViolation(Exception ex) =>
        ex is PostgresException { SqlState: "23505" } ||
        ex.InnerException is PostgresException { SqlState: "23505" };
}
