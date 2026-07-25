using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

/// <summary>
/// DR-10: Doctor cannot reach admin surfaces; peer appointments stay concealed in the queue.
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class DoctorAccessDenialSmokeTests : E2ePageTestBase
{
    public DoctorAccessDenialSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Restricted_Nav_And_Direct_Routes_Are_Denied()
    {
        try
        {
            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Reports" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Audit Logs" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Staff Management" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Usage & Limits" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical Notes", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Doctor Reports" })).ToHaveCountAsync(0);

            await AssertDeniedRouteAsync("/clinic/reports");
            await AssertDeniedRouteAsync("/clinic/audit-logs");
            await AssertDeniedRouteAsync("/staff");
            await AssertDeniedRouteAsync("/organization/settings");
            await AssertDeniedRouteAsync("/organization/dashboard");
            await AssertDeniedRouteAsync("/clinic/settings");
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Restricted_Nav_And_Direct_Routes_Are_Denied));
            throw;
        }
    }

    [Fact]
    public async Task Doctor_Does_Not_See_Peer_Clinic_Appointment_Or_Note_Content()
    {
        try
        {
            var (peer, peerNoteId) = await DoctorE2eApi.CreatePeerClinicBAppointmentWithNoteAsync(Host, "DR10-PEER");

            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Page.GotoAsync("/appointments");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {peer.Id}" }))
                .ToHaveCountAsync(0);
            await Expect(Page.GetByText(peer.Id.ToString("D"))).ToHaveCountAsync(0);
            await Expect(Page.GetByText("PEER-SECRET", new() { Exact = false })).ToHaveCountAsync(0);

            // No dedicated medical-note deep link in Doctor Web; concealment is API 404 (same as DR-6/DR-9).
            (await DoctorE2eApi.GetNoteStatusAsDoctorAsync(Host, peerNoteId))
                .Should().Be(System.Net.HttpStatusCode.NotFound);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Does_Not_See_Peer_Clinic_Appointment_Or_Note_Content));
            throw;
        }
    }

    private async Task AssertDeniedRouteAsync(string path)
    {
        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var content = await Page.ContentAsync();
        var denied = Page.Url.Contains("/forbidden", StringComparison.OrdinalIgnoreCase)
                     || Page.Url.Contains("/login", StringComparison.OrdinalIgnoreCase)
                     || content.Contains("permission", StringComparison.OrdinalIgnoreCase)
                     || content.Contains("do not have permission", StringComparison.OrdinalIgnoreCase)
                     || content.Contains("not found", StringComparison.OrdinalIgnoreCase)
                     || content.Contains("Access denied", StringComparison.OrdinalIgnoreCase);
        denied.Should().BeTrue($"Doctor must not see protected content at {path} (url={Page.Url})");
        content.ToLowerInvariant().Should().NotContain("clinic appointment report");
        content.ToLowerInvariant().Should().NotContain("organization dashboard");
    }
}
