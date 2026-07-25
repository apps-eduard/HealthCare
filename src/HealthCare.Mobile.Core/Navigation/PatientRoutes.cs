namespace HealthCare.Mobile.Core.Navigation;

/// <summary>Blazor route constants for Patient mobile. Feature pages are placeholders until later PM milestones.</summary>
public static class PatientRoutes
{
    public const string Startup = "/";
    public const string SignIn = "/sign-in";
    public const string Register = "/register";
    public const string Home = "/home";
    public const string Profile = "/profile";
    public const string Clinics = "/clinics";
    public const string Appointments = "/appointments";
    public const string Connectivity = "/connectivity";

    public static bool RequiresAuthentication(string relativePath)
    {
        var path = Normalize(relativePath);
        return path is Home or Profile or Clinics or Appointments;
    }

    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Startup;
        }

        path = path.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var q = path.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            path = path[..q];
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
