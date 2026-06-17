namespace GiseBsPayGateway.Entities;

public class CollectedTaxLine : BaseEntity
{
    public Guid CollectedTaxRecordId { get; set; }
    public CollectedTaxRecord CollectedTaxRecord { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
}
