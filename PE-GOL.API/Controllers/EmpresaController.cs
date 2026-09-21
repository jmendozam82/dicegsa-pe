using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.BLL.Interfaces;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.API.Controllers;

/// <summary>
/// Endpoints de Configuración de la Empresa (Spec HU-006 § Endpoints) — AUTOCONFIGURACIÓN del
/// tenant por el ADM (D2), a diferencia de /api/v1/tenants (gestión SaaS del SuperAdmin, HU-001).
/// Ruta raíz api/v1/empresa (NO bajo Saas/): el tenant_id se resuelve SIEMPRE del TenantContext
/// (claims del JWT, SEC-06) — nunca del body ni del query string.
/// GET: lectura multi-rol (AdminTenant/Gerente/JefeArea, D10) · PUT y POST logo: solo AdminTenant (D12).
/// Respuestas SIEMPRE con el wrapper ApiResponse&lt;T&gt; (ARCH-07).
/// </summary>
[ApiController]
[Route("api/v1/empresa")]
public class EmpresaController : ControllerBase
{
    private readonly IEmpresaService _service;

    public EmpresaController(IEmpresaService service)
    {
        _service = service;
    }

    /// <summary>GET /api/v1/empresa — Configuración de la empresa (logoUrl = URL firmada 24 h o null).</summary>
    [HttpGet]
    [Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
    [ProducesResponseType(typeof(ApiResponse<EmpresaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(CancellationToken ct = default)
    {
        var data = await _service.ObtenerAsync(ct);
        return Ok(new ApiResponse<EmpresaResponse> { Success = true, Data = data });
    }

    /// <summary>PUT /api/v1/empresa — Actualiza nombre/eslogan/zonaHoraria (solo AdminTenant).</summary>
    [HttpPut]
    [Authorize(Roles = "AdminTenant")]
    [ProducesResponseType(typeof(ApiResponse<EmpresaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Actualizar([FromBody] EmpresaUpdateRequest request, CancellationToken ct = default)
    {
        var data = await _service.ActualizarAsync(request, ct);
        return Ok(new ApiResponse<EmpresaResponse> { Success = true, Message = "Configuración actualizada", Data = data });
    }

    /// <summary>POST /api/v1/empresa/logo — Sube el logo (multipart, campo "archivo"; solo AdminTenant).
    /// PNG/JPG ≤ 2 MB; se persiste el path en tenant.logo_url y se devuelve URL firmada fresca.</summary>
    [HttpPost("logo")]
    [Authorize(Roles = "AdminTenant")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<EmpresaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SubirLogo(IFormFile archivo)
    {
        // Guard del controller: sin archivo → 400 (el resto de validaciones de formato/tamaño
        // las hace la BLL → 422, fuente de verdad UX-04).
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = "Debe seleccionar un archivo de imagen",
                Errors = ["Debe seleccionar un archivo de imagen"]
            });

        await using var stream = archivo.OpenReadStream();
        var data = await _service.SubirLogoAsync(stream, archivo.FileName, archivo.ContentType, archivo.Length, HttpContext.RequestAborted);
        return Ok(new ApiResponse<EmpresaResponse> { Success = true, Message = "Logo actualizado", Data = data });
    }
}