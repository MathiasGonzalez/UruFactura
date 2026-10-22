namespace UruFactura.EntityFrameworkCore;

/// <summary>Contador con nombre (tabla <c>UruFacturaSecuencias</c>), p. ej. el número de envío del emisor.</summary>
public class SecuenciaEntity
{
    public string Nombre { get; set; } = string.Empty;
    public long Valor { get; set; }
}
