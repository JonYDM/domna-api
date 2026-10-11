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

// Cadena de conexión: Railway/producción la inyecta como DATABASE_URL o ConnectionStrings__Default.
// Si no hay, el backend corre con InMemory (demo/local).
var connectionString =
    builder.Configuration["ConnectionStrings:Default"]
    ?? builder.Configuration["DATABASE_URL"]
    ?? Environment.GetEnvironmentVariable("DATABASE_URL");

// Capas Application + Infrastructure (Postgres si hay cadena, si no InMemory).
builder.Services.AgregarDomna(jwtOptions, correoOptions, connectionString);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().WithOrigins(origenes)));

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

// Migración + seed al arrancar.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DomnaDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    // En Postgres aplicamos las migraciones (crea/actualiza las tablas). InMemory no las necesita.
    if (DependencyInjection.UsaPostgres(connectionString))
        db.Database.Migrate();
    Seed.Sembrar(db, hasher);
}

app.UseMiddleware<ManejadorErrores>();

// Swagger disponible también en producción (para probar la API en Railway).
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>Punto de entrada expuesto para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program { }
