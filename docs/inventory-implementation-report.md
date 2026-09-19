# Inventory in POSGardenia — Study Report and Implementation Plan

Branch: `inventry1` (based on `main` + the delete/partial-payment commit).
Source studied: `D:\gardenia\restaurant-stock-frontend` (React prototype + `.kiro/specs/requirements.md`) and the current POSGardenia code.

---

## 1. Summary in plain words

Your React stock app is a **daily stock sheet**. Every day, for every drink / cigarette / soft drink, someone types:

> Previous (yesterday's closing) − Sold (typed by hand) + New (stock received) = Today (closing)

"Copy Yesterday" carries closing stock into the next day's opening. Food has no stock, only a sold count. Products are always shown on every date. A day can be locked. A monthly page adds the days up.

The problem with that app is the **Sold number is typed by hand**, so it can disagree with what the till really sold.

In the POS we keep the same idea and the same screen, but **Sold comes from the bills automatically**. Nobody types it. What people still enter is only what the POS cannot know: **stock received** and **adjustments** (breakage, free drinks, count corrections).

So the whole implementation is:

1. Tell the POS which sold products use which stock (a peg of whiskey uses 25 ml of the whiskey bottle stock).
2. Every time an item goes on a bill, write a line in a **stock ledger** and reduce the stock.
3. Show a **Daily Stock** screen that reads the ledger: Opening, Received, Sold, Adjusted, Closing.
4. Add a **Close Day** button that freezes that day's closing, so tomorrow's opening is automatic (this replaces "Copy Yesterday").

---

## 2. What the React app does (and what we keep / change)

| React app feature | In the POS |
|---|---|
| 9 fixed categories, each with different fields | Keep the idea. A category gets a **type**: Liquor, Bottle, Cigarette, Simple. The type decides which fields the Add Product form shows. |
| Drinks tracked in **ml** (Beer in bottles); price per bottle and per 100 ml | Stock is kept in **ml**. Each serving (bottle, 100 ml, 50 ml, 25 ml) is its own sellable POS product that uses X ml of the same stock. 50 ml / 25 ml prices are calculated from the price per 100 ml. |
| Cigarettes: packs + loose | Stock is kept in **single cigarettes**; shown as packs + loose. "Pack" and "Single" are two sellable products on one stock. |
| Soft drinks: bottles | Stock in bottles, one sellable product. |
| Food: only sold count | No stock. Sold count already comes from bills (Item Sales report). |
| **Sold** typed by hand | **Automatic** from bills. |
| **New** typed by hand | **Receive Stock** entry (kept, this is real input). |
| Copy Yesterday | Removed as a button. Opening = yesterday's closing automatically. |
| Product sync (every product on every date) | Free: the daily sheet is a query over all stock items. |
| Negative stock highlighted, not blocked | Same (used for audit). |
| Lock day (Admin / Cashier / Manager) | **Close Day** per date. Roles come later (POS has no login yet). |
| Expenses table | Already exists in POS (Reports tab). Reused. |
| Monthly summary | New report reading the same data. |
| LocalStorage per date | SQLite tables (below). |

---

## 3. Where POS is today (facts checked in the code)

- WPF, .NET 9, one process, local SQLite (`%LocalAppData%\POSGardenia\posgardenia.db`). No API, no MVVM. Repositories in `Data/`, small services in `Services/`, everything else in `MainWindow.xaml(.cs)`.
- Tables: `Categories, Products, DiningTables, Bills, BillItems, Payments, Expenses`. `Products` has a name, category, one selling price, kitchen flag, active/deleted flags — **no stock at all**.
- Items reach a bill in **two places**: `BillItemRepository.Add` (adding to an open table bill) and `BillRepository.InsertBillItem` (inside the pay transaction). Any stock rule must cover both.
- Bills can now be partly paid and stay open; void is blocked once a payment exists; delete/deactivate of products, categories, tables is soft.
- Migration style: `ALTER TABLE … ADD COLUMN` wrapped in try/swallow, then `CREATE TABLE IF NOT EXISTS`.

---

## 4. Architecture

```
                 ┌────────────────────────── MainWindow (WPF) ──────────────────────────┐
                 │  POS tab   Tables tab   History   Reports   Management   INVENTORY    │
                 └───────────────┬───────────────────────────────────────────┬───────────┘
                                 │ (unchanged screens call as today)         │ new screens
                                 ▼                                           ▼
   BillRepository / BillItemRepository ──► StockService  ◄────────── Inventory tab handlers
   (bill create, add item, cancel, void)     (the only place that      (receive, adjust,
                                              changes stock)            daily sheet, close day)
                                                 │
                                                 ▼
        Data/  StockItemRepository  StockMovementRepository  DailyStockSnapshotRepository
                                                 │
                                                 ▼
                                   SQLite (same posgardenia.db)
```

Rules that keep it simple and safe:

- **One writer.** Only `StockService` writes stock. POS code just calls it.
- **Same transaction.** When an item is added to a bill, the stock movement is written in the *same* database transaction, so a bill and its stock can never disagree.
- **Ledger, not a counter.** Stock is never overwritten. Every change is a row in `StockMovements`; the current quantity is a cached running total that can be rebuilt from the ledger.
- **No API layer, no new project.** Same style as the rest of the app.

### Data model (new tables in the same database)

| Table | Purpose | Key columns |
|---|---|---|
| `StockItems` | The physical thing on the shelf (e.g. "Johnnie Walker Black") | Name, CategoryId, TrackingUnit (`ml` / `bottle` / `cigarette`), CurrentQuantity, LowStockThreshold, IsActive/IsDeleted |
| `StockMovements` | The ledger | StockItemId, MovementDate, Type (`Sale`, `Restock`, `Adjustment`, `Wastage`, `InitialStock`), QuantityChange (− out, + in), BillItemId (for sales), Note, CreatedAt |
| `DailyStockSnapshot` | Frozen closing per item per date | StockItemId, Date, Opening, Closing, IsLocked (unique per item+date) |
| `Products` (2 new columns) | Links what is sold to what is stocked | `StockItemId` (null = not tracked), `UnitsPerSale` (stock used per 1 sold) |
| `Categories` (1 new column) | Drives the Add Product form | `ProductType` (Simple / Liquor / Bottle / Cigarette) |

Why two tables (`StockItems` vs `Products`): one bottle of whiskey is sold as a peg, a 50 ml, a 100 ml and a whole bottle, all at different prices, but they all come out of **one** stock. Same for cigarette pack vs single.

Example — Whiskey, 750 ml bottle, 10 bottles received:

| Sellable product | Price | UnitsPerSale | Stock item |
|---|---|---|---|
| Whiskey (Bottle) | 8,000 | 750 | Whiskey (ml) |
| Whiskey (100 ml) | 1,200 | 100 | Whiskey (ml) |
| Whiskey (50 ml) | 600 (auto) | 50 | Whiskey (ml) |
| Whiskey (25 ml) | 300 (auto) | 25 | Whiskey (ml) |

Stock starts at 7,500 ml. Selling 2 × 50 ml writes one movement of −100 ml.

### The daily sheet is a calculation, not stored data

For a stock item on a date:

```
Opening   = closing of the last closed day before it (or derived from the ledger)
Received  = Restock + InitialStock movements that day
Sold      = −(Sale movements that day)          ← from bills
Adjusted  = Adjustment + Wastage movements that day
Closing   = Opening + Received − Sold + Adjusted
```

"Close Day" stores Opening/Closing in `DailyStockSnapshot` and marks it locked. Tomorrow's opening is that closing — this is Copy Yesterday, without a button.

---

## 5. Decisions I recommend (please confirm)

1. **When does stock go down — when the item is ordered or when the bill is paid?**
   *Recommended: when the item is added to the bill.* Drinks leave the shelf when served, not when paid, and with partial payments a table bill can stay open for hours. Cost: if a line is cancelled or a bill is voided, a reversing movement must be written. That is small because void is already blocked once a payment exists, and cancel-item is not on any button yet.
2. **Existing products.** Nothing changes for them. They stay "not tracked" until you link them to a stock item (opt-in per product). Your live data is not touched.
3. **Add Product by category type** (the Liquor / Cigarette / Bottle form you described earlier, including auto-created 50 ml / 25 ml items). Recommended yes — it is what makes setup fast and correct.
4. **Physical stock count.** Not in the React app. Optional later: enter the counted quantity, system writes an Adjustment for the difference and shows the variance. Recommended as a later step.
5. **Roles / login for locking.** POS has no users. Recommended: Close Day without roles first; add login and roles as a separate step.

---

## 6. Implementation process (phases — each can be tested on its own)

**Phase 1 — Foundation (no visible change)**
Add the three tables and the three new columns using the existing migration style. Models and repositories for stock items, movements, snapshots. `StockService` with the rules (record sale, reverse sale, receive, adjust, close day). Nothing calls it yet, so the POS behaves exactly as today.

**Phase 2 — Product setup**
Category "Product Type" dropdown on the Categories tab. Add Product form shows the right fields for the type; saving creates the stock item, the opening stock movement and all sellable products in one step. Editing an existing product can still link it manually to a stock item.

**Phase 3 — Connect the POS (the important one)**
Call `StockService` from the two bill-item insert paths, inside the same transaction. Write reversals on cancel-item and void. Products without a stock link are skipped, so untracked items keep working. Payment code is not touched.

**Phase 4 — Inventory tab**
Stock Items (list, low-stock flag, edit threshold) → Receive Stock / Adjust / Wastage (the "New" column) → **Daily Stock** sheet for any date with Close Day → movement history.

**Phase 5 — Reports**
Monthly summary (sales from payments, expenses, profit, closed days), low-stock and negative-stock warnings, stock value if a cost price is added later.

**Phase 6 — Later**
Physical count/variance, login + roles for Close Day, supplier / purchase records.

---

## 7. Risks and how they are handled

| Risk | Handling |
|---|---|
| Damaging live data while testing (this happened once in the earlier attempt) | Build and test against a **copy** of the database; never point test code at `%LocalAppData%` directly. |
| Bill and stock out of sync | Same DB transaction; ledger keeps every change traceable. |
| Two places insert bill items | Both call the same `StockService` method; covered in Phase 3 verification. |
| Rounding on 50/25 ml prices | Calculated from price per 100 ml and rounded to 2 decimals when the product is created. |
| WPF pitfall found earlier | Do not set `Text`/`TextChanged` in XAML on inputs that trigger handlers during startup; set defaults in code. |
| Negative stock | Allowed and highlighted (as in the React spec) so mistakes can be audited, never silently blocked. |

---

## 8. Definition of done (acceptance checks)

- Sell 2 × 50 ml whiskey at the till → stock drops 100 ml, Daily Stock shows Sold 100 ml.
- Cancel that line / void that bill → stock returns.
- Receive 5 bottles → Received 3,750 ml, closing correct.
- Close Day → next day's opening equals that closing, with no copy step.
- Product not linked to stock sells exactly as before.
- Cigarette pack + single sales produce the right packs + loose closing.
- Food never appears in stock; its sold count still shows in reports.

---

## Design changes made during implementation

These replace the matching parts of the plan above.

- **No low-stock level anywhere.** Not stored, not entered, not shown or flagged. (Negative stock is still flagged.)
- **Main item.** A stock item is a *main item*. Products that share a main item share one stock. It is set on the product (Management > Products > "Track stock" > Main item, Stock unit, Stock used per sale) and shown as the "Main Item" column in the Products grid. A sub product (e.g. "Arrack 50ml") picks the existing main item ("Arrack") and sets how much stock one sale uses (50).
- **Products are set up first, stock second.** Product forms no longer ask for opening stock. Stock is added on **Inventory > Daily Stock > Add stock entry** (Received / Adjust / Wastage); there is no separate opening-quantity screen.
- **Stock Items tab** lists main items (name, unit, in stock, linked products). There is no separate "new stock item" form and no rename/deactivate: main items are created from Products.
- **POS** lists products of the same main item together (bottle, 100 ml, 50 ml, 25 ml).
- **No category "product type".** The Liquor / Bottle / Cigarette types and their auto-created servings were removed. Every product uses the same form; sub products are added one by one, sharing a main item.
- **Kitchen flag moved to the category.** A category has a "Kitchen category" tick and all its products follow it (the product form no longer has a Kitchen Item box). Selecting a category loads it into the form and Save becomes Update Category (name and kitchen tick), like products. `Products.IsKitchenItem` is kept as a copy of the category's flag so the kitchen-ticket queries are unchanged. When the column was first added, a category became a kitchen category if any of its products was a kitchen item.
- **New categories and products are saved as active.** There is no Active tick box; use Deactivate / Reactivate Selected to change it. Editing a product never changes its active state.
- **Labels on every input** so each field says what it is for.
- The "Import Products" idea was tried and removed.
- **No Close Day / Reopen Day and no Stock History tab.** The business day simply ends at midnight: each date's closing is the next date's opening, calculated from the ledger. Entries can be added for any date; nothing is locked. The DailyStockSnapshot table is no longer created or used (an existing empty one is left alone).
