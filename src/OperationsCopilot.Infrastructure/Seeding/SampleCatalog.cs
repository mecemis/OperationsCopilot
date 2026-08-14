namespace OperationsCopilot.Infrastructure.Seeding;

/// <summary>Static definition of one seeded product, before inventory and sales are generated.</summary>
/// <param name="DemandWeight">
/// Relative sales volume. Drives how many sales rows the generator produces for this product,
/// so the seeded data has genuine bestsellers and genuine slow movers rather than uniform noise.
/// </param>
internal sealed record SampleProduct(
    string Sku,
    string Name,
    string Category,
    string Description,
    decimal UnitPrice,
    string Supplier,
    double DemandWeight,
    bool IsDiscontinued = false);

/// <summary>Static definition of one warehouse.</summary>
internal sealed record SampleWarehouse(string Code, string Region, IReadOnlySet<string> Categories);

/// <summary>
/// The fixed shape of the demo dataset. Product lines, categories, suppliers, and warehouse
/// coverage all match the Markdown knowledge base, so questions that combine a policy with a
/// database lookup have consistent answers.
/// </summary>
internal static class SampleCatalog
{
    public static readonly IReadOnlyList<SampleWarehouse> Warehouses =
    [
        new("WH-EU-01", "EMEA", new HashSet<string>
        {
            Categories.PowerTools, Categories.Electronics, Categories.SafetyEquipment,
            Categories.HandTools, Categories.Consumables,
        }),
        new("WH-NA-01", "AMER", new HashSet<string>
        {
            Categories.PowerTools, Categories.HandTools, Categories.Consumables,
        }),
        new("WH-AP-01", "APAC", new HashSet<string>
        {
            Categories.Electronics, Categories.SafetyEquipment,
        }),
    ];

    public static class Categories
    {
        public const string PowerTools = "Power Tools";
        public const string Electronics = "Electronics";
        public const string SafetyEquipment = "Safety Equipment";
        public const string HandTools = "Hand Tools";
        public const string Consumables = "Consumables";
    }

    public static readonly IReadOnlyList<SampleProduct> Products =
    [
        // Power Tools — SKU prefix PT
        new("PT-1001", "Torqline 18V Brushless Drill", Categories.PowerTools,
            "Compact 18V brushless drill driver with a two-speed gearbox, 20-stage clutch and an LED work light. Ships with two 4.0Ah batteries and a fast charger.",
            249.00m, "Torqline Industrial", 1.00),
        new("PT-1002", "Torqline 18V Impact Driver", Categories.PowerTools,
            "Quarter-inch hex impact driver delivering 180Nm of torque, with three-mode speed control for fastening without stripping.",
            219.00m, "Torqline Industrial", 0.85),
        new("PT-1003", "Corvex 1200W Angle Grinder", Categories.PowerTools,
            "125mm angle grinder with restart protection, anti-vibration side handle and a tool-free guard adjustment.",
            179.50m, "Corvex Power", 0.60),
        new("PT-1004", "Corvex Reciprocating Saw", Categories.PowerTools,
            "Variable-speed reciprocating saw with tool-free blade change and an orbital action setting for fast rough cuts.",
            198.00m, "Corvex Power", 0.42),
        new("PT-1005", "Torqline Rotary Hammer SDS-Plus", Categories.PowerTools,
            "3.2 joule SDS-Plus rotary hammer with vibration control, suited to sustained drilling in reinforced concrete.",
            389.00m, "Torqline Industrial", 0.35),
        new("PT-1006", "Corvex 600W Detail Sander", Categories.PowerTools,
            "Palm sander with a hook-and-loop base and integrated dust extraction port. Superseded by the PT-1012 platform.",
            89.00m, "Corvex Power", 0.08, IsDiscontinued: true),

        // Electronics — SKU prefix EL
        new("EL-2001", "Voltek Digital Multimeter DM-600", Categories.Electronics,
            "True-RMS digital multimeter measuring voltage, current, resistance, capacitance and frequency, with CAT III 600V safety rating.",
            139.00m, "Voltek Instruments", 0.90),
        new("EL-2002", "Voltek Thermal Camera TC-120", Categories.Electronics,
            "Handheld 120x90 thermal imager with a -20C to 400C range, used for electrical inspection and building surveys.",
            899.00m, "Voltek Instruments", 0.30),
        new("EL-2003", "Voltek Clamp Meter CM-210", Categories.Electronics,
            "600A AC/DC clamp meter with inrush current capture and a backlit display for work in poorly lit plant rooms.",
            174.00m, "Voltek Instruments", 0.55),
        new("EL-2004", "Nordsen Cable Tester NT-40", Categories.Electronics,
            "Network and coax cable tester with wire-map, length measurement and a remote identifier set for structured cabling.",
            219.00m, "Nordsen Electronics", 0.38),
        new("EL-2005", "Nordsen Laser Distance Meter 60m", Categories.Electronics,
            "60 metre laser measure with area, volume and Pythagoras modes, plus Bluetooth export to site survey apps.",
            118.00m, "Nordsen Electronics", 0.72),

        // Safety Equipment — SKU prefix SE
        new("SE-3001", "Guardline Full Body Harness", Categories.SafetyEquipment,
            "Five-point fall arrest harness with a dorsal D-ring, adjustable leg straps and a quick-connect chest buckle. EN 361 certified.",
            164.00m, "Guardline Safety", 0.68),
        new("SE-3002", "Guardline Hard Hat Vented", Categories.SafetyEquipment,
            "Vented industrial safety helmet with a six-point suspension and integrated accessory slots for ear defenders and visors.",
            42.50m, "Guardline Safety", 1.10),
        new("SE-3003", "Guardline Cut-Resistant Gloves Level D", Categories.SafetyEquipment,
            "ANSI cut level A4 gloves with a nitrile palm coating, offering grip retention when handling oily sheet metal.",
            18.90m, "Guardline Safety", 1.40),
        new("SE-3004", "Aeroshield Respirator Half Mask", Categories.SafetyEquipment,
            "Reusable half-mask respirator with a bayonet filter mount, compatible with P3 particulate and A2 vapour cartridges.",
            76.00m, "Aeroshield Protective", 0.62),
        new("SE-3005", "Aeroshield Safety Goggles Anti-Fog", Categories.SafetyEquipment,
            "Indirect-vent goggles with an anti-fog, anti-scratch polycarbonate lens rated for impact and chemical splash.",
            27.50m, "Aeroshield Protective", 0.95),

        // Hand Tools — SKU prefix HT
        new("HT-4001", "Ironvale Combination Spanner Set 12pc", Categories.HandTools,
            "Twelve-piece chrome vanadium combination spanner set from 8mm to 22mm, in a roll-up canvas pouch.",
            89.00m, "Ironvale Tooling", 0.80),
        new("HT-4002", "Ironvale Ratchet Screwdriver", Categories.HandTools,
            "Ratcheting screwdriver with a magnetic quarter-inch bit holder and a ten-bit cartridge stored in the handle.",
            34.00m, "Ironvale Tooling", 1.20),
        new("HT-4003", "Ironvale Adjustable Wrench 250mm", Categories.HandTools,
            "Wide-opening adjustable wrench with a laser-etched metric scale and a slim head for confined access.",
            29.50m, "Ironvale Tooling", 0.90),
        new("HT-4004", "Bexley Precision Plier Set 5pc", Categories.HandTools,
            "Five-piece ESD-safe precision plier set for electronics work, with induction-hardened cutting edges.",
            67.00m, "Bexley Handworks", 0.45),
        new("HT-4005", "Bexley Claw Hammer 20oz", Categories.HandTools,
            "Twenty-ounce claw hammer with a fibreglass anti-shock shaft and a milled face for framing work.",
            38.00m, "Bexley Handworks", 0.58),

        // Consumables — SKU prefix CN
        new("CN-5001", "Corvex Cutting Disc 125mm (25 pack)", Categories.Consumables,
            "Twenty-five pack of 1mm reinforced cutting discs for stainless and mild steel, rated to 12,200 rpm.",
            44.00m, "Corvex Power", 1.60),
        new("CN-5002", "Torqline Impact Bit Set 32pc", Categories.Consumables,
            "Thirty-two piece impact-rated driver bit set covering Phillips, Pozidriv, Torx and hex profiles.",
            31.00m, "Torqline Industrial", 1.35),
        new("CN-5003", "Aeroshield P3 Filter Pair", Categories.Consumables,
            "Replacement P3 particulate filter pair for the Aeroshield half-mask respirator range.",
            22.00m, "Aeroshield Protective", 1.25),
        new("CN-5004", "Ironvale Abrasive Flap Disc 115mm", Categories.Consumables,
            "Zirconia flap disc for blending and finishing welds on steel, available in 40 to 120 grit.",
            6.80m, "Ironvale Tooling", 1.05),
        new("CN-5005", "Nordsen Heat Shrink Assortment", Categories.Consumables,
            "Assorted 3:1 adhesive-lined heat shrink tubing kit covering 1.6mm to 12.7mm in six colours.",
            26.50m, "Nordsen Electronics", 0.50),
    ];

    public static readonly IReadOnlyList<string> Channels = ["Direct", "Distributor", "Online"];
}
