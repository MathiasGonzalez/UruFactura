namespace UruFactura;

/// <summary>Utilidades para la cédula de identidad uruguaya (C.I.).</summary>
public static class Ci
{
    // Ponderadores del dígito verificador (módulo 10) sobre los primeros 7 dígitos.
    private static readonly int[] Ponderadores = [2, 9, 8, 7, 6, 3, 4];

    /// <summary>
    /// Indica si <paramref name="ci"/> es una cédula con dígito verificador correcto: de 6 a 8 dígitos sin puntos ni
    /// guión, el último el verificador (módulo 10 con ponderadores 2-9-8-7-6-3-4 sobre el número completado con
    /// ceros a la izquierda hasta 7 dígitos).
    /// </summary>
    public static bool EsValida(string? ci)
    {
        if (ci is null || ci.Length is < 6 or > 8 || !ci.All(char.IsAsciiDigit))
            return false;
        var digitos = ci.PadLeft(8, '0');
        var suma = 0;
        for (var i = 0; i < 7; i++)
            suma += (digitos[i] - '0') * Ponderadores[i];
        return (10 - suma % 10) % 10 == digitos[7] - '0';
    }
}
