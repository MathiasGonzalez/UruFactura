namespace UruFactura;

/// <summary>
/// Tipo de documento del receptor, del mandante o del propietario de la mercadería (<c>DocType</c>). El valor es
/// el código de la DGI. Los documentos uruguayos (NIE, RUT y C.I.) son del país <c>UY</c>; el DNI, de Argentina,
/// Brasil, Chile o Paraguay.
/// </summary>
public enum TipoDocumentoReceptor
{
    /// <summary>NIE, número de identificación de extranjero (Uruguay) — 1</summary>
    Nie = 1,

    /// <summary>RUT (Registro Único Tributario) — 2</summary>
    Rut = 2,

    /// <summary>Cédula de Identidad (Uruguay) — 3</summary>
    CedulaIdentidad = 3,

    /// <summary>Otro (por ejemplo, un documento del exterior sin tipo propio) — 4</summary>
    Otro = 4,

    /// <summary>Pasaporte (de cualquier país) — 5</summary>
    Pasaporte = 5,

    /// <summary>DNI (Argentina, Brasil, Chile o Paraguay) — 6</summary>
    Dni = 6,

    /// <summary>NIFE, número de identificación fiscal extranjero (de cualquier país) — 7</summary>
    Nife = 7,
}
