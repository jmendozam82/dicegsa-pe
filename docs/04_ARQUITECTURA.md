# 🏗️ Arquitectura del Sistema — PE-GOL SaaS
## Documento de Diseño Arquitectónico · Versión 1.0
### Stack: ASP.NET Core .NET 8 · Supabase (PostgreSQL 15) · Bootstrap 5.3

---

## 1. Visión General

PE-GOL SaaS es una aplicación web **multi-tenant** construida sobre una arquitectura **N-Tier** con separación estricta de responsabilidades. Cada capa se implementa como un proyecto independiente dentro de una solución .NET, siguiendo el mismo patrón establecido en Freiroute TMS.

```
┌─────────────────────────────────────────────────────────────┐
│                      INTERNET / BROWSER                     │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTPS / TLS 1.2+
┌──────────────────────────▼──────────────────────────────────┐
│              PE-GOL.Aplicacion  (ASP.NET Core MVC)          │
│   Vistas Razor · Controllers MVC · Bootstrap 5.3 · jQuery  │
└──────────────────────────┬──────────────────────────────────┘
                           │ HTTP interno
┌──────────────────────────▼──────────────────────────────────┐
│                 PE-GOL.API  (ASP.NET Core Web API)          │
│       Controllers REST · JWT Auth · FluentValidation        │
└──────────────────────────┬──────────────────────────────────┘
                           │ Interfaces / DI
┌──────────────────────────▼──────────────────────────────────┐
│                     PE-GOL.BLL                              │
│           Servicios de negocio · Cálculos · Reglas          │
└──────────────────────────┬──────────────────────────────────┘
                           │ Interfaces / DI
┌──────────────────────────▼──────────────────────────────────┐
│                     PE-GOL.DAL                              │
│          Repositorios · Dapper · Queries SQL / RLS          │
└──────────────────────────┬──────────────────────────────────┘
                           │ TCP 5432
┌──────────────────────────▼──────────────────────────────────┐
│              Supabase (PostgreSQL 15 + RLS)                 │
│       Row-Level Security · Storage Buckets · Auth JWT       │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. Proyectos de la Solución

| # | Proyecto | Tipo | Responsabilidad |
|---|----------|------|-----------------|
| 1 | `PE-GOL.Aplicacion` | ASP.NET Core MVC | Presentación: vistas Razor, controllers MVC, gestión de sesión, consumo de la API interna |
| 2 | `PE-GOL.API` | ASP.NET Core Web API | API REST: endpoints, autenticación JWT, validación de requests, documentación Swagger |
| 3 | `PE-GOL.BLL` | Class Library | Lógica de negocio: servicios, cálculos de progreso/OKRs/semáforos, orquestación |
| 4 | `PE-GOL.DAL` | Class Library | Acceso a datos: repositorios con Dapper, queries parametrizadas, manejo de transacciones |
| 5 | `PE-GOL.Entity` | Class Library | Entidades del dominio: clases POCO que mapean las tablas de la BD |
| 6 | `PE-GOL.DTO` | Class Library | Objetos de transferencia: Request/Response DTOs entre capas y hacia el cliente |
| 7 | `PE-GOL.IOC` | Class Library | Inversión de control: registro de dependencias, configuración de servicios, fábricas |
| 8 | `PE-GOL.Utility` | Class Library | Utilidades transversales: helpers, constantes, extensiones, semáforo, JWT helper, email service |

### Dependencias entre proyectos

```
Aplicacion  →  API  →  BLL  →  DAL  →  Entity
                ↓        ↓       ↓
               DTO      DTO     DTO
                ↑        ↑       ↑
               IOC ──────────────┘
                         ↑
                      Utility  (cross-cutting, todos dependen)
```

---

## 3. Estructura de Carpetas por Proyecto

```
PE-GOL.sln
│
├── PE-GOL.Aplicacion/
│   ├── Controllers/
│   │   ├── AuthController.cs
│   │   ├── DashboardController.cs
│   │   ├── PlanController.cs
│   │   ├── OKRController.cs
│   │   ├── PresupuestoController.cs
│   │   └── ReporteController.cs
│   ├── Views/
│   │   ├── Shared/
│   │   │   ├── _Layout.cshtml
│   │   │   ├── _Sidebar.cshtml
│   │   │   └── _Navbar.cshtml
│   │   ├── Dashboard/
│   │   ├── Plan/
│   │   ├── OKR/
│   │   ├── Presupuesto/
│   │   └── Reporte/
│   ├── wwwroot/
│   │   ├── css/  (design tokens, custom)
│   │   ├── js/   (módulos por sección)
│   │   └── lib/  (Bootstrap, jQuery, Chart.js, DHTMLX Gantt)
│   └── Program.cs
│
├── PE-GOL.API/
│   ├── Controllers/
│   │   ├── v1/
│   │   │   ├── AuthController.cs
│   │   │   ├── TenantController.cs
│   │   │   ├── CicloController.cs
│   │   │   ├── AreaController.cs
│   │   │   ├── ObjetivoCGController.cs
│   │   │   ├── PlanAccionController.cs
│   │   │   ├── OKRController.cs
│   │   │   ├── CAPEXController.cs
│   │   │   ├── OPEXController.cs
│   │   │   ├── ReporteController.cs
│   │   │   ├── DashboardController.cs   ← [implementado — HU-015, 2026-09-20]
│   │   │   └── NotificacionController.cs
│   ├── Middleware/
│   │   ├── TenantMiddleware.cs       ← inyecta tenant_id en contexto HTTP
│   │   ├── AuditMiddleware.cs        ← [diseño — NO implementado] la auditoría vive en BLL vía InsertLogAsync transaccional (patrón HU-001..008, ver ADR-003)
│   │   └── ExceptionMiddleware.cs    ← manejo global de errores
│   ├── Filters/
│   │   └── ValidationFilter.cs
│   └── Program.cs
│
├── PE-GOL.BLL/
│   ├── Interfaces/
│   │   ├── ITenantService.cs
│   │   ├── ICicloService.cs
│   │   ├── IAreaService.cs
│   │   ├── IObjetivoCGService.cs
│   │   ├── IPlanAccionService.cs
│   │   ├── IOKRService.cs
│   │   ├── ICAPEXService.cs
│   │   ├── IOPEXService.cs
│   │   ├── IReporteService.cs
│   │   ├── IDashboardService.cs     ← [implementado — HU-015, 2026-09-20]
│   │   └── INotificacionService.cs
│   └── Services/
│       ├── TenantService.cs
│       ├── CicloService.cs
│       ├── AreaService.cs
│       ├── ObjetivoCGService.cs
│       ├── PlanAccionService.cs      ← cálculo progreso ponderado, status automático
│       ├── OKRService.cs             ← cálculo KRs, puntuación trimestral/final
│       ├── CAPEXService.cs           ← cálculo desembolsos, status proyecto
│       ├── OPEXService.cs            ← cálculo memoria de cálculo, variación
│       ├── ReporteService.cs         ← generación PDF/XLSX
│       ├── DashboardService.cs       ← agregación de métricas [implementado — HU-015, 2026-09-20]
│       └── NotificacionService.cs    ← alertas correo, notificaciones in-app
│
├── PE-GOL.DAL/
│   ├── Interfaces/
│   │   └── I[Entidad]Repository.cs  (uno por entidad)
│   └── Repositories/
│       └── [Entidad]Repository.cs   (Dapper + SQL parametrizado)
│
├── PE-GOL.Entity/
│   ├── Saas/        (Tenant, Plan, Usuario, Rol, LogAuditoria)
│   ├── Ciclo/       (Ciclo, Configuracion, Umbral)
│   ├── Estrategia/  (Filosofia, Pilar, Area, ObjetivoCG)
│   ├── Plan/        (AccionPlan, Entregable, HistorialProgreso)
│   ├── OKR/         (OKR, KeyResult, ValorMensualKR)
│   ├── Presupuesto/ (ProyectoCapex, DesembolsoCapex, CuentaOpex,
│   │                 SubcuentaOpex, PresupuestoOpex, MemoriaCalculo)
│   └── Sistema/     (Notificacion, Alerta)
│
├── PE-GOL.DTO/
│   ├── Request/     ([Modulo]CreateRequest, [Modulo]UpdateRequest)
│   └── Response/    ([Modulo]Response, [Modulo]ListResponse, ApiResponse<T>)
│
├── PE-GOL.IOC/
│   └── DependencyContainer.cs       ← registro de todos los servicios y repos
│
└── PE-GOL.Utility/
    ├── SemaforoHelper.cs             ← evalúa umbrales, retorna color [implementado — HU-015, 2026-09-20]
    ├── JwtHelper.cs                  ← generación y validación de tokens
    ├── EmailService.cs               ← envío de correos (SMTP / SendGrid)
    ├── StorageHelper.cs              ← URLs firmadas Supabase Storage
    ├── PdfGenerator.cs              ← generación PDF (DinkToPdf / iTextSharp)
    ├── ExcelGenerator.cs            ← generación XLSX (ClosedXML)
    ├── Constants.cs                  ← constantes globales del sistema
    └── Extensions/
        ├── DateExtensions.cs
        └── DecimalExtensions.cs
```

---

## 4. Patrón Multi-Tenant

### 4.1 Flujo de resolución del Tenant

```
Request HTTP
    │
    ▼
[TenantMiddleware]
    │  Lee claim "tenant_id" del JWT
    │  Inyecta TenantContext en HttpContext.Items
    │
    ▼
[API Controller]
    │  Recibe TenantContext vía DI
    │
    ▼
[BLL Service]
    │  Pasa tenant_id a todos los métodos del repositorio
    │
    ▼
[DAL Repository]
    │  Todas las queries incluyen WHERE tenant_id = @TenantId
    │  RLS en PostgreSQL como segunda línea de defensa
    ▼
[Supabase / PostgreSQL]
    │  Row-Level Security Policy valida tenant_id en JWT claim
```

### 4.2 TenantContext

```csharp
public class TenantContext
{
    public Guid TenantId    { get; set; }
    public Guid UserId      { get; set; }
    public string Rol       { get; set; }  // SuperAdmin | AdminTenant | Gerente | JefeArea
    public Guid? AreaId     { get; set; }  // solo para rol JefeArea
    public Guid? CicloId    { get; set; }  // ciclo activo del tenant
}
```

### 4.3 Roles y acceso por capa

| Rol | Scope de datos | Restricción en DAL |
|-----|---------------|-------------------|
| SuperAdmin | Todos los tenants (solo metadatos) | Sin filtro de tenant |
| AdminTenant | Su tenant completo | `WHERE tenant_id = @TenantId` |
| Gerente | Su tenant · todas las áreas | `WHERE tenant_id = @TenantId` |
| JefeArea | Su tenant · su área | `WHERE tenant_id = @TenantId AND area_id = @AreaId` |

---

## 5. Flujo de Autenticación

```
[Browser] ──POST /api/v1/auth/login──▶ [API]
                                          │  Valida credenciales en BD
                                          │  Genera JWT (60 min) + Refresh Token (7 días)
                                          │  JWT Claims: user_id, tenant_id, rol, area_id
[Browser] ◀── { access_token, refresh_token } ──

[Browser] ──GET /api/v1/okrs (Bearer JWT)──▶ [TenantMiddleware]
                                                     │  Valida JWT
                                                     │  Extrae claims → TenantContext
                                                     ▼
                                              [Controller → BLL → DAL]
                                              (tenant_id en todas las queries)
```

---

## 6. Estrategia de Archivos Adjuntos (Entregables)

```
[JefeArea sube archivo]
         │
         ▼
[API Controller]  ──▶  [StorageHelper]  ──▶  [Supabase Storage]
                                                  Bucket: "entregables"
                                                  Path: /{tenant_id}/{ciclo_id}/{accion_id}/{uuid}.ext
                         ◀── file_path
         │
         ▼
[DAL] guarda en tabla entregable:
      accion_id, file_name, file_path, file_size, uploaded_by, uploaded_at

[JefeArea descarga]
         │
         ▼
[API] llama StorageHelper.GetSignedUrl(file_path, expiry: 24h)
         │
         ▼
[Browser] recibe URL firmada temporal → descarga directa desde Supabase Storage
```

---

## 7. Batch Jobs (Procesos Programados)

| Job | Frecuencia | Responsabilidad |
|-----|-----------|-----------------|
| `AlertaAccionesAtrasadasJob` | Diario · 02:00 AM | Detecta acciones que pasaron a Atrasado; envía correo al jefe responsable (una sola vez por acción) |
| `AlertaOKRsPeligroJob` | Mensual · día 1 · 03:00 AM | Evalúa puntuación acumulada de OKRs del mes cerrado; envía alerta al jefe y Gerente si < umbral amarillo |
| `ResumenGerencialJob` | Semanal · Lunes · 07:00 AM | Genera y envía correo de resumen consolidado al Gerente de cada tenant activo |
| `LimpiezaNotificacionesJob` | Semanal · Domingo · 01:00 AM | Elimina notificaciones in-app con más de 30 días de antigüedad |

Implementación: **Hosted Service** (`IHostedService`) + `System.Threading.Timer` en `PE-GOL.API`.

---

## 8. Librerías y Paquetes NuGet Principales

| Paquete | Versión | Uso |
|---------|---------|-----|
| `Dapper` | 2.1.x | ORM ligero para queries SQL en DAL |
| `Npgsql` | 8.x | Driver PostgreSQL para .NET |
| `FluentValidation.AspNetCore` | 11.x | Validación de DTOs en la API |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.x | Autenticación JWT |
| `ClosedXML` | 0.102.x | Generación de archivos Excel (.xlsx) |
| `DinkToPdf` | 1.0.8 | Generación de PDFs desde HTML |
| `Supabase` | 0.13.x | Cliente .NET para Supabase Storage |
| `Serilog.AspNetCore` | 8.x | Logging estructurado JSON |
| `Swashbuckle.AspNetCore` | 6.x | Documentación Swagger/OpenAPI |
| `BCrypt.Net-Next` | 4.x | Hashing de contraseñas |
| `MailKit` | 4.x | Envío de correos SMTP |

---

## 9. Variables de Entorno (appsettings)

```json
{
  "ConnectionStrings": {
    "Supabase": "Host=db.[ref].supabase.co;Database=postgres;Username=postgres;Password=***"
  },
  "Jwt": {
    "SecretKey": "***",
    "Issuer": "pe-gol-saas",
    "Audience": "pe-gol-users",
    "AccessTokenMinutes": 60,
    "RefreshTokenDays": 7
  },
  "Supabase": {
    "Url": "https://[ref].supabase.co",
    "AnonKey": "***",
    "ServiceKey": "***",
    "StorageBucket": "entregables"
  },
  "Email": {
    "Host": "smtp.sendgrid.net",
    "Port": 587,
    "User": "apikey",
    "Password": "***",
    "FromAddress": "noreply@pegol.dicegsa.com",
    "FromName": "PE-GOL SaaS"
  },
  "Logging": {
    "MinimumLevel": "Information"
  }
}
```

---

*Documento generado el 13/09/2026 · Fase 2 — Diseño Arquitectónico.*
*Consistente con arquitectura Freiroute TMS (N-Tier · 8 proyectos).*
