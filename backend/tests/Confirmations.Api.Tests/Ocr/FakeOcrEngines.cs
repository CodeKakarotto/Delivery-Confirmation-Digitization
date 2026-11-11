using Confirmations.Api.Ocr;

namespace Confirmations.Api.Tests.Ocr;

/// <summary>Pretends to be Tesseract: returns prepared output, or fails on purpose.</summary>
public sealed class FakeTesseractRunner(Func<string> output) : ITesseractRunner
{
    public int Calls { get; private set; }

    public static FakeTesseractRunner Returning(TsvBuilder ticket) => new(ticket.Build);
    public static FakeTesseractRunner Crashing() => new(() => throw new InvalidOperationException("Tesseract exited with code 1."));

    public Task<string> RunTsvAsync(string imagePath, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(output());
    }
}

/// <summary>Pretends to check the signature box.</summary>
public sealed class FakeSignatureDetector(SignatureCheck? answer) : ISignatureMarkDetector
{
    public PixelBox? LastArea { get; private set; }

    public SignatureCheck? Detect(string imagePath, PixelBox area)
    {
        LastArea = area;
        return answer;
    }
}
