using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminAuditSmokeTests : E2ePageTestBase
{
    public ClinicAdminAuditSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_View_Clinic_Audit_After_Profile_Update()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/clinic/settings");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Profile" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            var unique = $"CA9 Audit {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var nameInput = Page.Locator("#clinic-settings-name");
            await Expect(nameInput).ToBeEnabledAsync();
            await nameInput.FillAsync(unique);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save clinic profile" }).ClickAsync();
            await Expect(Page.GetByText("Clinic profile saved.")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.GotoAsync("/clinic/audit-logs");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Audit Logs" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.Locator("[data-testid='clinic-audit-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export CSV" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Organization settings", new() { Exact = false })).ToHaveCountAsync(0);

            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Clinic audit logs" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            var table = Page.GetByRole(AriaRole.Table, new() { Name = "Clinic audit logs" });
            await Expect(table.GetByRole(AriaRole.Cell, new() { Name = "clinic_profile_update" }).First)
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(table.GetByText("Clinic profile updated", new() { Exact = false }).First)
                .ToBeVisibleAsync();

            await table.GetByRole(AriaRole.Button, new() { Name = "View clinic audit event detail" }).First.ClickAsync();
            var detail = Page.Locator(".ant-drawer").Filter(new() { HasText = "Clinic audit event" });
            await Expect(detail).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(detail.GetByText("Clinic profile updated", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(detail.GetByText("Raw metadata, passwords, tokens, and clinical content are not available."))
                .ToBeVisibleAsync();
            await Expect(detail.GetByText("Password", new() { Exact = true })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export CSV" })).ToHaveCountAsync(0);
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_View_Clinic_Audit_After_Profile_Update));
            throw;
        }
    }
}
