namespace UruFactura;

/// <summary>
/// Ya se emitió un CFE del mismo tipo con el mismo <see cref="Cfe.NroInterno"/> (y la DGI no lo
/// rechazó): no se emitió otro. Suele ser el reintento de una emisión que sí se completó; el CFE ya
/// emitido está en <see cref="Existente"/>.
/// </summary>
public class CfeDuplicadoException : UruFacturaException
{
    /// <summary>El CFE emitido antes con el mismo tipo y número interno.</summary>
    public CfeEmitido Existente { get; }

    public CfeDuplicadoException(CfeEmitido existente)
        : base($"Ya se emitió el CFE {existente} con NroInterno '{existente.Documento.NroInterno}': no se emite otro.")
    {
        Existente = existente;
    }
}
