using System.Text;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Models;

namespace UruFactura.Cae;

/// <summary>
/// Gestiona los CAE (Constancias de Autorización de Emisión) de la DGI.
/// Controla vencimientos, rangos disponibles y alertas.
/// Esta clase es thread-safe.
/// </summary>
public class CaeManager : ICaeManager
{
    private readonly Dictionary<TipoCfe, List<Models.Cae>> _caesPorTipo = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Registra un CAE en el gestor. Si ya hay uno con la misma serie, tipo y rango, lo reemplaza.
    /// </summary>
    /// <param name="cae">CAE a registrar.</param>
    public virtual void RegistrarCae(Models.Cae cae)
    {
        ArgumentNullException.ThrowIfNull(cae);

        lock (_lock)
            AgregarCaeSinLock(cae);
    }

    /// <summary>
    /// Registra múltiples CAEs a la vez usando un solo lock para el lote completo.
    /// Valida que ningún elemento sea null antes de modificar el estado interno.
    /// </summary>
    public virtual void RegistrarCaes(IEnumerable<Models.Cae> caes)
    {
        ArgumentNullException.ThrowIfNull(caes);

        // Snapshotear siempre (ToList) para evitar que un caller mute la colección entre la
        // validación y el lock. Luego validar la copia antes de modificar el estado interno.
        var lista = caes.ToList();
        for (var i = 0; i < lista.Count; i++)
        {
            if (lista[i] is null)
                throw new ArgumentNullException(nameof(caes), $"El elemento en el índice {i} es null.");
        }

        lock (_lock)
        {
            foreach (var cae in lista)
                AgregarCaeSinLock(cae);
        }
    }

    // Debe llamarse con _lock adquirido.
    private void AgregarCaeSinLock(Models.Cae cae)
    {
        if (!_caesPorTipo.TryGetValue(cae.TipoCfe, out var lista))
            _caesPorTipo[cae.TipoCfe] = lista = [];

        // Registrar dos veces el mismo CAE (misma serie, tipo y rango) lo reemplaza en vez de duplicarlo:
        // así es seguro volver a cargar los CAEs desde el repositorio.
        var existente = lista.FindIndex(c =>
            c.NroSerie == cae.NroSerie && c.RangoDesde == cae.RangoDesde && c.RangoHasta == cae.RangoHasta);
        if (existente >= 0)
            lista[existente] = cae;
        else
            lista.Add(cae);
    }

    /// <summary>
    /// Obtiene el próximo número de comprobante para el tipo de CFE indicado
    /// y el CAE activo correspondiente.
    /// </summary>
    /// <param name="tipoCfe">Tipo de CFE para el que se necesita un número.</param>
    /// <returns>Tupla (cae, número).</returns>
    /// <exception cref="CaeException">Si no hay CAE vigente o disponible.</exception>
    public virtual (Models.Cae Cae, long Numero) ObtenerProximoNumero(TipoCfe tipoCfe)
    {
        lock (_lock)
        {
            // Elegir e incrementar bajo el mismo lock evita que dos hilos tomen el último
            // número del mismo CAE y el segundo falle habiendo otro CAE disponible.
            var caeActivo = ObtenerCaeActivoInterno(tipoCfe)
                ?? throw new CaeException(
                    $"No hay CAE vigente y con números disponibles para el tipo {tipoCfe}.");

            return (caeActivo, caeActivo.ObtenerProximoNumero());
        }
    }

    /// <summary>
    /// Obtiene el CAE activo para el tipo de CFE indicado.
    /// Prioriza el CAE con más números disponibles y que no esté por vencer.
    /// </summary>
    /// <param name="tipoCfe">Tipo de CFE.</param>
    /// <returns>CAE activo, o null si no existe.</returns>
    public virtual Models.Cae? ObtenerCaeActivo(TipoCfe tipoCfe)
    {
        lock (_lock)
        {
            return ObtenerCaeActivoInterno(tipoCfe);
        }
    }

    /// <summary>
    /// Retorna todas las advertencias activas de los CAEs registrados.
    /// </summary>
    /// <param name="diasAlertaVencimiento">Días de anticipación para alertar vencimiento.</param>
    /// <param name="porcentajeAlertaUso">Porcentaje de uso a partir del cual alertar.</param>
    public virtual IReadOnlyList<string> ObtenerAdvertencias(
        int diasAlertaVencimiento = 7,
        decimal porcentajeAlertaUso = 80m) =>
        Todos()
            .Select(c => c.ObtenerAdvertencia(diasAlertaVencimiento, porcentajeAlertaUso))
            .OfType<string>()
            .ToList();

    /// <summary>
    /// Retorna todos los CAEs registrados.
    /// </summary>
    public virtual IReadOnlyList<Models.Cae> ObtenerTodosLosCaes() => Todos();

    /// <summary>
    /// Retorna todos los CAEs vigentes con números disponibles.
    /// </summary>
    public virtual IReadOnlyList<Models.Cae> ObtenerCaesActivos() =>
        Todos().Where(c => c.EsVigente && c.TieneNumerosDisponibles).ToList();

    /// <summary>
    /// Retorna un resumen de estado de los CAEs.
    /// </summary>
    public virtual string ResumenEstado()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Estado de CAEs ===");

        foreach (var lista in Todos().GroupBy(c => c.TipoCfe))
        {
            sb.AppendLine($"\nTipo CFE: {lista.Key} ({(int)lista.Key})");
            foreach (var cae in lista)
            {
                var estado = cae.EsVigente
                    ? (cae.TieneNumerosDisponibles ? "✅ Activo" : "⚠️ Sin números")
                    : "❌ Vencido";

                sb.AppendLine($"  Serie: {cae.NroSerie} | {estado}");
                sb.AppendLine($"  Rango: {cae.RangoDesde}-{cae.RangoHasta} | Usado hasta: {cae.UltimoNroUsado}");
                sb.AppendLine($"  Vence: {cae.FechaVencimiento:dd/MM/yyyy} | Uso: {cae.PorcentajeUso}%");
            }
        }

        return sb.ToString();
    }

    // Thread-safety: This method must be called while holding _lock.
    // Do not call from public methods without first acquiring the lock.
    private Models.Cae? ObtenerCaeActivoInterno(TipoCfe tipoCfe)
    {
        // Primero el CAE con mayor fecha de vencimiento; a igual vencimiento, el que tenga más
        // números disponibles (comparación lexicográfica de la tupla). Ante empate, el primero.
        return _caesPorTipo.TryGetValue(tipoCfe, out var lista)
            ? lista.Where(c => c.EsVigente && c.TieneNumerosDisponibles)
                   .MaxBy(c => (c.FechaVencimiento, c.RangoHasta - c.UltimoNroUsado))
            : null;
    }

    private List<Models.Cae> Todos()
    {
        lock (_lock)
            return _caesPorTipo.Values.SelectMany(l => l).ToList();
    }
}
