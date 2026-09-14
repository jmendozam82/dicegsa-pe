using PE_GOL.BLL.Limites;
using PE_GOL.DTO.Limites;

namespace PE_GOL.Tests;

/// <summary>
/// Tests de contrato para PlanLimitValidator — Spec HU-002 § "Tests requeridos" (casos 1-7).
/// TDD fase red (TEST-01): el stub PlanLimitValidator lanza NotImplementedException, por lo que
/// TODOS los tests fallan deliberadamente hasta que @BackendDev implemente la lógica pura en fase 4
/// (spec § Lógica BLL paso 8). Patrón Arrange/Act/Assert · xUnit (TEST-03/05).
/// </summary>
public class PlanLimitValidatorTests
{
    private readonly PlanLimitValidator _validator = new();

    // ─── 1. Dentro de límites ───────────────────────────────────────────────

    [Fact]
    public void Validar_DentroDeLimites_RetornaValido()
    {
        // Arrange
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 5, UsuariosActuales: 10, CiclosActivosActuales: 1);

        // Act
        var resultado = _validator.Validar(limites, conteos);

        // Assert: EsValido=true, Errores vacío
        Assert.True(resultado.EsValido);
        Assert.Empty(resultado.Errores);
    }

    // ─── 2. Justo en el límite ─────────────────────────────────────────────

    [Fact]
    public void Validar_JustoEnElLimite_RetornaValido()
    {
        // Arrange: conteos == máximos → válido (comparación estricta `>`, no `>=`)
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 10, UsuariosActuales: 25, CiclosActivosActuales: 1);

        // Act
        var resultado = _validator.Validar(limites, conteos);

        // Assert
        Assert.True(resultado.EsValido);
        Assert.Empty(resultado.Errores);
    }

    // ─── 3. Áreas excedidas ─────────────────────────────────────────────────

    [Fact]
    public void Validar_AreasExcedidas_RetornaError()
    {
        // Arrange: 11 áreas activas vs máx 10
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 11, UsuariosActuales: 10, CiclosActivosActuales: 1);

        // Act
        var resultado = _validator.Validar(limites, conteos);

        // Assert: error de áreas con los valores (spec: "El tenant supera el límite de áreas: 11 > 10")
        Assert.False(resultado.EsValido);
        Assert.Single(resultado.Errores);
        Assert.Contains("áreas", resultado.Errores[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("11 > 10", resultado.Errores[0]);
    }

    // ─── 4. Usuarios excedidos ──────────────────────────────────────────────

    [Fact]
    public void Validar_UsuariosExcedidos_RetornaError()
    {
        // Arrange: 26 usuarios vs máx 25
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 5, UsuariosActuales: 26, CiclosActivosActuales: 1);

        // Act
        var resultado = _validator.Validar(limites, conteos);

        // Assert: error de usuarios con los valores (spec: "El tenant supera el límite de usuarios: 26 > 25")
        Assert.False(resultado.EsValido);
        Assert.Single(resultado.Errores);
        Assert.Contains("usuarios", resultado.Errores[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("26 > 25", resultado.Errores[0]);
    }

    // ─── 5. Ciclos activos excedidos ────────────────────────────────────────

    [Fact]
    public void Validar_CiclosActivosExcedidos_RetornaError()
    {
        // Arrange: 2 ciclos activos vs máx 1
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 5, UsuariosActuales: 10, CiclosActivosActuales: 2);

        // Act
        var resultado = _validator.Validar(limites, conteos);

        // Assert: error de ciclos activos con los valores (spec: "...ciclos activos: 2 > 1")
        Assert.False(resultado.EsValido);
        Assert.Single(resultado.Errores);
        Assert.Contains("ciclos activos", resultado.Errores[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 > 1", resultado.Errores[0]);
    }

    // ─── 6. Múltiples violaciones ───────────────────────────────────────────

    [Fact]
    public void Validar_MultiplesViolaciones_RetornaTodosLosErrores()
    {
        // Arrange: 3 recursos excedidos → 3 errores acumulados
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 11, UsuariosActuales: 26, CiclosActivosActuales: 2);

        // Act
        var resultado = _validator.Validar(limites, conteos);

        // Assert: Errores.Count == N (todos los recursos infractores, no solo el primero)
        Assert.False(resultado.EsValido);
        Assert.Equal(3, resultado.Errores.Count);
    }

    // ─── 7. Atajo booleano EsValido ─────────────────────────────────────────

    [Fact]
    public void EsValido_CuandoHayViolacion_RetornaFalse()
    {
        // Arrange
        var limites = new PlanLimits(10, 25, 1);
        var conteos = new ConteosUsoPlan(AreasActuales: 11, UsuariosActuales: 10, CiclosActivosActuales: 1);

        // Act
        var esValido = _validator.EsValido(limites, conteos);

        // Assert: atajo booleano coherente con Validar
        Assert.False(esValido);
    }
}