namespace WatchForge.Interfaces.Library;

/// <summary>
/// Časová abstrakcia — testovateľnosť (SystemClock pre produkciu, FakeClock v testoch).
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Poskytovateľ času pre doménovú logiku (oddelené od IClock pre špecifické potreby).
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
