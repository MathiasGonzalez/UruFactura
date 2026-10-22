using System.Security.Cryptography.X509Certificates;

namespace UruFactura.Signature;

/// <summary>Carga del certificado de firma.</summary>
internal static class CertificadoLoader
{
    /// <summary>
    /// Carga un .p12/.pfx. Usa <see cref="X509KeyStorageFlags.EphemeralKeySet"/> (la clave no se
    /// persiste en el sistema) salvo en macOS, donde ese modo no está soportado.
    /// </summary>
    /// <exception cref="FirmaDigitalException">Si el archivo no existe, la contraseña es incorrecta o no tiene clave privada.</exception>
    internal static X509Certificate2 Cargar(string ruta, string? password)
    {
        if (!File.Exists(ruta))
            throw new FirmaDigitalException($"No se encontró el archivo de certificado: '{ruta}'.");

        X509Certificate2 certificado;
        try
        {
            var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
            certificado = X509CertificateLoader.LoadPkcs12FromFile(ruta, password, flags);
        }
        catch (Exception ex)
        {
            throw new FirmaDigitalException(
                $"No se pudo cargar el certificado '{ruta}' (¿contraseña incorrecta o archivo dañado?).", ex);
        }

        try
        {
            ValidarClavePrivada(certificado);
            return certificado;
        }
        catch
        {
            certificado.Dispose();
            throw;
        }
    }

    /// <exception cref="FirmaDigitalException">Si el certificado no tiene clave privada.</exception>
    internal static void ValidarClavePrivada(X509Certificate2 certificado)
    {
        if (!certificado.HasPrivateKey)
            throw new FirmaDigitalException("El certificado no contiene clave privada.");
    }
}
