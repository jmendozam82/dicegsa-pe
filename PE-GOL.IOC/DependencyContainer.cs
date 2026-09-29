using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PE_GOL.BLL.Interfaces;
using PE_GOL.BLL.Limites;
using PE_GOL.BLL.Services;
using PE_GOL.DAL.Infrastructure;
using PE_GOL.DAL.Interfaces;
using PE_GOL.DAL.Repositories.Ciclo;
using PE_GOL.DAL.Repositories.Objetivos;
using PE_GOL.DAL.Repositories.PlanOperativo;
using PE_GOL.DAL.Repositories.Saas;
using PE_GOL.Utility.Security;
using PE_GOL.Utility.Storage;
using PE_GOL.Utility.Email;

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
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<ICicloRepository, CicloRepository>(); // HU-007: dominio Ciclo (D2)
        services.AddScoped<IObjetivoCgRepository, ObjetivoCgRepository>(); // HU-017: objetivos corporativos
        services.AddScoped<IAccionPlanRepository, AccionPlanRepository>(); // HU-019: acciones de plan
        services.AddScoped<IHistorialProgresoRepository, HistorialProgresoRepository>(); // HU-020: historial de progreso
        services.AddScoped<ILogAuditoriaRepository, LogAuditoriaRepository>(); // HU-005: log de auditoría (D4, tabla global fuera de RLS)

        // Servicios de negocio (Scoped: estado por request).
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEmpresaService, EmpresaService>(); // HU-006: autoconfiguración del tenant (D2)
        services.AddScoped<ICicloService, CicloService>(); // HU-007: gestión de ciclos anuales (D2)
        services.AddScoped<IAreaService, AreaService>(); // HU-009: áreas estratégicas (hijos del agregado Ciclo, D-I/D2)
        services.AddScoped<IResponsableService, ResponsableService>(); // HU-010: responsables (hijos del agregado Ciclo, D-I/D2)
        services.AddScoped<IFilosofiaService, FilosofiaService>(); // HU-011: visión y misión (hija del agregado Ciclo, D-I/D2)
        services.AddScoped<IPilarService, PilarService>(); // HU-013: pilares estratégicos (hijos del agregado Ciclo, D-I/D2)
        services.AddScoped<IObjetivoCgService, ObjetivoCgService>(); // HU-017: objetivos corporativos
        services.AddScoped<IAccionPlanService, AccionPlanService>(); // HU-019: acciones de plan
        services.AddScoped<IDashboardService, DashboardService>(); // HU-015: tablero de inicio del Jefe de Área (D-J)
        services.AddScoped<IEmailService, EmailService>(); // HU-010: correo de activación (STACK-10, D-C)
        services.AddScoped<ILogAuditoriaService, LogAuditoriaService>(); // HU-005: consulta del log de auditoría (solo lectura, CA #3)

        // PlanLimitValidator es STATELESS (lógica pura, D4): Singleton.
        // TenantService (nueva dependencia, HU-002 §9.1) y PlanService lo consumen vía
        // IPlanService/PlanService — la inyección se resuelve automáticamente por el DI.
        services.AddSingleton<PlanLimitValidator>();

        // ── Infraestructura de autenticación (HU-004) ──────────────────────────────
        // JwtOptions: Singleton enlazado desde la sección "Jwt" de appsettings
        // (AccessTokenMinutes=60, RefreshTokenDays=7, Key — SEC-01). El valor por defecto
        // de Key es SOLO dev/tests; Program.cs lanza si Jwt:Key falta en producción.
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value);

        // TenantContext: Scoped — poblado por TenantMiddleware desde los claims del JWT
        // (SEC-06, ARCH-04) y consumido por los servicios BLL para la auditoría (D6).
        services.AddScoped<TenantContext>();

        // ── Infraestructura de storage (HU-006 ADR-005 + HU-022 ADR-013) ───────────────────────
        // SupabaseStorageOptions: Singleton enlazado desde la sección "Supabase" de appsettings
        // (Url, ServiceKey, LogoBucket="logos-tenant", EntregablesBucket="entregables" — ARCH-06).
        // El bucket del logo NUNCA es StorageBucket: StorageBucket quedó obsoleta (ADR-013).
        // IStorageFileApi: Singleton — la ÚNICA capa que habla con el SDK de Supabase (D-O);
        // se construye desde SupabaseStorageOptions para que `Supabase.Client` no salga de
        // PE-GOL.Utility hacia este contenedor (ARCH-02). Se registra ANTES que IStorageHelper,
        // que la consume por inyección; los 4 tests de Storage de HU-022 la mockean, de modo que
        // StorageHelper es testeable sin red ni credenciales por primera vez.
        // IStorageHelper / StorageHelper: Singleton stateless (ADR-005) — 2 wrappers de dominio
        // (HU-006) + 3 genéricos con bucket explícito (HU-022, ADR-013).
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var url = config["Supabase:Url"]
                ?? throw new InvalidOperationException("Supabase:Url no está configurada. Defínela en appsettings.json o en la variable de entorno SUPABASE_URL.");
            var serviceKey = config["Supabase:ServiceKey"]
                ?? throw new InvalidOperationException("Supabase:ServiceKey no está configurada. Defínela en appsettings.json o en la variable de entorno SUPABASE_SERVICE_KEY.");
            var entregablesBucket = config["Supabase:EntregablesBucket"] ?? "entregables";

            // Guard de consistencia (ADR-013 § "Regla de consistencia"): la clave canónica del
            // bucket de entregables es EntregablesBucket. Si viniera vacía, se avisa en el
            // ARRANQUE, no a mitad de una subida de archivos (que es cuando ya no se puede
            // deshacer y los adjuntos irían al bucket equivocado).
            if (string.IsNullOrWhiteSpace(entregablesBucket))
                throw new InvalidOperationException(
                    "Supabase:EntregablesBucket no puede estar vacía. Debe ser el bucket privado de entregables (ARCH-06; por defecto \"entregables\").");

            return new SupabaseStorageOptions
            {
                Url = url,
                ServiceKey = serviceKey,
                LogoBucket = config["Supabase:LogoBucket"] ?? "logos-tenant",
                EntregablesBucket = entregablesBucket
            };
        });
        services.AddSingleton<IStorageFileApi>(sp =>
            new SupabaseStorageFileApi(sp.GetRequiredService<SupabaseStorageOptions>()));
        services.AddSingleton<IStorageHelper, StorageHelper>();

        return services;
    }
}