namespace UruFactura;

/// <summary>Utilidades para el RUT (RUC) uruguayo de 12 dígitos.</summary>
public static class Rut
{
    // Ponderadores del dígito verificador (módulo 11) sobre los primeros 11 dígitos.
    private static readonly int[] Ponderadores = [4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    /// <summary>
    /// Indica si <paramref name="rut"/> tiene 12 dígitos y un dígito verificador correcto
    /// (módulo 11 con ponderadores 4-3-2-9-8-7-6-5-4-3-2; un resultado 10 es inválido).
    /// </summary>
    public static bool EsValido(string? rut)
    {
        if (rut is null || rut.Length != 12 || !rut.All(char.IsAsciiDigit))
            return false;

        var suma = 0;
        for (var i = 0; i < 11; i++)
            suma += (rut[i] - '0') * Ponderadores[i];

        var dv = 11 - suma % 11;
        if (dv == 11) dv = 0;
        return dv != 10 && dv == rut[11] - '0';
    }
}
