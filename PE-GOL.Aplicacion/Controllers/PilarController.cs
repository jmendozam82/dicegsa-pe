using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PE_GOL.Aplicacion.Exceptions;
using PE_GOL.Aplicacion.Models;
using PE_GOL.Aplicacion.Services;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;
using PE_GOL.Utility.Exceptions;

namespace PE_GOL.Aplicacion.Controllers;

/// <summary>
/// UI de Pilares Estratégicos (Spec HU-013 § UI — CRUD; HU-014 § UI — Objetivos Trimestrales).
/// GET multi-rol (AdminTenant/Gerente/JefeArea — SEC-07 NO APLICA, D-E: pilar corporativo);
/// POST/PUT/DELETE solo Gerente (RN-006; ADM/JEF solo lectura — RN-007).
/// Código PEC-N auto-generado por la BLL (CA #1) — no editable. DELETE físico (D-A) con 422 si
/// tiene CGs/OKRs (CA #3). Objetivos trimestrales: 4 campos opcionales, texto plano máx 2000 (HU-014).
/// SEC-06: {cicloId}/{pilarId} vienen de la ruta; tenant_id nunca viaja en body/query.
/// </summary>
[Authorize(Roles = "AdminTenant,Gerente,JefeArea")]
public class PilarController : Controller
{
    private readonly IApiClient _apiClient;
    private readonly ILogger<PilarController> _logger;

    public PilarController(IApiClient apiClient, ILogger<PilarController> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    // ─── Listado (GET /Pilar?cicloId=...) ────────────────────────────────────

    public async Task<IActionResult> Index(Guid? cicloId)
    {
        try
        {
            var ciclos = await _apiClient.GetAsync<List<CicloResponse>>("/api/v1/ciclos");
            var modelo = new PilaresIndexViewModel
            {
                Ciclos = ciclos,
                CicloId = cicloId,
                EsGerente = User.IsInRole("Gerente")
            };

            if (cicloId.HasValue)
            {
                var pilares = await _apiClient.GetAsync<List<PilarResponse>>($"/api/v1/ciclos/{cicloId}/pilares");
                modelo.Items = pilares;
                var ciclo = ciclos.FirstOrDefault(c => c.Id == cicloId.Value);
                modelo.CicloNombre = ciclo?.Nombre;
                modelo.CicloEstado = ciclo?.Estado;
            }

            return View(modelo);
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
        catch (ApiClientException ex)
        {
            TempData["Error"] = ex.Message;
            return View(new PilaresIndexViewModel
            {
                CicloId = cicloId,
                EsGerente = User.IsInRole("Gerente")
            });
        }
    }

    // ─── Crear (GET/POST /Pilar/Crear?cicloId=...) — solo Gerente ─────────────

    [Authorize(Roles = "Gerente")]
    public IActionResult Crear(Guid cicloId)
        => View(new PilarFormViewModel { CicloId = cicloId });

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> Crear(Guid cicloId, PilarCreateRequest request)
    {
        try
        {
            var pilar = await _apiClient.PostAsync<PilarCreateRequest, PilarResponse>(
                $"/api/v1/ciclos/{cicloId}/pilares", request);
            TempData["Success"] = $"Pilar '{pilar.Nombre}' ({pilar.Codigo}) creado correctamente.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new PilarFormViewModel
            {
                CicloId = cicloId,
                Nombre = request.Nombre,
                EstrategiaVictoria = request.EstrategiaVictoria,
                Orden = request.Orden
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Editar (GET/POST /Pilar/Editar/{id}?cicloId=...) — solo Gerente ──────

    [Authorize(Roles = "Gerente")]
    public async Task<IActionResult> Editar(Guid cicloId, Guid id)
    {
        try
        {
            var pilar = await _apiClient.GetAsync<PilarResponse>($"/api/v1/ciclos/{cicloId}/pilares/{id}");
            return View(new PilarFormViewModel
            {
                CicloId = cicloId,
                PilarId = pilar.Id,
                Nombre = pilar.Nombre,
                EstrategiaVictoria = pilar.EstrategiaVictoria,
                Orden = pilar.Orden,
                EsEdicion = true,
                Codigo = pilar.Codigo
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

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> Editar(Guid cicloId, Guid id, PilarUpdateRequest request)
    {
        try
        {
            var pilar = await _apiClient.PutAsync<PilarUpdateRequest, PilarResponse>(
                $"/api/v1/ciclos/{cicloId}/pilares/{id}", request);
            TempData["Success"] = $"Pilar '{pilar.Nombre}' ({pilar.Codigo}) actualizado correctamente.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new PilarFormViewModel
            {
                CicloId = cicloId,
                PilarId = id,
                Nombre = request.Nombre,
                EstrategiaVictoria = request.EstrategiaVictoria,
                Orden = request.Orden,
                EsEdicion = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Eliminar (POST /Pilar/Eliminar/{id}?cicloId=...) — solo Gerente ──────

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> Eliminar(Guid cicloId, Guid id)
    {
        try
        {
            var pilar = await _apiClient.DeleteAsync<PilarResponse>($"/api/v1/ciclos/{cicloId}/pilares/{id}");
            TempData["Success"] = $"Pilar '{pilar.Nombre}' ({pilar.Codigo}) eliminado correctamente.";
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (ApiClientException ex)
        {
            // 422 (tiene CGs/OKRs — CA #3, o ciclo Cerrado — RC-12) → flash con el mensaje del servidor.
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index), new { cicloId });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }

    // ─── Objetivos Trimestrales (GET /Pilar/ObjetivosTrimestrales/{id}?cicloId=...) ──

    public async Task<IActionResult> ObjetivosTrimestrales(Guid cicloId, Guid id)
    {
        try
        {
            var pilar = await _apiClient.GetAsync<PilarResponse>($"/api/v1/ciclos/{cicloId}/pilares/{id}");
            return View(new ObjetivosTrimestralesViewModel
            {
                CicloId = cicloId,
                PilarId = pilar.Id,
                PilarCodigo = pilar.Codigo,
                PilarNombre = pilar.Nombre,
                ObjetivoQ1 = pilar.ObjetivoQ1,
                ObjetivoQ2 = pilar.ObjetivoQ2,
                ObjetivoQ3 = pilar.ObjetivoQ3,
                ObjetivoQ4 = pilar.ObjetivoQ4,
                EsGerente = User.IsInRole("Gerente")
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

    // ─── Guardar Objetivos Trimestrales (POST .../ObjetivosTrimestrales/{id}) — solo Gerente ──

    [Authorize(Roles = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> ObjetivosTrimestrales(Guid cicloId, Guid id, ObjetivosTrimestralesUpdateRequest request)
    {
        try
        {
            var pilar = await _apiClient.PutAsync<ObjetivosTrimestralesUpdateRequest, PilarResponse>(
                $"/api/v1/ciclos/{cicloId}/pilares/{id}/objetivos-trimestrales", request);
            TempData["Success"] = $"Objetivos trimestrales del pilar '{pilar.Nombre}' guardados correctamente.";
            return RedirectToAction(nameof(ObjetivosTrimestrales), new { cicloId, id });
        }
        catch (ApiClientException ex) when (ex.StatusCode == 404)
        {
            return NotFound();
        }
        catch (ApiClientException ex)
        {
            foreach (var error in ex.Errors)
                ModelState.AddModelError(string.Empty, error);
            return View(new ObjetivosTrimestralesViewModel
            {
                CicloId = cicloId,
                PilarId = id,
                ObjetivoQ1 = request.ObjetivoQ1,
                ObjetivoQ2 = request.ObjetivoQ2,
                ObjetivoQ3 = request.ObjetivoQ3,
                ObjetivoQ4 = request.ObjetivoQ4,
                EsGerente = true
            });
        }
        catch (UnauthorizedException)
        {
            return RedirectToAction("Login", "Auth");
        }
    }
}