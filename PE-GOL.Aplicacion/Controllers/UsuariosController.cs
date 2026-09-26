using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Gestión de Usuarios de Tenants — solo SuperAdmin (HU-003 § UI del SA).
/// Doble capa: [Authorize(Roles="SuperAdmin")] MVC + [Authorize(Roles="SuperAdmin")] en la API.
/// SEC-06: el SA es GLOBAL → declara tenantId/areaId explícitamente (única excepción
/// documentada del spec HU-003). Sin DELETE físico: activar/desactivar (D1).
/// </summary>
[Authorize(Roles = "SuperAdmin")]
public class UsuariosController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<UsuariosController> _logger;

    public UsuariosController(IApiClient apiClient, ILogger<UsuariosController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado paginado con filtros ────────────────────────────────────────

    public async Task<IActionResult> Index(int page = 1, int pageSize = 10,
        string? rol = null, string? estado = null, Guid? tenantId = null)
    {
        var tenants = await CargarTenantsAsync();

        // Saneo (espejo BLL HU-003): page ≥ 1 · pageSize 1..100 (default 10).
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = new Dictionary<string, string?>
        {
            ["page"] = page.ToString(),
            ["pageSize"] = pageSize.ToString()
        };
        if (!string.IsNullOrWhiteSpace(rol))
            query["rol"] = rol;
        if (!string.IsNullOrWhiteSpace(estado))
            query["estado"] = estado;
        if (tenantId.HasValue)
            query["tenantId"] = tenantId.Value.ToString();

        try
        {
            var resultado = await _apiClient.GetAsync<PagedResult<UsuarioResponse>>("/api/v1/usuarios", query);
            var modelo = new UsuariosIndexViewModel
            {
                Items = resultado.Items,
                Page = resultado.Page,
                PageSize = resultado.PageSize,
                Total = resultado.Total,
                TotalPages = resultado.TotalPages,
                RolFiltro = rol,
                EstadoFiltro = estado,
                TenantIdFiltro = tenantId,
                Tenants = tenants
            };
            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            var modelo = new UsuariosIndexViewModel
            {
                Items = [],
                Page = page,
                PageSize = pageSize,
                Total = 0,
                TotalPages = 0,
                RolFiltro = rol,
                EstadoFiltro = estado,
                TenantIdFiltro = tenantId,
                Tenants = tenants
            };
            return View(modelo);
        }
    }

    // ─── Crear ───────────────────────────────────────────────────────────────

    public async Task<IActionResult> Crear()
    {
        var tenants = await CargarTenantsAsync();
        return View(new UsuarioFormViewModel { Tenants = tenants });
    }

    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioCreateRequest request)
    {
        SancionarAreaId(request.Rol);
        try
        {
            var usuario = await _apiClient.PostAsync<UsuarioCreateRequest, UsuarioResponse>("/api/v1/usuarios", request);
            TempData["Success"] = $"Usuario '{usuario.Nombre}' creado. Deberá cambiar su contraseña en el primer inicio de sesión.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var tenants = await CargarTenantsAsync();
            return View(new UsuarioFormViewModel
            {
                Nombre = request.Nombre,
                Correo = request.Correo,
                Rol = request.Rol,
                TenantId = request.TenantId,
                AreaId = request.AreaId,
                Tenants = tenants
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (sin password; se resetea aparte) ────────────────────────────

    public async Task<IActionResult> Editar(Guid id)
    {
        try
        {
            var usuario = await _apiClient.GetAsync<UsuarioResponse>($"/api/v1/usuarios/{id}");
            var tenants = await CargarTenantsAsync();
            return View(new UsuarioFormViewModel
            {
                Id = usuario.Id,
                Nombre = usuario.Nombre,
                Correo = usuario.Correo,
                Rol = usuario.Rol,
                TenantId = usuario.TenantId ?? Guid.Empty,
                AreaId = usuario.AreaId,
                EsEdicion = true,
                Tenants = tenants
            });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    [HttpPost]
    public async Task<IActionResult> Editar(Guid id, UsuarioUpdateRequest request)
    {
        SancionarAreaId(request.Rol);
        try
        {
            var usuario = await _apiClient.PutAsync<UsuarioUpdateRequest, UsuarioResponse>($"/api/v1/usuarios/{id}", request);
            TempData["Success"] = $"Usuario '{usuario.Nombre}' actualizado correctamente.";
            return RedirectToAction(nameof(Index));
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            var tenants = await CargarTenantsAsync();
            return View(new UsuarioFormViewModel
            {
                Id = id,
                Nombre = request.Nombre,
                Correo = request.Correo,
                Rol = request.Rol,
                TenantId = request.TenantId,
                AreaId = request.AreaId,
                EsEdicion = true,
                Tenants = tenants
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Activar / Desactivar (soft-state, sin DELETE) ───────────────────────

    [HttpPost]
    public async Task<IActionResult> Activar(Guid id)
    {
        try
        {
            await _apiClient.PostAsync<UsuarioResponse>($"/api/v1/usuarios/{id}/activar");
            TempData["Success"] = "Usuario activado.";
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Desactivar(Guid id)
    {
        try
        {
            await _apiClient.PostAsync<UsuarioResponse>($"/api/v1/usuarios/{id}/desactivar");
            TempData["Success"] = "Usuario desactivado (sesiones activas revocadas).";
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        return RedirectToAction(nameof(Index));
    }

    // ─── Reset de contraseña (temporal; Data null, llega por correo en Sprint 2) ──

    [HttpPost]
    public async Task<IActionResult> ResetContrasena(Guid id)
    {
        try
        {
            await _apiClient.PostAsync<object>($"/api/v1/usuarios/{id}/reset-contrasena");
            TempData["Success"] = "Contraseña temporal generada. El usuario deberá cambiarla en su próximo inicio de sesión.";
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        return RedirectToAction(nameof(Index));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// RN-011: areaId aplica SOLO a JefeArea. En cualquier otro rol se fuerza null y se
    /// limpian los errores de binding heredados (p. ej. el autofill del navegador que
    /// llena areaId con el correo → "The value 'x' is not valid for AreaId").
    /// </summary>
    private void SancionarAreaId(string rol)
    {
        if (string.Equals(rol, "JefeArea", StringComparison.OrdinalIgnoreCase))
            return;

        var clavesArea = ModelState.Keys
            .Where(k => k.EndsWith("AreaId", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var clave in clavesArea)
            ModelState.Remove(clave);
    }

    /// <summary>Catálogo de tenants para filtros/formulario del SA (API limita a 100).</summary>
    private async Task<List<TenantResponse>> CargarTenantsAsync()
    {
        try
        {
            var query = new Dictionary<string, string?> { ["page"] = "1", ["pageSize"] = "100" };
            var resultado = await _apiClient.GetAsync<PagedResult<TenantResponse>>("/api/v1/tenants", query);
            return resultado.Items;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el catálogo de tenants para la UI de usuarios.");
            return [];
        }
    }
}