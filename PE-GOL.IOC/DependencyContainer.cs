using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Limites;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DAL.Repositories.Saas;

namespace PE_GOL.IOC;

/// <summary>
/// Contenedor de inyección de dependencias (04_ARQUITECTURA.md § 2 — proyecto PE-GOL.IOC).
/// Registra repositorios (Scoped), servicios BLL (Scoped), la fábrica de conexiones
/// (Singleton con la connection string) y la infraestructura transversal.
/// Los validadores FluentValidation se registran en PE-GOL.API/Program.cs desde su propio
/// ensamblado (los validators viven en la capa API por diseño: no pueden registrarse aquí
/// sin que IOC referencie API, lo que violaría ARCH-02).
/// </summary>
public static class DependencyContainer
{
    public static IServiceCollection AddPEGolServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Supabase")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Supabase no está configurada. Defínala en appsettings.json o en la variable de entorno SUPABASE_CONNECTION.");

        // Fábrica de conexiones: singleton, inmutable, reutilizable (Dapper).
        services.AddSingleton<IDbConnectionFactory>(new DbConnectionFactory(connectionString));

        // Repositorios (Scoped: una instancia por request HTTP).
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();

        // Servicios de negocio (Scoped: estado por request).
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IUsuarioService, UsuarioService>();

        // PlanLimitValidator es STATELESS (lógica pura, D4): Singleton.
        // TenantService (nueva dependencia, HU-002 §9.1) y PlanService lo consumen vía
        // IPlanService/PlanService — la inyección se resuelve automáticamente por el DI.
        services.AddSingleton<PlanLimitValidator>();

        return services;
    }
}