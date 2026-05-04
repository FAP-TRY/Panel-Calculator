using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;

namespace PanelCalculator.Data.Repositories;

public interface IEstimationRepository : IRepository<Estimation>
{
    Task<Estimation?> GetByEstimationNumberAsync(string estimationNumber);
    Task<IEnumerable<Estimation>> GetByStatusAsync(string status);
    Task<IEnumerable<Estimation>> GetByClientNameAsync(string clientName);
    Task<IEnumerable<Estimation>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<IEnumerable<Estimation>> GetAllWithDetailsAsync();
}

public class EstimationRepository : BaseRepository<Estimation>, IEstimationRepository
{
    public EstimationRepository(PanelCalculatorContext context) : base(context)
    {
    }

    public async Task<Estimation?> GetByEstimationNumberAsync(string estimationNumber)
    {
        return await _context.Estimations
            .Include(e => e.Details)
            .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(e => e.EstimationNumber == estimationNumber);
    }

    public async Task<IEnumerable<Estimation>> GetByStatusAsync(string status)
    {
        return await _context.Estimations
            .Where(e => e.Status == status)
            .Include(e => e.Details)
            .OrderByDescending(e => e.CreatedDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Estimation>> GetByClientNameAsync(string clientName)
    {
        return await _context.Estimations
            .Where(e => e.ClientName.Contains(clientName))
            .Include(e => e.Details)
            .OrderByDescending(e => e.CreatedDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Estimation>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.Estimations
            .Where(e => e.CreatedDate >= startDate && e.CreatedDate <= endDate)
            .Include(e => e.Details)
            .OrderByDescending(e => e.CreatedDate)
            .ToListAsync();
    }

    public async Task<IEnumerable<Estimation>> GetAllWithDetailsAsync()
    {
        return await _context.Estimations
            .Include(e => e.Details)
            .ThenInclude(d => d.Product)
            .OrderByDescending(e => e.CreatedDate)
            .ToListAsync();
    }
}
