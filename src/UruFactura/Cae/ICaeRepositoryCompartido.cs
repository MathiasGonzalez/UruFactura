using UruFactura.Enums;

namespace UruFactura.Cae;

/// <summary>
/// Repositorio de CAE compartido entre varias instancias de la aplicación (escalado horizontal,
/// deploys blue/green, una API y un worker, etc.). La numeración —de los CFE y de los envíos a la
/// DGI— se reserva de forma atómica en el almacenamiento, que pasa a ser la fuente de verdad: dos
/// instancias nunca obtienen el mismo número.
/// <para>
/// Cuando el <see cref="ICaeRepository"/> configurado en el cliente implementa esta interfaz,
/// <c>UruFacturaClient.AsignarCaeAsync</c> reserva el número aquí en lugar de usar el
/// <see cref="ICaeManager"/> en memoria, y <c>AsignarCae</c> (sincrónico) deja de estar disponible.
/// </para>
/// <para>
/// La implementación debe ser atómica frente a otras instancias: por ejemplo, un
/// <c>UPDATE ... SET UltimoNroUsado = @nuevo WHERE Id = @id AND UltimoNroUsado = @leido</c>
/// reintentado si no afecta filas. El paquete <c>UruFactura.EntityFrameworkCore</c> incluye una.
/// </para>
/// </summary>
public interface ICaeRepositoryCompartido : ICaeRepository
{
    /// <summary>
    /// Reserva el próximo número del CAE vigente para <paramref name="tipoCfe"/>, con el mismo
    /// criterio que <see cref="CaeManager"/>: primero el de vencimiento más lejano y, a igual
    /// vencimiento, el que tenga más números disponibles.
    /// </summary>
    /// <param name="tipoCfe">Tipo de CFE a numerar.</param>
    /// <param name="hoy">Fecha actual en Uruguay (los CAE vencidos antes de esta fecha se ignoran).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>El CAE (con <see cref="Models.Cae.UltimoNroUsado"/> igual al número reservado) y el número; <c>null</c> si no hay CAE vigente con números disponibles.</returns>
    ValueTask<(Models.Cae Cae, long Numero)?> ReservarProximoNumeroAsync(
        TipoCfe tipoCfe, DateOnly hoy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva el número de envío del emisor (<c>Idemisor</c> del sobre e <c>IDEmisor</c> del reporte
    /// diario, hasta 10 dígitos), único entre todas las instancias.
    /// </summary>
    ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default);
}
