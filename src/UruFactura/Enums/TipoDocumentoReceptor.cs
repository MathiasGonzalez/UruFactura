namespace UruFactura;

/// <summary>
/// Tipos de documento del receptor según el DocType del XSD de la DGI.
/// Valores oficiales: 2 RUC, 3 CI, 4 Otro, 5 Pasaporte, 6 DNI, 7 NIFE.
/// </summary>
public enum TipoDocumentoReceptor
{
    /// <summary>RUT (Registro Único Tributario) — 2</summary>
    Rut = 2,

    /// <summary>Cédula de Identidad — 3</summary>
    CedulaIdentidad = 3,

    /// <summary>Otro (por ejemplo, un documento del exterior sin tipo propio) — 4</summary>
    Otro = 4,

    /// <summary>Pasaporte — 5</summary>
    Pasaporte = 5,

    /// <summary>DNI — 6</summary>
    Dni = 6,

    /// <summary>NIFE — 7</summary>
    Nife = 7,
}
