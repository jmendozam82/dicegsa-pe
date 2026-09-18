using PE_GOL.DAL.Interfaces;

namespace PE_GOL.API.HostedServices;

/// <summary>
/// HostedService de retención del Log de Auditoría (Spec HU-005 § Lógica BLL paso 3, D5; ARCH-05).
/// Batch diario idempotente: elimina las entradas con created_at &lt; NOW() - RetencionDias.
/// CA #4 (retención mínima 90 días) configurable vía appsettings:
///   · Auditoria:RetencionDias (int, default 90)
///   · Auditoria:HoraLimpieza (string "HH:mm", default "03:00")
/// Singleton (los HostedServices lo son); ILogAuditoriaRepository es Scoped → scope DI por
/// ejecución (using var scope = _scopeFactory.CreateScope()).
/// RNF-014 (graceful): si la BD falla → Log.Error y CONTINUAR (el siguiente ciclo reintenta;
/// la retención es una política de almacenamiento, no bloquea la operación principal).
/// CA #3/D12: este DELETE batch es la ÚNICA operación de borrado sobre log_auditoria y NO es
/// invocable por API (política de almacenamiento automática, no operación de rol).
/// Superficie idéntica al stub de contrato de @QA (PE_GOL.Tests.Stubs): StartAsync/StopAsync/
/// EjecutarLimpiezaAsync/CalcularProximoDisparo.
/// </summary>
public class LogAuditoriaLimpiezaService : IHostedService, IDisposable
{
    private const int RetencionDiasDefault = 90;
    private const string HoraLimpiezaDefault = "03:00";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LogAuditoriaLimpiezaService> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public LogAuditoriaLimpiezaService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<LogAuditoriaLimpiezaService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Spec paso 3 · StartAsync: calcula el primer disparo (hoy a HoraLimpieza; si ya
    /// pasó, mañana) y arranca el loop diario con Task.Delay hasta el próximo disparo.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = EjecutarLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Spec paso 5 · StopAsync: cancela el token y detiene el loop limpiamente.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask; }
            catch (OperationCanceledException) { /* apagado normal */ }
        }
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>Loop diario: espera hasta el próximo disparo, ejecuta la limpieza y reprograma
    /// el siguiente disparo (una ejecución por día — verificación del test #24).</summary>
    private async Task EjecutarLoopAsync(CancellationToken ct)
    {
        var horaLimpieza = _configuration["Auditoria:HoraLimpieza"] ?? HoraLimpiezaDefault;
        var proximo = CalcularProximoDisparo(DateTimeOffset.UtcNow, horaLimpieza);

        while (!ct.IsCancellationRequested)
        {
            var espera = proximo - DateTimeOffset.UtcNow;
            if (espera > TimeSpan.Zero)
            {
                try { await Task.Delay(espera, ct); }
                catch (OperationCanceledException) { break; }
            }

            await EjecutarLimpiezaAsync(ct);

            // Reprograma el siguiente disparo a la HoraLimpieza del día siguiente.
            proximo = CalcularProximoDisparo(DateTimeOffset.UtcNow, horaLimpieza);
        }
    }

    /// <summary>Spec paso 4 · Ejecución diaria: cutoff = UtcNow.AddDays(-RetencionDias) →
    /// EliminarAnterioresAAsync(cutoff) (DAL-L4) → Log.Information; error → Log.Error y
    /// continuar (RNF-014, graceful — el siguiente ciclo reintenta).</summary>
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

    /// <summary>Calcula el próximo disparo diario: hoy a HoraLimpieza si aún no pasó; si ya
    /// pasó, mañana a HoraLimpieza (loop diario, verificación del test #24).</summary>
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

    public void Dispose()
    {
        _cts?.Dispose();
    }
}