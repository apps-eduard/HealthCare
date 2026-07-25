using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class DoctorProfileSmokeTests : E2ePageTestBase
{
    public DoctorProfileSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Can_Edit_Own_Profile_And_Sees_Read_Only_Identity()
    {
        try
        {
            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GetByRole(AriaRole.Link, new() { Name = "My Profile" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/doctor/profile.*"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My Profile" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.Locator("[data-testid='doctor-profile-clinic']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-testid='doctor-profile-organization']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-testid='doctor-profile-email']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-testid='doctor-profile-role']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-testid='doctor-profile-specialty']")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Status is read-only")).ToBeVisibleAsync();

            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Staff Management" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Patients" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Select organization" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical Notes", new() { Exact = false })).ToHaveCountAsync(0);

            var unique = $"DR2 Profile {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var displayInput = Page.Locator("#doctor-profile-display-name");
            await Expect(displayInput).ToBeEnabledAsync();
            await displayInput.FillAsync(unique);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Save doctor profile" }).ClickAsync();
            await Expect(Page.GetByText("Doctor profile saved.")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My Profile" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.Locator("#doctor-profile-display-name")).ToHaveValueAsync(unique);

            await Expect(Page.Locator("[data-testid='doctor-profile-email']")).ToContainTextAsync("read-only");
            await Expect(Page.Locator("[data-testid='doctor-profile-role']")).ToContainTextAsync("read-only");
            await Expect(Page.Locator("[data-testid='doctor-profile-specialty']")).ToContainTextAsync("read-only");
            await Expect(Page.GetByText("Status is read-only")).ToBeVisibleAsync();

            await LogoutAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/login.*"));
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Can_Edit_Own_Profile_And_Sees_Read_Only_Identity));
            throw;
        }
    }
}
