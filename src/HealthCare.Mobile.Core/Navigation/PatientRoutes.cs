namespace HealthCare.Mobile.Core.Navigation;

/// <summary>Blazor route constants for Patient mobile.</summary>
public static class PatientRoutes
{
    public const string Startup = "/";
    public const string SignIn = "/sign-in";
    public const string Register = "/register";
    public const string RegistrationComplete = "/registration-complete";
    public const string ConfirmEmail = "/confirm-email";
    public const string Home = "/home";
    public const string Profile = "/profile";
    public const string ProfileEdit = "/profile/edit";
    public const string Clinics = "/clinics";
    public const string ClinicEnroll = "/clinics/enroll";
    public const string Appointments = "/appointments";
    public const string Connectivity = "/connectivity";
    public const string BookingPlaceholder = "/discovery/booking-next";

    public static string ClinicDetails(string clinicCode) =>
        $"{Clinics}/{Uri.EscapeDataString(clinicCode)}";

    public static string ClinicDoctors(string clinicCode) =>
        $"{Clinics}/{Uri.EscapeDataString(clinicCode)}/doctors";

    public static string DoctorAvailability(string clinicCode, Guid staffMemberId) =>
        $"{Clinics}/{Uri.EscapeDataString(clinicCode)}/doctors/{staffMemberId:D}/availability";

    public static bool RequiresAuthentication(string relativePath)
    {
        var path = Normalize(relativePath);
        if (path is Home or Profile or ProfileEdit or Appointments or BookingPlaceholder)
        {
            return true;
        }

        return path == Clinics
               || path.StartsWith(Clinics + "/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGuestOnly(string relativePath)
    {
        var path = Normalize(relativePath);
        return path is SignIn
            or Register
            or RegistrationComplete
            or ConfirmEmail;
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
