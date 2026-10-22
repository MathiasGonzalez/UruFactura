namespace UruFactura.Models;

/// <summary>Medio de pago informado en el comprobante (zona <c>MediosPago</c>).</summary>
public class MedioPago
{
    /// <summary>Identificación detallada del medio de pago (hasta 100 caracteres).</summary>
    public string Glosa { get; set; } = string.Empty;

    /// <summary>Valor del pago.</summary>
    public decimal Valor { get; set; }

    /// <summary>Código asignado al medio de pago (hasta 3 dígitos, opcional).</summary>
    public int? Codigo { get; set; }

    /// <summary>Orden de impresión (1-99, opcional).</summary>
    public int? Orden { get; set; }
}
