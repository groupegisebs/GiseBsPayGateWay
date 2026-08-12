namespace GiseBsPayGateway.Services.Tax;

/// <summary>
/// Catalogue des taux de TVA / GST / VAT des pays d'Afrique (taux standard sur services numériques / SaaS).
/// Les montants catalogue sont hors taxe (HT) ; le montant à encaisser est toujours TTC = HT + taxe.
/// Sources : taux officiels standards publiés (à confirmer localement avant production).
/// </summary>
public static class AfricanTaxRates
{
    public sealed record Rate(
        string CountryCode,
        string CountryNameFr,
        string TaxName,
        decimal RatePercent,
        string Notes = "");

    /// <summary>Taux standard (%). 0 = pas de TVA nationale standard applicable / non en vigueur.</summary>
    public static readonly IReadOnlyDictionary<string, Rate> ByCountry =
        new Dictionary<string, Rate>(StringComparer.OrdinalIgnoreCase)
        {
            ["DZ"] = new("DZ", "Algérie", "TVA", 19.00m),
            ["AO"] = new("AO", "Angola", "IVA", 14.00m),
            ["BJ"] = new("BJ", "Bénin", "TVA", 18.00m),
            ["BW"] = new("BW", "Botswana", "VAT", 14.00m),
            ["BF"] = new("BF", "Burkina Faso", "TVA", 18.00m),
            ["BI"] = new("BI", "Burundi", "TVA", 18.00m),
            ["CV"] = new("CV", "Cabo Verde", "IVA", 15.00m),
            ["CM"] = new("CM", "Cameroun", "TVA", 19.25m, "Taux standard Cameroun 19,25 %"),
            ["CF"] = new("CF", "Centrafrique", "TVA", 19.00m),
            ["TD"] = new("TD", "Tchad", "TVA", 18.00m),
            ["KM"] = new("KM", "Comores", "TVA", 10.00m),
            ["CG"] = new("CG", "Congo", "TVA", 18.00m),
            ["CD"] = new("CD", "RD Congo", "TVA", 16.00m),
            ["CI"] = new("CI", "Côte d'Ivoire", "TVA", 18.00m),
            ["DJ"] = new("DJ", "Djibouti", "TVA", 10.00m),
            ["EG"] = new("EG", "Égypte", "VAT", 14.00m),
            ["GQ"] = new("GQ", "Guinée équatoriale", "TVA", 15.00m),
            ["ER"] = new("ER", "Érythrée", "VAT", 0.00m, "Pas de TVA standard nationale clairement applicable"),
            ["SZ"] = new("SZ", "Eswatini", "VAT", 15.00m),
            ["ET"] = new("ET", "Éthiopie", "VAT", 15.00m),
            ["GA"] = new("GA", "Gabon", "TVA", 18.00m),
            ["GM"] = new("GM", "Gambie", "VAT", 15.00m),
            ["GH"] = new("GH", "Ghana", "VAT", 15.00m, "Hors prélèvements additionnels éventuels (NHIL/GETFund)"),
            ["GN"] = new("GN", "Guinée", "TVA", 18.00m),
            ["GW"] = new("GW", "Guinée-Bissau", "TVA", 15.00m),
            ["KE"] = new("KE", "Kenya", "VAT", 16.00m),
            ["LS"] = new("LS", "Lesotho", "VAT", 15.00m),
            ["LR"] = new("LR", "Liberia", "GST", 10.00m),
            ["LY"] = new("LY", "Libye", "VAT", 0.00m, "Pas de TVA standard nationale"),
            ["MG"] = new("MG", "Madagascar", "TVA", 20.00m),
            ["MW"] = new("MW", "Malawi", "VAT", 16.50m),
            ["ML"] = new("ML", "Mali", "TVA", 18.00m),
            ["MR"] = new("MR", "Mauritanie", "TVA", 16.00m),
            ["MU"] = new("MU", "Maurice", "VAT", 15.00m),
            ["MA"] = new("MA", "Maroc", "TVA", 20.00m),
            ["MZ"] = new("MZ", "Mozambique", "IVA", 16.00m),
            ["NA"] = new("NA", "Namibie", "VAT", 15.00m),
            ["NE"] = new("NE", "Niger", "TVA", 19.00m),
            ["NG"] = new("NG", "Nigeria", "VAT", 7.50m),
            ["RW"] = new("RW", "Rwanda", "VAT", 18.00m),
            ["ST"] = new("ST", "Sao Tomé-et-Principe", "IVA", 15.00m),
            ["SN"] = new("SN", "Sénégal", "TVA", 18.00m),
            ["SC"] = new("SC", "Seychelles", "VAT", 15.00m),
            ["SL"] = new("SL", "Sierra Leone", "GST", 15.00m),
            ["SO"] = new("SO", "Somalie", "VAT", 0.00m, "Cadre TVA national non standardisé"),
            ["ZA"] = new("ZA", "Afrique du Sud", "VAT", 15.00m),
            ["SS"] = new("SS", "Soudan du Sud", "VAT", 18.00m),
            ["SD"] = new("SD", "Soudan", "VAT", 17.00m),
            ["TZ"] = new("TZ", "Tanzanie", "VAT", 18.00m),
            ["TG"] = new("TG", "Togo", "TVA", 18.00m),
            ["TN"] = new("TN", "Tunisie", "TVA", 19.00m),
            ["UG"] = new("UG", "Ouganda", "VAT", 18.00m),
            ["ZM"] = new("ZM", "Zambie", "VAT", 16.00m),
            ["ZW"] = new("ZW", "Zimbabwe", "VAT", 15.00m),
            // Territoires / États additionnels souvent listés en Afrique
            ["EH"] = new("EH", "Sahara occidental", "TVA", 0.00m, "Régime fiscal à confirmer"),
        };

    public static bool TryGet(string? countryCode, out Rate rate)
    {
        rate = null!;
        if (string.IsNullOrWhiteSpace(countryCode))
            return false;
        return ByCountry.TryGetValue(countryCode.Trim().ToUpperInvariant(), out rate!);
    }

    public static IReadOnlyList<Rate> AllOrdered() =>
        ByCountry.Values.OrderBy(x => x.CountryNameFr, StringComparer.OrdinalIgnoreCase).ToList();
}
