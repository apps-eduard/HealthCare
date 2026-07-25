using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminPatientsSmokeTests : E2ePageTestBase
{
    public ClinicAdminPatientsSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_Update_Clinic_Patient_Status_Without_Org_Controls()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/patients");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Patient Directory" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Patients enrolled in your clinic", new() { Exact = false }))
                .ToBeVisibleAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.Locator("[data-testid='patients-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Usage & Limits" })).ToHaveCountAsync(0);

            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Clinic patients" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.Locator("table.hc-table tbody tr").First).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Enroll", Exact = true })).ToHaveCountAsync(0);

            await Page.Locator("button[aria-label^='Details for']").First.ClickAsync();
            var drawer = Page.Locator(".ant-drawer").Filter(new() { HasText = "Enrollment status" });
            await Expect(drawer).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(drawer.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Enroll in clinic" })).ToHaveCountAsync(0);

            await drawer.Locator("#patient-enrollment-status").ClickAsync();
            await Page.Locator(".ant-select-dropdown:visible").GetByText("Inactive", new() { Exact = true }).ClickAsync();
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Update enrollment status" }).ClickAsync();
            await ConfirmEnrollmentChangeAsync();

            await Expect(drawer.GetByText("Inactive").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Patient Directory" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Page.Locator("button[aria-label^='Details for']").First.ClickAsync();
            drawer = Page.Locator(".ant-drawer").Filter(new() { HasText = "Enrollment status" });
            await Expect(drawer).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(drawer.GetByText("Inactive").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

            // Restore Active for other tests.
            await drawer.Locator("#patient-enrollment-status").ClickAsync();
            await Page.Locator(".ant-select-dropdown:visible").GetByText("Active", new() { Exact = true }).ClickAsync();
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Update enrollment status" }).ClickAsync();
            await ConfirmEnrollmentChangeAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_Update_Clinic_Patient_Status_Without_Org_Controls));
            throw;
        }
    }

    private async Task ConfirmEnrollmentChangeAsync()
    {
        var confirm = Page.Locator(".ant-modal").Filter(new() { HasText = "Update clinic enrollment" });
        await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Yes" }).ClickAsync();
    }
}
