using Confirmations.Api.Auth;
using Confirmations.Api.Review;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Confirmations.Api.Controllers;

/// <summary>
/// "Check &amp; approve" (Step 7). Employees and Managers only. The reviewer is
/// always the signed-in user; any user ID sent by the browser is ignored.
/// </summary>
[ApiController]
[Route("api/confirmations")]
[Authorize(Policy = Policies.EmployeeOrManager)]
public class ConfirmationsController(ReviewService review, UserManager<IdentityUser> userManager) : ControllerBase
{
    public sealed record ProblemBody(string Message, IReadOnlyList<string>? Reasons = null);

    private string CurrentUserId => userManager.GetUserId(User)!;

    /// <summary>Sheets waiting for review, oldest first.</summary>
    [HttpGet("waiting")]
    public async Task<IActionResult> Waiting(CancellationToken ct) => Ok(await review.WaitingAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await review.GetAsync(id, ct) is { } detail ? Ok(detail) : NotFound();

    /// <summary>Save corrections without deciding.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Save(Guid id, EditRequest request, CancellationToken ct) =>
        await Respond(id, await review.SaveAsync(id, request, CurrentUserId, ct), ct);

    /// <summary>Save corrections and approve: this makes it the official confirmation.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, EditRequest request, CancellationToken ct) =>
        await Respond(id, await review.ApproveAsync(id, request, CurrentUserId, ct), ct);

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectRequest request, CancellationToken ct) =>
        await Respond(id, await review.RejectAsync(id, request, CurrentUserId, ct), ct);

    [HttpPost("{id:guid}/send-to-manager")]
    public async Task<IActionResult> SendToManager(Guid id, ManagerRequest request, CancellationToken ct) =>
        await Respond(id, await review.SendToManagerAsync(id, request, CurrentUserId, ct), ct);

    private async Task<IActionResult> Respond(Guid id, ReviewResult result, CancellationToken ct) => result.Outcome switch
    {
        ReviewOutcome.Ok => Ok(await review.GetAsync(id, ct)),
        ReviewOutcome.NotFound => NotFound(),
        ReviewOutcome.Invalid => BadRequest(new ProblemBody(result.Message!, result.Reasons)),
        _ => Conflict(new ProblemBody(result.Message!)),
    };
}
