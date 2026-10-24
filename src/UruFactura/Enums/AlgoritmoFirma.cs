namespace UruFactura;

/// <summary>
/// Algoritmo de firma/digest usado en la firma del CFE y en la firma WS-Security.
/// </summary>
public enum AlgoritmoFirma
{
    /// <summary>RSA-SHA256 (por defecto).</summary>
    Sha256,

    /// <summary>
    /// RSA-SHA1. Es el algoritmo que usan los ejemplos de la documentación de la DGI;
    /// usar solo si la DGI rechaza SHA-256 en el ambiente utilizado.
    /// </summary>
    Sha1,
}
