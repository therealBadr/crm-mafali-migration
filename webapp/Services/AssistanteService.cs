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
public class AssistanteService
{
    private readonly AppDbContext _db;

    public AssistanteService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<Assistante>> ListAsync()
    {
        return await _db.Assistantes
            .OrderBy(a => a.PrenomNom)
            .ToListAsync();
    }

    public async Task CreateAsync(string prenomNom, string? service, bool allFiltres, string? commentaire)
    {
        _db.Assistantes.Add(new Assistante
        {
            PrenomNom = prenomNom,
            Service = service,
            AllFiltres = allFiltres,
            Commentaire = commentaire,
        });
        await SaveOrThrowFriendly();
    }

    public async Task UpdateAsync(string currentPrenomNom, string newPrenomNom, string? service, bool allFiltres, string? commentaire)
    {
        int affected;
        try
        {
            affected = await _db.Assistantes
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
        var entity = await _db.Assistantes.FindAsync(prenomNom);
        if (entity is null) return;
        _db.Assistantes.Remove(entity);
        await _db.SaveChangesAsync();
    }

    private async Task SaveOrThrowFriendly()
    {
        try
        {
            await _db.SaveChangesAsync();
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
