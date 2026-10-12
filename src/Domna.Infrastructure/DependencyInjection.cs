using Domna.Application.Abstractions;
using Domna.Application.Services;
using Domna.Infrastructure.Persistence;
using Domna.Infrastructure.Repositories;
using Domna.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Domna.Infrastructure;

/// <summary>Registro de dependencias de Infrastructure + Application en el contenedor DI.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AgregarDomna(
        this IServiceCollection services,
        JwtOptions jwtOptions,
        CorreoOptions correoOptions,
        string? connectionString = null,
        R2Options? r2Options = null)
    {
        // PostgreSQL si hay cadena de conexión (producción/Railway); si no, InMemory (demo/local).
        var pg = NormalizarConexionPostgres(connectionString);
        if (!string.IsNullOrWhiteSpace(pg))
            services.AddDbContext<DomnaDbContext>(o => o.UseNpgsql(pg));
        else
            services.AddDbContext<DomnaDbContext>(o => o.UseInMemoryDatabase("domna"));

        services.AddScoped<IClientaRepository, ClientaRepository>();
        services.AddScoped<IProductoRepository, ProductoRepository>();
        services.AddScoped<ICategoriaRepository, CategoriaRepository>();
        services.AddScoped<IColorRepository, ColorRepository>();

        services.AddSingleton(jwtOptions);
        services.AddSingleton(correoOptions);
        services.AddSingleton(r2Options ?? new R2Options());
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtGenerator, JwtGenerator>();
        services.AddSingleton<IReloj, RelojSistema>();
        services.AddSingleton<IAlmacenamientoImagenes, R2Almacenamiento>();

        // Correo real por SMTP si hay credenciales configuradas; si no, el simulado (solo loguea el token).
        if (correoOptions.Configurado)
            services.AddScoped<IServicioCorreo, SmtpCorreo>();
        else
            services.AddScoped<IServicioCorreo, CorreoSimulado>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPersonalService, PersonalService>();
        services.AddScoped<IProductoService, ProductoService>();
        services.AddScoped<ICategoriaService, CategoriaService>();
        services.AddScoped<IColorService, ColorService>();

        return services;
    }

    /// <summary>¿Hay cadena de conexión Postgres? (producción). Expuesto para que la Api decida migrar.</summary>
    public static bool UsaPostgres(string? connectionString) =>
        !string.IsNullOrWhiteSpace(NormalizarConexionPostgres(connectionString));

    /// <summary>
    /// Normaliza la cadena de conexión. Railway entrega una URL `postgresql://user:pass@host:port/db`
    /// (o `postgres://`); Npgsql espera formato clave=valor. Si ya viene en ese formato, se usa tal cual.
    /// </summary>
    public static string? NormalizarConexionPostgres(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var v = valor.Trim();
        if (!v.StartsWith("postgres://") && !v.StartsWith("postgresql://"))
            return v; // ya es formato Npgsql (Host=...;Username=...;...)

        var uri = new Uri(v);
        var userInfo = uri.UserInfo.Split(':', 2);
        var usuario = Uri.UnescapeDataString(userInfo[0]);
        var clave = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var baseDatos = uri.AbsolutePath.TrimStart('/');
        var puerto = uri.Port > 0 ? uri.Port : 5432;
        return $"Host={uri.Host};Port={puerto};Database={baseDatos};Username={usuario};Password={clave};SSL Mode=Require;Trust Server Certificate=true";
    }
}
