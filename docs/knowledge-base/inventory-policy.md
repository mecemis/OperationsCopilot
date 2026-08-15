# Aurora Supply Co. Inventory and Replenishment Policy

Effective 1 January 2026. Owner: Operations Directorate. Review cadence: quarterly.

## Reorder Thresholds

Every product carries a per-warehouse reorder threshold. Stock is considered **low** when
quantity on hand is at or below that threshold, and **critical** when it is at or below half
the threshold.

Thresholds are recalculated on the first business day of each quarter using the formula:

    reorder threshold = (average daily units sold over the trailing 90 days x supplier lead time in days) + safety stock

Safety stock is fourteen days of average demand for Tier 1 suppliers and twenty-eight days for
Tier 2 and Tier 3 suppliers. Category managers may raise a threshold at any time, but lowering a
threshold requires sign-off from the Operations Director.

## Replenishment Triggers

When a product falls to low stock, the system raises a replenishment task assigned to the
category manager. Tasks must be actioned within two business days. When a product falls to
critical stock, the task escalates immediately to the Operations Director and the assigned
supplier account manager.

Purchase orders should bring stock back to the reorder threshold plus one full lead-time cycle
of demand, not merely back to the threshold. Ordering only to the threshold is the single most
common cause of repeat stockouts.

## Stockout Handling

A stockout is any product with zero quantity on hand in a warehouse that normally carries it.
On stockout:

1. Mark the product as unavailable in the affected region within one hour.
2. Check sister warehouses for transferable stock before raising a new purchase order.
3. Notify customers with open backorders within one business day, including a revised date.
4. Log the stockout in the weekly operations review with root cause.

Inter-warehouse transfers are preferred over expedited supplier orders when the receiving
warehouse can be served within three days, because transfer cost averages 40% of expedite cost.

## Slow-Moving and Discontinued Stock

Stock that has not sold a single unit in ninety days is classified as slow-moving and is excluded
from automatic replenishment. Discontinued products are never replenished; remaining stock is sold
down and any residual after two quarters is written off against the obsolescence provision.

Discontinued products remain visible in the catalog with an explicit end-of-life flag so that
support and sales staff can still look up specifications for units already in the field.

## Cycle Counting

Each warehouse performs rolling cycle counts so that every SKU is counted at least once per
quarter. High-value SKUs, defined as unit price above 500, are counted monthly. A count variance
above 2% of on-hand units triggers a recount before the ledger is adjusted.

The last counted date on an inventory record is the authoritative signal of data freshness. Any
stock figure last counted more than ninety days ago should be treated as an estimate and flagged
as such when reported to customers or finance.
