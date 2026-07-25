using FluentAssertions;
using HealthCare.Mobile.Core.Configuration;

namespace HealthCare.Mobile.Tests;

public sealed class MobileAppOptionsValidatorTests
{
    [Fact]
    public void Validate_Accepts_Emulator_Cleartext_BaseUrl()
    {
        var options = new MobileAppOptions
        {
            EnvironmentName = "Emulator",
            ApiBaseUrl = "http://10.0.2.2:5080",
            AllowCleartextHttp = true,
            HttpTimeoutSeconds = 30,
        };

        MobileAppOptionsValidator.Validate(options).Should().BeEmpty();
        MobileAppOptionsValidator.GetNormalizedBaseAddress(options).AbsoluteUri
            .Should().Be("http://10.0.2.2:5080/");
    }

    [Fact]
    public void Validate_Rejects_Relative_Url()
    {
        var options = new MobileAppOptions
        {
            ApiBaseUrl = "/api",
            AllowCleartextHttp = true,
        };

        MobileAppOptionsValidator.Validate(options)
            .Should().Contain(e => e.Contains("absolute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Rejects_Http_When_Cleartext_Disabled()
    {
        var options = new MobileAppOptions
        {
            EnvironmentName = "Device",
            ApiBaseUrl = "http://192.168.1.10:5080",
            AllowCleartextHttp = false,
        };

        MobileAppOptionsValidator.Validate(options)
            .Should().Contain(e => e.Contains("AllowCleartextHttp", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Rejects_Cleartext_In_Production()
    {
        var options = new MobileAppOptions
        {
            EnvironmentName = "Production",
            ApiBaseUrl = "https://api.example.com",
            AllowCleartextHttp = true,
        };

        MobileAppOptionsValidator.Validate(options)
            .Should().Contain(e => e.Contains("Production", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void Validate_Rejects_Timeout_Out_Of_Range(int seconds)
    {
        var options = new MobileAppOptions
        {
            ApiBaseUrl = "https://api.example.com",
            AllowCleartextHttp = false,
            HttpTimeoutSeconds = seconds,
        };

        MobileAppOptionsValidator.Validate(options)
            .Should().Contain(e => e.Contains("HttpTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void GetNormalizedBaseAddress_Trims_Trailing_Slash()
    {
        var options = new MobileAppOptions
        {
            ApiBaseUrl = "https://api.example.com/v1/",
            AllowCleartextHttp = false,
        };

        MobileAppOptionsValidator.GetNormalizedBaseAddress(options).AbsoluteUri
            .Should().Be("https://api.example.com/v1/");
    }
}
