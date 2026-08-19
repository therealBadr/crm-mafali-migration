using MafaliCrm.Web.Data;
using MafaliCrm.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MafaliCrm.Web.Services;

// IDbContextFactory, not an injected AppDbContext — see ClientService for
// why: a scoped AppDbContext lives for the whole Blazor Server session, not
// one request, so stale tracked state from a failed operation can corrupt a
// later unrelated one. A fresh context per method call avoids that.
public class FranchiseService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public FranchiseService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<string>> ListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.TypeFranchises
            .OrderBy(f => f.NomFranchise)
            .Select(f => f.NomFranchise)
            .ToListAsync();
    }

    public async Task CreateAsync(string nomFranchise)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.TypeFranchises.Add(new TypeFranchise { NomFranchise = nomFranchise });
        await SaveOrThrowFriendly(db);
    }

    // Same change-tracker restriction as TypeFamille.NomFamille: EF Core
    // never allows mutating a primary key property via a tracked entity,
    // regardless of relationships (confirmed unconditional after hitting
    // the same error again on the zero-relationship Assistante entity).
    // ExecuteUpdateAsync from the start here rather than re-discovering it.
    public async Task RenameAsync(string currentName, string newName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        int affected;
        try
        {
            affected = await db.TypeFranchises
                .Where(f => f.NomFranchise == currentName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.NomFranchise, newName));
        }
        catch (Exception ex) when (FriendlyError.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette franchise existe déjà.");
        }

        if (affected == 0)
        {
            throw new InvalidOperationException("Franchise introuvable.");
        }
    }

    public async Task DeleteAsync(string nomFranchise)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.TypeFranchises.FindAsync(nomFranchise);
        if (entity is null) return;
        db.TypeFranchises.Remove(entity);
        await db.SaveChangesAsync();
    }

    private static async Task SaveOrThrowFriendly(AppDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex) when (FriendlyError.IsUniqueViolation(ex))
        {
            throw new InvalidOperationException("Cette franchise existe déjà.");
        }
    }
}
