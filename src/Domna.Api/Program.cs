using System.Text;
using Domna.Api;
using Domna.Application.Abstractions;
using Domna.Infrastructure;
using Domna.Infrastructure.Persistence;
using Domna.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Railway (y otros PaaS) asignan el puerto por la variable PORT. Si existe, escuchamos ahí.
var puerto = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(puerto))
    builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");

// Opciones del JWT desde configuración (sección "Jwt"); con valores por defecto para la demo.
var jwtOptions = new JwtOptions();
builder.Configuration.GetSection("Jwt").Bind(jwtOptions);

// Opciones de correo (sección "Correo"); se admiten variables de entorno (Correo__Password, etc.).
var correoOptions = new CorreoOptions();
builder.Configuration.GetSection("Correo").Bind(correoOptions);

// Opciones de R2 (sección "R2" / variables R2__*). Llaves secretas, solo en el servidor.
var r2Options = new R2Options();
builder.Configuration.GetSection("R2").Bind(r2Options);

// Cadena de conexión: Railway/producción la inyecta como DATABASE_URL o ConnectionStrings__Default.
// Si no hay, el backend corre con InMemory (demo/local).
var connectionString =
    builder.Configuration["ConnectionStrings:Default"]
    ?? builder.Configuration["DATABASE_URL"]
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

// Capas Application + Infrastructure (Postgres si hay cadena, si no InMemory).
builder.Services.AgregarDomna(jwtOptions, correoOptions, connectionString, r2Options);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Compresión de respuestas (Brotli + Gzip) para JSON. EnableForHttps = true porque detrás del
// proxy de Railway el tráfico interno puede ir cifrado y aun así queremos comprimir.
// PRECAUCIÓN BREACH: NO se comprime nada bajo /api/auth/* (ahí viajan tokens); comprimir contenido
// que mezcla secreto + entrada reflejada abre la puerta a ataques tipo BREACH/CRIME.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = new[]
    {
        "application/json",
        "application/problem+json",
        "text/json",
    };
});

// Filtro reutilizable de idempotencia (se usa con [Idempotente] en los POST sensibles).
builder.Services.AddScoped<Domna.Api.IdempotenciaFilter>();

// Orígenes del front permitidos por CORS: por env (Cors__Origenes, separados por coma) o localhost.
var origenes = (builder.Configuration["Cors:Origenes"] ?? "http://localhost:5173,http://127.0.0.1:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// Autenticación JWT (HS256) con la misma clave del generador.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret))
        };
    });
builder.Services.AddAuthorization();

// CORS para el front (dev local y producción). Orígenes configurables por env.
// Exponemos ETag (para los GET condicionales del catálogo) e Idempotent-Replay (para que el front
// sepa que un POST reintentado fue un replay y no un alta nueva).
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().WithOrigins(origenes)
     .WithExposedHeaders("ETag", "Idempotent-Replay")));

// Swagger con soporte de Bearer para probar endpoints protegidos.
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Domna API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pega el token JWT (sin el prefijo 'Bearer ')."
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

var app = builder.Build();

app.Logger.LogInformation(
    "Correo: {Modo}",
    correoOptions.Configurado ? $"SMTP real vía {correoOptions.Host} ({correoOptions.Usuario})" : "simulado (solo log)");
app.Logger.LogInformation(
    "Imágenes R2: {Modo}",
    r2Options.Configurado ? $"activo (bucket {r2Options.Bucket})" : "no configurado");

// Migración + seed al arrancar.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DomnaDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    var usaPg = DependencyInjection.UsaPostgres(connectionString);
    app.Logger.LogInformation(
        "Base de datos: {Modo} (DATABASE_URL {Estado})",
        usaPg ? "PostgreSQL" : "InMemory",
        string.IsNullOrWhiteSpace(connectionString) ? "AUSENTE" : "presente");

    if (usaPg)
    {
        try
        {
            db.Database.Migrate(); // crea/actualiza las tablas en Postgres
            app.Logger.LogInformation("Migraciones aplicadas correctamente en PostgreSQL.");
        }
        catch (Exception ex)
        {
            // Si falla, lo gritamos en los logs (antes caía silencioso a datos efímeros).
            app.Logger.LogError(ex, "FALLÓ la conexión/migración a PostgreSQL. Revisa DATABASE_URL.");
            throw; // mejor que el deploy falle visible a que corra con datos que se pierden
        }
    }
    Seed.Sembrar(db, hasher);
}

app.UseMiddleware<ManejadorErrores>();

// Swagger disponible también en producción (para probar la API en Railway).
app.UseSwagger();
app.UseSwaggerUI();

// Compresión: va temprano (envuelve el stream antes de que los controllers escriban), pero DESPUÉS
// del manejador de errores. BREACH: no comprimimos /api/auth/* (ahí viajan tokens).
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase),
    rama => rama.UseResponseCompression());

app.UseCors();

// Política de caché "segura por defecto" para /api: no-store salvo lista blanca, ETag/304 incluido.
// Va DESPUÉS de CORS (para que las cabeceras CORS ya estén) y ANTES de auth/controllers.
app.UseMiddleware<CacheHttpMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Punto de entrada expuesto para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program { }
