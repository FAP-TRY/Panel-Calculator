using Microsoft.EntityFrameworkCore;

namespace PanelCalculator.Data.Repositories;

public abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly PanelCalculatorContext _context;

    public BaseRepository(PanelCalculatorContext context)
    {
        _context = context;
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _context.Set<T>().ToListAsync();
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        return await _context.Set<T>().FindAsync(id);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        await _context.Set<T>().AddAsync(entity);
        await SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Updates only the scalar properties of <paramref name="entity"/>.
    /// Uses Entry().State instead of Set().Update() to avoid cascading
    /// Modified/Added state onto navigation properties (e.g. Products),
    /// which would trigger UNIQUE constraint violations on related tables.
    /// </summary>
    public virtual async Task<T> UpdateAsync(T entity)
    {
        var entry = _context.Entry(entity);

        // If already tracked by this context — just mark it modified so
        // EF Core's change-detection picks up the changed scalar properties.
        // If it arrived detached (e.g. from a different context scope) —
        // attach it and mark modified, still without touching navigation props.
        if (entry.State == EntityState.Detached)
            entry.State = EntityState.Modified;
        // else: entity is Unchanged/Modified — SaveChanges will flush it.

        await SaveChangesAsync();
        return entity;
    }

    public virtual async Task DeleteAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.Set<T>().Remove(entity);
            await SaveChangesAsync();
        }
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}
