using System.Security.Cryptography.X509Certificates;

namespace UruFactura.Signature;

/// <summary>Carga de certificados PKCS#12 con las banderas adecuadas para cada plataforma.</summary>
internal static class CertificadoLoader
{
    /// <summary>
    /// Carga un .p12/.pfx. Usa <see cref="X509KeyStorageFlags.EphemeralKeySet"/> (la clave no se
    /// persiste) salvo en macOS, donde ese modo no está soportado.
    /// </summary>
    internal static X509Certificate2 Cargar(string ruta, string password)
    {
        var flags = OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

        return X509CertificateLoader.LoadPkcs12FromFile(ruta, password, flags);
    }
}
