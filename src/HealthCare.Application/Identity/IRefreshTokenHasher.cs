namespace HealthCare.Application.Identity;

public interface IRefreshTokenHasher
{
    string Hash(string rawToken);
}
