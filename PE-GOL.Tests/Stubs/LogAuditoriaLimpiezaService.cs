using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PE_GOL.DAL.Interfaces;

namespace PE_GOL.Tests.Stubs;

/// <summary>
/// Stub de contrato del HostedService de retención (Spec HU-005 § Lógica BLL paso 3, D5).
/// El servicio REAL vive en PE-GOL.API/HostedServices/LogAuditoriaLimpiezaService.cs
/// (proyecto Web SDK NO referenciado por tests — mismo patrón que el stub de
/// TenantMiddleware en HU-004, ver PE-GOL.Tests.csproj L23-27).
/// ⚠️ STUB TDD (fase roja, TEST-01): los métodos lanzaban NotImplementedException.
/// @BackendDev implementó los cuerpos en fase 4 con la MISMA superficie (los 5 tests de
/// LogAuditoriaLimpiezaServiceTests son la especificación ejecutable):
///   · Ctor (Singleton): (IServiceScopeFactory, IConfiguration, ILogger&lt;LogAuditoriaLimpiezaService&gt;).
///     El repositorio ILogAuditoriaRepository es Scoped → scope DI por ejecución
///     (using var scope = _scopeFactory.CreateScope(); GetRequiredService&lt;ILogAuditoriaRepository&gt;()).
///   · Config: Auditoria:RetencionDias (int, default 90 — CA #4) y Auditoria:HoraLimpieza
///     (string "HH:mm", default "03:00") — añadidas a PE-GOL.API/appsettings.json.
///   · StartAsync: calcula el primer disparo (hoy a HoraLimpieza; si ya pasó, mañana) y
///     arranca el loop con PeriodicTimer/Task.Delay. StopAsync: cancela el token.
///   · EjecutarLimpiezaAsync: cutoff = UtcNow.AddDays(-RetencionDias) →
///     repo.EliminarAnterioresAAsync(cutoff, ct) → Log.Information → error → Log.Error y
///     CONTINUAR (RNF-014, graceful — el siguiente ciclo reintenta).
///   · Registro en Program.cs: builder.Services.AddHostedService&lt;LogAuditoriaLimpiezaService&gt;().
/// </summary>
public class LogAuditoriaLimpiezaService
{
    private const int RetencionDiasDefault = 90;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LogAuditoriaLimpiezaService> _logger;

    /// <summary>Ctor usado por @QA en fase TDD: tests = especificación (TEST-01/03/05).</summary>
    public LogAuditoriaLimpiezaService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<LogAuditoriaLimpiezaService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>IHostedService.StartAsync — arranca el loop diario (el real vive en PE-GOL.API).</summary>
    public Task StartAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>IHostedService.StopAsync — cancela el token y detiene el loop limpiamente.</summary>
    public Task StopAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Ejecución diaria de la limpieza (invocada por el loop a la HoraLimpieza).
    /// cutoff = UtcNow.AddDays(-RetencionDias) → EliminarAnterioresAAsync(cutoff) →
    /// Log.Information; error → Log.Error y continuar (RNF-014).
    /// </summary>
    public async Task EjecutarLimpiezaAsync(CancellationToken ct = default)
    {
        var retencionDias = ObtenerRetencionDias();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retencionDias);

        try
        {
            // Singleton → Scoped: scope DI por ejecución (patrón del spec paso 1).
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ILogAuditoriaRepository>();

            var eliminados = await repo.EliminarAnterioresAAsync(cutoff, ct);

            _logger.LogInformation(
                "Limpieza de log de auditoría: {Eliminados} entradas anteriores a {Cutoff} eliminadas. Modulo=Auditoria",
                eliminados, cutoff);
        }
        catch (Exception ex)
        {
            // RNF-014: la retención es política de almacenamiento; un fallo de BD no debe
            // tumbar el proceso — se loggea y el siguiente ciclo reintenta.
            _logger.LogError(ex, "Error en la limpieza del log de auditoría. Modulo=Auditoria");
        }
    }

    /// <summary>
    /// Calcula el próximo disparo diario: hoy a HoraLimpieza si aún no pasó; si ya pasó,
    /// mañana a HoraLimpieza (loop diario, verificación del test #24).
    /// </summary>
    public DateTimeOffset CalcularProximoDisparo(DateTimeOffset ahora, string horaLimpieza)
    {
        var partes = horaLimpieza.Split(':');
        var hora = int.Parse(partes[0]);
        var minuto = int.Parse(partes[1]);

        var hoy = new DateTimeOffset(ahora.Year, ahora.Month, ahora.Day, hora, minuto, 0, ahora.Offset);
        return hoy > ahora ? hoy : hoy.AddDays(1);
    }

    /// <summary>Auditoria:RetencionDias (int, default 90 — CA #4 "retención mínima").</summary>
    private int ObtenerRetencionDias()
    {
        var valor = _configuration["Auditoria:RetencionDias"];
        return int.TryParse(valor, out var dias) && dias > 0 ? dias : RetencionDiasDefault;
    }
}