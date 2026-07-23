namespace HealthCare.Application.Identity;

public interface IRefreshTokenGenerator
{
    string GenerateRawToken();
}
