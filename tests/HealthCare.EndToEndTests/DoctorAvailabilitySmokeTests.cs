using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class DoctorAvailabilitySmokeTests : E2ePageTestBase
{
    public DoctorAvailabilitySmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Sees_Self_Only_Availability_And_Schedule_Defaults()
    {
        try
        {
            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GetByRole(AriaRole.Link, new() { Name = "Availability" }).ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/availability.*"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My Availability" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.Locator("#availability-doctor-self")).ToBeDisabledAsync();
            await Expect(Page.Locator("#availability-doctor")).ToHaveCountAsync(0);

            await Page.GotoAsync("/appointments");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.Locator("[data-testid='appointments-doctor-self']")).ToBeVisibleAsync();
            await Expect(Page.Locator("#queue-doctor")).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Your assigned appointments", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Create appointment" })).ToHaveCountAsync(0);

            await Page.GotoAsync("/appointments/calendar");
            await Expect(Page.Locator("[data-testid='calendar-doctor-self']")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Your assigned schedule", new() { Exact = false })).ToBeVisibleAsync();

            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Patients" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Staff Management" })).ToHaveCountAsync(0);

            await LogoutAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/login.*"));
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Sees_Self_Only_Availability_And_Schedule_Defaults));
            throw;
        }
    }
}
