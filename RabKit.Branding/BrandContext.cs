namespace RabKit.Branding;

/// <summary>
/// Process-wide static accessor for the currently-active brand
/// (<see cref="IBrandConfig"/>) and industry profile
/// (<see cref="IIndustryProfile"/>). Wired exactly once from
/// <c>Program.Main</c> via <see cref="Initialize"/>; every other layer
/// (export services, UI forms, license verifier) reads from
/// <see cref="Current"/> / <see cref="CurrentIndustry"/>.
///
/// <para>
/// The contract is deliberately strict: accessing the properties before
/// <see cref="Initialize"/> throws, so a forgotten wiring shows up
/// loudly at startup instead of producing a half-branded app.
/// </para>
/// </summary>
public static class BrandContext
{
    private static IBrandConfig?      _config;
    private static IIndustryProfile?  _industry;
    private static readonly object    _gate = new();

    /// <summary>
    /// Currently-active brand configuration. Throws
    /// <see cref="InvalidOperationException"/> when accessed before
    /// <see cref="Initialize"/>.
    /// </summary>
    public static IBrandConfig Current
    {
        get
        {
            if (_config == null)
                throw new InvalidOperationException(
                    "BrandContext.Initialize() must be called from Program.Main " +
                    "before any other code accesses BrandContext.");
            return _config;
        }
    }

    /// <summary>
    /// Currently-active industry profile. Throws
    /// <see cref="InvalidOperationException"/> when accessed before
    /// <see cref="Initialize"/>.
    /// </summary>
    public static IIndustryProfile CurrentIndustry
    {
        get
        {
            if (_industry == null)
                throw new InvalidOperationException(
                    "BrandContext.Initialize() must be called from Program.Main " +
                    "before any other code accesses BrandContext.");
            return _industry;
        }
    }

    /// <summary>
    /// Initialize the static brand + industry singletons. Safe to call
    /// multiple times (e.g. in tests) — replaces the previous values.
    /// Both arguments are required; passing null throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    public static void Initialize(IBrandConfig config, IIndustryProfile industry)
    {
        lock (_gate)
        {
            _config   = config   ?? throw new ArgumentNullException(nameof(config));
            _industry = industry ?? throw new ArgumentNullException(nameof(industry));
        }
    }

    /// <summary>
    /// Test-only: reset both singletons so the next access throws again.
    /// Production code should never call this.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (_gate)
        {
            _config = null;
            _industry = null;
        }
    }

    /// <summary>True once <see cref="Initialize"/> has been called.</summary>
    public static bool IsInitialized
    {
        get
        {
            lock (_gate) { return _config != null && _industry != null; }
        }
    }
}
