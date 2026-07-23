using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

/// <summary>
/// Base controller enforcing the /api/v1 route prefix convention.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
}
