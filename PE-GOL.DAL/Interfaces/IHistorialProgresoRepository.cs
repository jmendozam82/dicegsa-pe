using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PE_GOL.DTO.Responses.Objetivos;
using PE_GOL.Entity.PlanOperativo;

namespace PE_GOL.DAL.Interfaces;

public interface IHistorialProgresoRepository
{
    Task InsertAsync(HistorialProgresoEntity entity, CancellationToken ct = default);
    Task<IEnumerable<HistorialProgresoResponse>> ListarPorAccionAsync(Guid accionId, Guid tenantId, CancellationToken ct = default);
}
