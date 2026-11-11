using System.Threading.Channels;
using Confirmations.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Confirmations.Api.Ocr;

/// <summary>List of scans waiting to be read. Uploads add to it and return immediately.</summary>
public sealed class OcrQueue
{
    private readonly Channel<Guid> _waiting = Channel.CreateUnbounded<Guid>();

    public void Enqueue(Guid scanId) => _waiting.Writer.TryWrite(scanId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _waiting.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Reads queued scans one at a time in the background. On startup it also
/// queues any scans that were uploaded but never read (e.g. after a restart).
/// </summary>
public class OcrBackgroundService(
    OcrQueue queue,
    IServiceScopeFactory scopes,
    IOptions<OcrOptions> options,
    ILogger<OcrBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunInBackground)
            return;

        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unread = await db.ScanDocuments.Where(s => s.OcrResult == null)
                .OrderBy(s => s.UploadedAt).Select(s => s.Id).ToListAsync(stoppingToken);
            foreach (var id in unread)
                queue.Enqueue(id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Could not look for unread scans at startup ({Reason}).", ex.GetType().Name);
        }

        await foreach (var scanId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<OcrProcessor>().ProcessAsync(scanId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Reading scan {ScanId} failed unexpectedly ({Reason}).", scanId, ex.GetType().Name);
            }
        }
    }
}
