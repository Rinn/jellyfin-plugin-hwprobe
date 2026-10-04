using System.Globalization;
using Xunit;

namespace Jellyfin.Plugin.HwProbe.TestSupport;

/// <summary>Switches the current culture for a test and restores it on dispose.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    /// <summary>Initializes a new instance of the <see cref="CultureScope"/> class.</summary>
    /// <param name="name">The culture to switch to, e.g. <c>de-DE</c>.</param>
    public CultureScope(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
    }

    /// <summary>Gets cultures that format numbers differently from the invariant one: decimal commas, a Unicode minus sign, other digits.</summary>
    public static TheoryData<string> Different { get; } = ["de-DE", "sv-SE", "ar-SA", "fa-IR"];

    /// <inheritdoc/>
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
