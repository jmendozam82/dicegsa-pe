using PE_GOL.DTO.Common;
using PE_GOL.DTO.Requests;
using PE_GOL.DTO.Responses;

namespace PE_GOL.BLL.Interfaces;

/// <summary>
/// Servicio de gestión de Tenants (Spec HU-001 § Lógica BLL). Solo SuperAdmin.
/// Los métodos lanzan ValidacionException (422) y NotFoundException (404) desde
/// PE_GOL.BLL.Exceptions.
/// </summary>
public interface ITenantService
{
    Task<TenantResponse> CrearAsync(TenantCreateRequest request, CancellationToken ct = default);
    Task<TenantResponse> ActualizarAsync(Guid id, TenantUpdateRequest request, CancellationToken ct = default);
    Task<TenantResponse> DesactivarAsync(Guid id, CancellationToken ct = default);
    Task<TenantResponse> ActivarAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<TenantResponse>> ListarAsync(int page, int pageSize, string? estado, Guid? planId, CancellationToken ct = default);
    Task<TenantResponse> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);
}