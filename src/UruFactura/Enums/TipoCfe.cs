namespace UruFactura;

/// <summary>
/// Tipos de CFE soportados, con el código numérico del formato CFE de la DGI (<c>TipoCFE</c>).
/// </summary>
public enum TipoCfe
{
    /// <summary>e-Ticket (101).</summary>
    ETicket = 101,
    /// <summary>Nota de crédito de e-Ticket (102).</summary>
    NotaCreditoETicket = 102,
    /// <summary>Nota de débito de e-Ticket (103).</summary>
    NotaDebitoETicket = 103,

    /// <summary>e-Ticket venta por cuenta ajena (131). Requiere <see cref="Cfe.Mandante"/>.</summary>
    ETicketCuentaAjena = 131,
    /// <summary>Nota de crédito de e-Ticket venta por cuenta ajena (132).</summary>
    NotaCreditoETicketCuentaAjena = 132,
    /// <summary>Nota de débito de e-Ticket venta por cuenta ajena (133).</summary>
    NotaDebitoETicketCuentaAjena = 133,

    /// <summary>e-Factura (111).</summary>
    EFactura = 111,
    /// <summary>Nota de crédito de e-Factura (112).</summary>
    NotaCreditoEFactura = 112,
    /// <summary>Nota de débito de e-Factura (113).</summary>
    NotaDebitoEFactura = 113,

    /// <summary>e-Factura de exportación (121).</summary>
    EFacturaExportacion = 121,
    /// <summary>Nota de crédito de e-Factura de exportación (122).</summary>
    NotaCreditoEFacturaExportacion = 122,
    /// <summary>Nota de débito de e-Factura de exportación (123).</summary>
    NotaDebitoEFacturaExportacion = 123,
    /// <summary>e-Remito de exportación (124).</summary>
    ERemitoExportacion = 124,

    /// <summary>e-Factura venta por cuenta ajena (141). Requiere <see cref="Cfe.Mandante"/>.</summary>
    EFacturaCuentaAjena = 141,
    /// <summary>Nota de crédito de e-Factura venta por cuenta ajena (142).</summary>
    NotaCreditoEFacturaCuentaAjena = 142,
    /// <summary>Nota de débito de e-Factura venta por cuenta ajena (143).</summary>
    NotaDebitoEFacturaCuentaAjena = 143,

    /// <summary>
    /// e-Boleta de entrada (151): la emite quien compra a un vendedor que no factura electrónicamente (no
    /// contribuyente, contribuyente IVA mínimo o Monotributo, o contribuyente IMEBA). El <see cref="Cfe.Receptor"/>
    /// es el vendedor.
    /// </summary>
    EBoleta = 151,
    /// <summary>Nota de crédito de e-Boleta de entrada (152).</summary>
    NotaCreditoEBoleta = 152,
    /// <summary>Nota de débito de e-Boleta de entrada (153).</summary>
    NotaDebitoEBoleta = 153,

    // --- Comprobantes de contingencia (CFC): misma estructura que el CFE que sustituyen ---

    /// <summary>e-Ticket de contingencia (201).</summary>
    ETicketContingencia = 201,
    /// <summary>Nota de crédito de e-Ticket de contingencia (202).</summary>
    NotaCreditoETicketContingencia = 202,
    /// <summary>Nota de débito de e-Ticket de contingencia (203).</summary>
    NotaDebitoETicketContingencia = 203,
    /// <summary>e-Factura de contingencia (211).</summary>
    EFacturaContingencia = 211,
    /// <summary>Nota de crédito de e-Factura de contingencia (212).</summary>
    NotaCreditoEFacturaContingencia = 212,
    /// <summary>Nota de débito de e-Factura de contingencia (213).</summary>
    NotaDebitoEFacturaContingencia = 213,
    /// <summary>e-Factura de exportación de contingencia (221).</summary>
    EFacturaExportacionContingencia = 221,
    /// <summary>Nota de crédito de e-Factura de exportación de contingencia (222).</summary>
    NotaCreditoEFacturaExportacionContingencia = 222,
    /// <summary>Nota de débito de e-Factura de exportación de contingencia (223).</summary>
    NotaDebitoEFacturaExportacionContingencia = 223,
    /// <summary>e-Remito de exportación de contingencia (224).</summary>
    ERemitoExportacionContingencia = 224,
    /// <summary>e-Ticket venta por cuenta ajena de contingencia (231).</summary>
    ETicketCuentaAjenaContingencia = 231,
    /// <summary>Nota de crédito de e-Ticket cuenta ajena de contingencia (232).</summary>
    NotaCreditoETicketCuentaAjenaContingencia = 232,
    /// <summary>Nota de débito de e-Ticket cuenta ajena de contingencia (233).</summary>
    NotaDebitoETicketCuentaAjenaContingencia = 233,
    /// <summary>e-Factura venta por cuenta ajena de contingencia (241).</summary>
    EFacturaCuentaAjenaContingencia = 241,
    /// <summary>Nota de crédito de e-Factura cuenta ajena de contingencia (242).</summary>
    NotaCreditoEFacturaCuentaAjenaContingencia = 242,
    /// <summary>Nota de débito de e-Factura cuenta ajena de contingencia (243).</summary>
    NotaDebitoEFacturaCuentaAjenaContingencia = 243,
    /// <summary>e-Boleta de entrada de contingencia (251).</summary>
    EBoletaContingencia = 251,
    /// <summary>Nota de crédito de e-Boleta de entrada de contingencia (252).</summary>
    NotaCreditoEBoletaContingencia = 252,
    /// <summary>Nota de débito de e-Boleta de entrada de contingencia (253).</summary>
    NotaDebitoEBoletaContingencia = 253,
    /// <summary>e-Remito de contingencia (281).</summary>
    ERemitoContingencia = 281,
    /// <summary>e-Resguardo de contingencia (282).</summary>
    EResguardoContingencia = 282,

    /// <summary>e-Remito (181).</summary>
    ERemito = 181,
    /// <summary>e-Resguardo (182). Informa retenciones/percepciones en <see cref="LineaDetalle.Retenciones"/>.</summary>
    EResguardo = 182,
}
