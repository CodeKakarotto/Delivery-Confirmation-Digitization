using Confirmations.Api.Data.Entities;
using Confirmations.Api.Validation;

namespace Confirmations.Api.Tests.Validation;

/// <summary>Short labels for the small "Exceptions / notes" column.</summary>
public class DeliveryExceptionsTests
{
    [Theory]
    [InlineData("Nobody home, left at front door", new[] { "Left at door", "Nobody home" })]
    [InlineData("LEFT WITH NEIGHBOR at 12", new[] { "Left with neighbor" })]
    [InlineData("left with neighbour", new[] { "Left with neighbor" })]
    [InlineData("No one home", new[] { "Nobody home" })]
    [InlineData("Customer refused delivery", new[] { "Refused" })]
    [InlineData("Returned to shop", new[] { "Returned to shop" })]
    [InlineData("Leave at side door", new string[0])]  // an instruction, not something that happened
    [InlineData("Ring doorbell", new string[0])]
    [InlineData(null, new string[0])]
    public void SpotsDeliveryExceptionsInTheInstructions(string? instructions, string[] expected)
    {
        Assert.Equal(expected, DeliveryExceptions.FromInstructions(instructions));
    }

    [Theory]
    [InlineData(FlagType.MissingField, "Zone", "Zone missing")]
    [InlineData(FlagType.MissingField, "OrderNumber", "Order ID missing")]
    [InlineData(FlagType.OrderNotFound, "OrderNumber", "Order not found")]
    [InlineData(FlagType.OrderAlreadyConfirmed, "OrderNumber", "Already confirmed")]
    [InlineData(FlagType.DuplicateOrder, "OrderNumber", "Duplicate order")]
    [InlineData(FlagType.DuplicateFile, null, "Uploaded twice")]
    [InlineData(FlagType.InvalidTotalProducts, "TotalProducts", "Bad product count")]
    [InlineData(FlagType.MissingSignature, null, "No signature")]
    [InlineData(FlagType.LowConfidence, "DeliverTo", "Check deliver to")]
    [InlineData(FlagType.OcrFailed, null, "Couldn't read")]
    public void EachProblemHasAShortLabel(FlagType type, string? field, string expected)
    {
        Assert.Equal(expected, DeliveryExceptions.LabelFor(type, field));
    }
}
