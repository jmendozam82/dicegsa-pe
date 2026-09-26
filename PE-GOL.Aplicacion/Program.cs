using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using PE_GOL.Aplicacion.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// ── Data protection: persiste las llaves en disco (App_Data/ bajo ContentRoot). La llave
//    por defecto es efímera por proceso → reiniciar invalidaba el cookie de sesión del
//    usuario (warn "Error unprotecting the session cookie" y bounce a Login). Con
//    PersistKeysToFileSystem el usuario no pierde la sesión en cada restart de dev.
var claveDp = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(claveDp);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(claveDp, "keys-data-protection")));

// ── Sesión MVC (cookie de sesión ASP.NET Core) — guarda JWT + usuario (HU-045 cimiento).
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SesionService>();

// ── Cliente HTTP tipado hacia la API interna (04_ARQUITECTURA § 1).
//    BaseAddress desde Api:BaseUrl (appsettings) — nunca hardcodeada (spec HU-045).
builder.Services.AddHttpClient<IApiClient, ApiClient>(client =>
{
    var baseUrl = builder.Configuration["Api:BaseUrl"]
        ?? throw new InvalidOperationException("Api:BaseUrl no está configurada en appsettings.json.");
    client.BaseAddress = new Uri(baseUrl);
});

// ── Autenticación MVC con cookie (D-4): el cookie autentica la capa MVC ([Authorize(Roles)]);
//    el JWT viaja en sesión y lo adjunta el ApiClient a la API (SEC-01/04).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Auth}/{action=Login}/{id?}");

app.Run();