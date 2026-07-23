namespace HealthCare.Web.Configuration;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Absolute base URL of HealthCare.Api (e.g. http://localhost:5080/).
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5080/";
}
