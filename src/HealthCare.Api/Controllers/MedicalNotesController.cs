using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.MedicalNotes;
using HealthCare.Contracts.MedicalNotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize]
[Authorize(Policy = AuthorizationPolicies.StaffUser)]
[Route("api/v1")]
public sealed class MedicalNotesController : ControllerBase
{
    private readonly IMedicalNoteService _notes;

    public MedicalNotesController(IMedicalNoteService notes)
    {
        _notes = notes;
    }

    [AuthorizePermission(Permissions.MedicalNotes.Read)]
    [HttpGet("appointments/{appointmentId:guid}/medical-notes")]
    [ProducesResponseType(typeof(IReadOnlyList<MedicalNoteSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MedicalNoteSummaryResponse>>> ListForAppointment(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await _notes.ListForAppointmentAsync(appointmentId, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.MedicalNotes.Create)]
    [HttpPost("appointments/{appointmentId:guid}/medical-notes")]
    [ProducesResponseType(typeof(MedicalNoteDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MedicalNoteDetailResponse>> CreateDraft(
        Guid appointmentId,
        [FromBody] CreateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _notes.CreateDraftAsync(appointmentId, request, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.MedicalNotes.Read)]
    [HttpGet("medical-notes/{medicalNoteId:guid}")]
    [ProducesResponseType(typeof(MedicalNoteDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MedicalNoteDetailResponse>> GetById(
        Guid medicalNoteId,
        CancellationToken cancellationToken)
    {
        var result = await _notes.GetByIdAsync(medicalNoteId, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.MedicalNotes.UpdateDraft)]
    [HttpPatch("medical-notes/{medicalNoteId:guid}/draft")]
    [ProducesResponseType(typeof(MedicalNoteDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MedicalNoteDetailResponse>> UpdateDraft(
        Guid medicalNoteId,
        [FromBody] UpdateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _notes.UpdateDraftAsync(medicalNoteId, request, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.MedicalNotes.Sign)]
    [HttpPost("medical-notes/{medicalNoteId:guid}/sign")]
    [ProducesResponseType(typeof(MedicalNoteDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MedicalNoteDetailResponse>> Sign(
        Guid medicalNoteId,
        [FromBody] SignMedicalNoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _notes.SignAsync(medicalNoteId, request, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.MedicalNotes.Amend)]
    [HttpPost("medical-notes/{medicalNoteId:guid}/amend")]
    [ProducesResponseType(typeof(MedicalNoteDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MedicalNoteDetailResponse>> Amend(
        Guid medicalNoteId,
        [FromBody] AmendMedicalNoteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _notes.AmendAsync(medicalNoteId, request, cancellationToken);
        return Ok(result);
    }
}
