using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HealthCare.Contracts.MedicalNotes;

namespace HealthCare.Web.Services;

public interface IMedicalNoteApiClient
{
    Task<IReadOnlyList<MedicalNoteSummaryResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> GetByIdAsync(
        Guid medicalNoteId,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> CreateDraftAsync(
        Guid appointmentId,
        CreateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> UpdateDraftAsync(
        Guid medicalNoteId,
        UpdateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> SignAsync(
        Guid medicalNoteId,
        SignMedicalNoteRequest request,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> AmendAsync(
        Guid medicalNoteId,
        AmendMedicalNoteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MedicalNoteApiClient : IMedicalNoteApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public MedicalNoteApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IReadOnlyList<MedicalNoteSummaryResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync(
            $"api/v1/appointments/{appointmentId:D}/medical-notes",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<List<MedicalNoteSummaryResponse>>(cancellationToken))
               ?? [];
    }

    public async Task<MedicalNoteDetailResponse> GetByIdAsync(
        Guid medicalNoteId,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync($"api/v1/medical-notes/{medicalNoteId:D}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid medical note response", null, null);
    }

    public async Task<MedicalNoteDetailResponse> CreateDraftAsync(
        Guid appointmentId,
        CreateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.PostAsJsonAsync(
            $"api/v1/appointments/{appointmentId:D}/medical-notes",
            request,
            SerializerOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid medical note create response", null, null);
    }

    public async Task<MedicalNoteDetailResponse> UpdateDraftAsync(
        Guid medicalNoteId,
        UpdateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.PatchAsJsonAsync(
            $"api/v1/medical-notes/{medicalNoteId:D}/draft",
            request,
            SerializerOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid medical note update response", null, null);
    }

    public async Task<MedicalNoteDetailResponse> SignAsync(
        Guid medicalNoteId,
        SignMedicalNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.PostAsJsonAsync(
            $"api/v1/medical-notes/{medicalNoteId:D}/sign",
            request,
            SerializerOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid medical note sign response", null, null);
    }

    public async Task<MedicalNoteDetailResponse> AmendAsync(
        Guid medicalNoteId,
        AmendMedicalNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.PostAsJsonAsync(
            $"api/v1/medical-notes/{medicalNoteId:D}/amend",
            request,
            SerializerOptions,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid medical note amend response", null, null);
    }
}
