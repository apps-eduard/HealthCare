namespace HealthCare.Contracts.Patients;

/// <summary>
/// Partial profile update. Properties with <c>*Specified</c> false are omitted and left unchanged.
/// Setting a property (including to null/empty) marks it specified.
/// </summary>
public sealed class UpdatePatientProfileRequest
{
    public int ExpectedVersion { get; init; }

    private string? _firstName;
    private bool _firstNameSpecified;
    private string? _middleName;
    private bool _middleNameSpecified;
    private string? _lastName;
    private bool _lastNameSpecified;
    private DateOnly? _dateOfBirth;
    private bool _dateOfBirthSpecified;
    private string? _gender;
    private bool _genderSpecified;
    private string? _mobileNumber;
    private bool _mobileNumberSpecified;
    private string? _preferredLanguage;
    private bool _preferredLanguageSpecified;
    private string? _address;
    private bool _addressSpecified;
    private string? _emergencyContact;
    private bool _emergencyContactSpecified;

    public string? FirstName
    {
        get => _firstName;
        set
        {
            _firstName = value;
            _firstNameSpecified = true;
        }
    }

    public bool FirstNameSpecified => _firstNameSpecified;

    public string? MiddleName
    {
        get => _middleName;
        set
        {
            _middleName = value;
            _middleNameSpecified = true;
        }
    }

    public bool MiddleNameSpecified => _middleNameSpecified;

    public string? LastName
    {
        get => _lastName;
        set
        {
            _lastName = value;
            _lastNameSpecified = true;
        }
    }

    public bool LastNameSpecified => _lastNameSpecified;

    public DateOnly? DateOfBirth
    {
        get => _dateOfBirth;
        set
        {
            _dateOfBirth = value;
            _dateOfBirthSpecified = true;
        }
    }

    public bool DateOfBirthSpecified => _dateOfBirthSpecified;

    public string? Gender
    {
        get => _gender;
        set
        {
            _gender = value;
            _genderSpecified = true;
        }
    }

    public bool GenderSpecified => _genderSpecified;

    public string? MobileNumber
    {
        get => _mobileNumber;
        set
        {
            _mobileNumber = value;
            _mobileNumberSpecified = true;
        }
    }

    public bool MobileNumberSpecified => _mobileNumberSpecified;

    public string? PreferredLanguage
    {
        get => _preferredLanguage;
        set
        {
            _preferredLanguage = value;
            _preferredLanguageSpecified = true;
        }
    }

    public bool PreferredLanguageSpecified => _preferredLanguageSpecified;

    public string? Address
    {
        get => _address;
        set
        {
            _address = value;
            _addressSpecified = true;
        }
    }

    public bool AddressSpecified => _addressSpecified;

    public string? EmergencyContact
    {
        get => _emergencyContact;
        set
        {
            _emergencyContact = value;
            _emergencyContactSpecified = true;
        }
    }

    public bool EmergencyContactSpecified => _emergencyContactSpecified;

    public bool HasAnyEditableField =>
        FirstNameSpecified
        || MiddleNameSpecified
        || LastNameSpecified
        || DateOfBirthSpecified
        || GenderSpecified
        || MobileNumberSpecified
        || PreferredLanguageSpecified
        || AddressSpecified
        || EmergencyContactSpecified;
}

public sealed class RegisterPatientWithClinicRequest
{
    /// <summary>
    /// Public clinic code. Resolved server-side to ClinicId (maps to unique Clinic.Slug).
    /// </summary>
    public string ClinicCode { get; init; } = string.Empty;
}
