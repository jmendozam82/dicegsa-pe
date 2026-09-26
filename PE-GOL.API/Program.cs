using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PE_GOL.API.HostedServices;
using PE_GOL.API.Middleware;
using PE_GOL.API.Validators;
using PE_GOL.IOC;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Configuración de Supabase (04_ARQUITECTURA.md § 9): carga opcional de
//    appsettings.Supabase.json con las credenciales reales (password de BD,
//    anon key y service_role key). Se fusiona DESPUÉS de appsettings.json y no
//    se versiona (ver .gitignore) para no exponer secretos.
builder.Configuration.AddJsonFile("appsettings.Supabase.json", optional: true, reloadOnChange: true);

// ── Logging estructurado (Serilog, RNF-023): configuración JSON con filtros por tenant/usuario/módulo.
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddControllers();

// Validación de forma del request (FluentValidation 11+): auto-validation + registro de validators.
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<TenantCreateRequestValidator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PE-GOL SaaS API",
        Version = "v1",
        Description = "API REST de PE-GOL SaaS · Sprints 1-3 completados: SaaS (HU-001..HU-004, HU-006..HU-008), Auditoría (HU-005), Planeación Estratégica (HU-009..HU-015), Objetivos y Plan de Acción (HU-016..HU-020) y UI de Tenants (HU-045). ~40 endpoints · Auth JWT por roles (SuperAdmin, AdminTenant, Gerente, JefeArea). Respuestas siempre en ApiResponse<T> (ARCH-07)."
    });

    // Esquema de seguridad Bearer JWT para Swagger (SEC-01).
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Ingrese el token JWT: Bearer {token}"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── Autenticación JWT base (SEC-01). La EMISIÓN de tokens es HU-004; aquí se deja la
//    infraestructura para que [Authorize] funcione leyendo Issuer/Audience/Key de appsettings.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key no está configurada en appsettings.json.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "pe-gol-saas";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "pe-gol-users";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // El claim del rol se emite como "rol" (JwtTokenHelper, ADR-004), no como
            // ClaimTypes.Role ("role"): AspNetCore solo mapea "role"→ClaimTypes.Role por
            // defecto, por eso [Authorize(Roles=...)] devolvía 403 pese a token válido.
            // SEC-01/HU-004: el claim "rol" (cuando existe) se trata como claim de rol.
            RoleClaimType = "rol"
        };
    });
builder.Services.AddAuthorization();

// CORS básico para el frontend (07_DESIGN_SYSTEM.md; se endurecerá en despliegue).
builder.Services.AddCors(o => o.AddPolicy("Default", p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

// Inyección de dependencias (PE-GOL.IOC): repositorios, servicios y fábrica de conexiones.
builder.Services.AddPEGolServices(builder.Configuration);

// HU-005 (CA #4): batch diario de retención del log de auditoría (ARCH-05 — HostedService).
// Config: Auditoria:RetencionDias (default 90) y Auditoria:HoraLimpieza (default "03:00").
builder.Services.AddHostedService<LogAuditoriaLimpiezaService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Manejo global de errores: envuelve en ApiResponse<T>; ValidacionException → 422,
// NotFoundException → 404, resto → 500 (ARCH-07).
app.UseMiddleware<ExceptionMiddleware>();

app.UseCors("Default");
app.UseAuthentication();

// ARCH-04/SEC-06: TenantMiddleware corre DESPUÉS de UseAuthentication — el JwtBearer ya
// validó el token (si era inválido, la request se rechazó con 401) y este middleware mapea
// los claims YA validados al TenantContext scoped (D6/D17, HU-004).
app.UseMiddleware<TenantMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();