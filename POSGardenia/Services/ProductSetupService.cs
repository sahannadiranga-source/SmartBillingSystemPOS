using Microsoft.Data.Sqlite;
using POSGardenia.Data;
using POSGardenia.Models;
using System;

namespace POSGardenia.Services
{
    // Products and their "main item" (stock item). Products that share a main item share one stock:
    // a 750 ml bottle and its 100 / 50 / 25 ml shots all draw from the one "Arrack" main item (in ml).
    // A product and its main item are saved in ONE transaction, so a half-saved product can never
    // be left behind. Quantities are added on Inventory > Daily Stock, not here.
    // (The kitchen flag is not set here: a product takes it from its category.
    //  The bottle / pack size is set on Inventory > Daily Stock, not here, and saving a product never changes it.)
    public class ProductSetupService
    {
        private readonly StockService _stockService = new();
        private readonly StockItemRepository _stockItemRepository = new();
        private readonly ProductRepository _productRepository = new();

        // Creates a product linked to a main item (created if it does not exist yet, joined if it does).
        // A sub product such as "Arrack 50ml" -> main item "Arrack", 50 per sale.
        public void CreateTrackedProduct(
            string name, int categoryId, decimal price,
            string mainItemName, string unit, decimal unitsPerSale)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new Exception("Enter product name.");
            if (price < 0)
                throw new Exception("Prices cannot be negative.");
            if (unitsPerSale <= 0)
                throw new Exception("Stock used per sale must be greater than zero.");
            if (_productRepository.GetByName(name) != null)
                throw new Exception($"A product named '{name}' already exists.");

            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            int stockItemId = ResolveMainItem(connection, transaction, mainItemName, unit, categoryId);

            _productRepository.Add(connection, transaction, new Product
            {
                Name = name,
                CategoryId = categoryId,
                SellingPrice = price,
                IsActive = true,
                StockItemId = stockItemId,
                UnitsPerSale = unitsPerSale
            });

            transaction.Commit();
        }

        // Saves an edited product together with its stock link. mainItemName == null means
        // "not tracked": the link is removed and sales no longer touch stock.
        public void UpdateProduct(
            Product product, string? mainItemName, string unit, decimal unitsPerSale)
        {
            using var connection = DatabaseHelper.GetConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (mainItemName == null)
            {
                product.StockItemId = null;
                product.UnitsPerSale = null;
            }
            else
            {
                if (unitsPerSale <= 0)
                    throw new Exception("Stock used per sale must be greater than zero.");

                product.StockItemId = ResolveMainItem(connection, transaction, mainItemName, unit, product.CategoryId);
                product.UnitsPerSale = unitsPerSale;
            }

            _productRepository.Update(connection, transaction, product);

            transaction.Commit();
        }

        // Finds the main item by name or creates it (with 0 stock). Joining an existing one requires
        // the same unit, otherwise quantities of different kinds would be mixed.
        private int ResolveMainItem(
            SqliteConnection connection, SqliteTransaction transaction,
            string mainItemName, string unit, int categoryId)
        {
            string name = mainItemName?.Trim() ?? "";
            if (name.Length == 0)
                throw new Exception("Enter the main item.");
            if (string.IsNullOrWhiteSpace(unit))
                throw new Exception("Enter the stock unit (for example ml, bottle, unit).");

            var existing = _stockItemRepository.GetByName(connection, transaction, name);
            if (existing != null)
            {
                if (!string.Equals(existing.TrackingUnit, unit.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Main item '{existing.Name}' is counted in {existing.TrackingUnit}, not {unit.Trim()}.");

                return existing.Id;
            }

            return _stockService.CreateStockItem(
                connection, transaction,
                new StockItem { Name = name, CategoryId = categoryId, TrackingUnit = unit.Trim() },
                0, DateTime.Today);
        }
    }
}
