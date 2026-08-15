# Aurora Supply Co. Product Catalog Guide

Owner: Category Management. For sales, support, and operations staff.

## Catalog Structure

The catalog is organized into five categories: Power Tools, Electronics, Safety Equipment,
Hand Tools, and Consumables. Every product belongs to exactly one category, which determines its
warranty period, margin floor, and replenishment behavior.

## SKU Format

SKUs follow the pattern `XX-NNNN`, where the two-letter prefix identifies the category and the
four digits are assigned sequentially within that category.

| Prefix | Category |
|--------|----------|
| PT | Power Tools |
| EL | Electronics |
| SE | Safety Equipment |
| HT | Hand Tools |
| CN | Consumables |

SKUs are never reused. When a product is discontinued and later reintroduced with changed
specifications, it receives a new SKU.

## Warehouse Network

Aurora operates three warehouses:

- **WH-EU-01** — Rotterdam. Serves EMEA. Largest facility, holds the full catalog.
- **WH-NA-01** — Columbus, Ohio. Serves AMER. Holds Power Tools, Hand Tools, and Consumables.
- **WH-AP-01** — Singapore. Serves APAC. Holds Electronics and Safety Equipment.

Products stocked in more than one warehouse have an independent reorder threshold per warehouse,
because demand and lead times differ by region.

## Sales Regions and Channels

Sales are recorded against three regions — EMEA, AMER, and APAC — and three channels: Direct,
Distributor, and Online. Region is determined by ship-to address, not by the customer's
registered office, which matters for warranty and returns handling.

## Product Lifecycle

Products move through four states: Introduced, Active, Slow-Moving, and Discontinued. Only Active
products participate in automatic replenishment. Slow-moving products, defined as no unit sales in
ninety days, are reviewed each quarter for discontinuation or promotion.

Discontinuation requires agreement between Category Management and Commercial Finance, and takes
effect at the start of a quarter so that customers get a clear end-of-life date.
