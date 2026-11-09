namespace Confirmations.Api.Data.Entities;

/// <summary>An order the shop knows about. Scanned order IDs are checked against these.</summary>
public class Order
{
    public int Id { get; set; }

    /// <summary>The order ID printed on the ticket, e.g. "MF-10001". Unique.</summary>
    public required string OrderNumber { get; set; }

    public required string Zone { get; set; }
    public required string DeliverTo { get; set; }
    public int TotalProducts { get; set; }
    public DateOnly DeliveryDate { get; set; }
}
