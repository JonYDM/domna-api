namespace Domna.Application.Dtos;

/// <summary>Aviso in-app, derivado del estado de los apartados (no se persiste, solo si está leído).</summary>
public record AvisoDto(
    string Id,
    string Tipo,
    string Titulo,
    string Texto,
    DateTime Fecha,
    bool Leido,
    bool Urgente,
    string ApartadoId);
