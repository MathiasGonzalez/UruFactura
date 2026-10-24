namespace UruFactura.Tests;

public class HoraUruguayTests
{
    [Fact]
    public void Hoy_ServidorEnUtcDespuesDeLas21DeUruguay_DevuelveLaFechaDeUruguay()
    {
        // 23:30 en Montevideo = 02:30 UTC del día siguiente.
        var reloj = new RelojFijo(new DateTimeOffset(2026, 6, 16, 2, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 6, 15), HoraUruguay.Hoy(reloj));
    }

    [Fact]
    public void Ahora_UsaElOffsetDeUruguay()
    {
        var ahora = HoraUruguay.Ahora(new RelojFijo(new DateTimeOffset(2026, 6, 16, 2, 30, 0, TimeSpan.Zero)));

        Assert.Equal(TimeSpan.FromHours(-3), ahora.Offset);
        Assert.Equal(new DateTime(2026, 6, 15, 23, 30, 0), ahora.DateTime);
    }
}
