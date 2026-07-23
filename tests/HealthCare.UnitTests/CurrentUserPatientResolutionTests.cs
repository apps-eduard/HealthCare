using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace HealthCare.UnitTests;

public sealed class CurrentUserPatientResolutionTests
{
    [Fact]
    public void Resolves_PatientId_From_Database_Linkage_Not_Claims()
    {
        var userId = Guid.NewGuid();
        var linkedPatientId = Guid.NewGuid();
        var claimPatientId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new HealthCareDbContext(options);
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = "resolve@test.local",
            UserName = "resolve@test.local",
            NormalizedEmail = "RESOLVE@TEST.LOCAL",
            NormalizedUserName = "RESOLVE@TEST.LOCAL",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        db.Roles.Add(new IdentityRole<Guid>
        {
            Id = roleId,
            Name = AppRoles.Patient,
            NormalizedName = AppRoles.Patient.ToUpperInvariant(),
        });
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        db.Patients.Add(new Patient
        {
            Id = linkedPatientId,
            UserId = userId,
            FirstName = "Linked",
            LastName = "Patient",
            IsActive = true,
        });
        db.SaveChanges();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, AppRoles.Patient),
                new Claim("patient_id", claimPatientId.ToString()),
            ], authenticationType: "Test")),
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        ICurrentUser currentUser = new CurrentUserContext(accessor, db, NullLogger<CurrentUserContext>.Instance);

        currentUser.PatientId.Should().Be(linkedPatientId);
        currentUser.PatientId.Should().NotBe(claimPatientId);
        currentUser.IsInRole(AppRoles.Patient).Should().BeTrue();
    }

    [Fact]
    public void Unlinked_Patient_User_Has_Null_PatientId()
    {
        var userId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new HealthCareDbContext(options);
        db.Users.Add(new ApplicationUser
        {
            Id = userId,
            Email = "unlinked@test.local",
            UserName = "unlinked@test.local",
            NormalizedEmail = "UNLINKED@TEST.LOCAL",
            NormalizedUserName = "UNLINKED@TEST.LOCAL",
            EmailConfirmed = true,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        db.SaveChanges();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, AppRoles.Patient),
            ], authenticationType: "Test")),
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        ICurrentUser currentUser = new CurrentUserContext(accessor, db, NullLogger<CurrentUserContext>.Instance);

        currentUser.PatientId.Should().BeNull();
        ((ICurrentPatient)(CurrentUserContext)currentUser).HasLinkedPatient.Should().BeFalse();
    }
}
