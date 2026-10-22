namespace UruFactura;

/// <summary>
/// Hora oficial de Uruguay (UTC−03:00, sin horario de verano desde 2015).
/// El SDK la usa para las fechas fiscales (fecha de emisión, vigencia del CAE, fecha de firma)
/// en lugar de la zona horaria del servidor, que en contenedores y nubes suele ser UTC: entre las
/// 21:00 y las 23:59 de Uruguay la fecha UTC ya es la del día siguiente.
/// </summary>
public static class HoraUruguay
{
    /// <summary>Desfase de la hora de Uruguay respecto de UTC.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(-3);

    /// <summary>Fecha y hora actual en Uruguay.</summary>
    /// <param name="reloj">Reloj a usar (por defecto el del sistema); útil en tests.</param>
    public static DateTimeOffset Ahora(TimeProvider? reloj = null) =>
        (reloj ?? TimeProvider.System).GetUtcNow().ToOffset(Offset);

    /// <summary>Fecha actual en Uruguay.</summary>
    /// <param name="reloj">Reloj a usar (por defecto el del sistema); útil en tests.</param>
    public static DateOnly Hoy(TimeProvider? reloj = null) =>
        DateOnly.FromDateTime(Ahora(reloj).DateTime);
}
