using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;

namespace PanelCalculator.Data.Repositories;

public interface IProductRepository : IRepository<Product>
{
    Task<IEnumerable<Product>> GetByCategoryAsync(string category);
    Task<Product?> GetByReferenceCodeAsync(string referenceCode);
    Task<IEnumerable<string>> GetAllCategoriesAsync();
    Task<IEnumerable<Product>> SearchAsync(string searchTerm);
}

public class ProductRepository : BaseRepository<Product>, IProductRepository
{
    public ProductRepository(PanelCalculatorContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Product>> GetByCategoryAsync(string category)
    {
        return await _context.Products
            .Where(p => p.Category == category)
            .OrderBy(p => p.ProductName)
            .ToListAsync();
    }

    public async Task<Product?> GetByReferenceCodeAsync(string referenceCode)
    {
        return await _context.Products
            .FirstOrDefaultAsync(p => p.ReferenceCode == referenceCode);
    }

    public async Task<IEnumerable<string>> GetAllCategoriesAsync()
    {
        return await _context.Products
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> SearchAsync(string searchTerm)
    {
        var term = searchTerm.ToLower();
        return await _context.Products
            .Where(p => p.ProductName.ToLower().Contains(term) ||
                        p.ReferenceCode.ToLower().Contains(term) ||
                        (p.Specifications != null && p.Specifications.ToLower().Contains(term)))
            .OrderBy(p => p.ProductName)
            .ToListAsync();
    }
}
