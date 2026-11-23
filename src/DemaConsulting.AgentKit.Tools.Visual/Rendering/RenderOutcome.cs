namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     The result of one render: either the picture, encoded as PNG, with everything the render
///     reported about it, or the classified failure.
/// </summary>
/// <remarks>
///     <para>
///     Exactly one of <see cref="Png"/> and <see cref="Failure"/> is set. Construct an instance
///     only through <see cref="Success"/> or <see cref="Failed"/>, which keep that invariant.
///     </para>
///     <para>
///     The collections are private copies exposed read-only, so nothing a caller does to the
///     lists it passed in can change an outcome after it was made. The class is immutable.
///     </para>
/// </remarks>
internal sealed class RenderOutcome
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RenderOutcome"/> class.
    /// </summary>
    /// <param name="png">The encoded picture, or null for a failure.</param>
    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">The picture's height in pixels.</param>
    /// <param name="format">The format the library drew.</param>
    /// <param name="page">The 1-based page that was drawn.</param>
    /// <param name="pageCount">The number of pages the content has.</param>
    /// <param name="notes">The formatted notes of the render.</param>
    /// <param name="features">The formatted features of the picture.</param>
    /// <param name="featuresTruncated">Whether features were left out.</param>
    /// <param name="dominantColors">The formatted dominant colors.</param>
    /// <param name="failure">The classified failure, or null for a success.</param>
    private RenderOutcome(
        byte[]? png,
        int width,
        int height,
        string format,
        int page,
        int pageCount,
        IEnumerable<string> notes,
        IEnumerable<string> features,
        bool featuresTruncated,
        IEnumerable<string> dominantColors,
        RenderFailure? failure)
    {
        Png = png;
        Width = width;
        Height = height;
        Format = format;
        Page = page;
        PageCount = pageCount;
        Notes = Array.AsReadOnly(notes.ToArray());
        Features = Array.AsReadOnly(features.ToArray());
        FeaturesTruncated = featuresTruncated;
        DominantColors = Array.AsReadOnly(dominantColors.ToArray());
        Failure = failure;
    }

    /// <summary>
    ///     Gets a value indicating whether the render produced a picture.
    /// </summary>
    public bool Succeeded => Failure is null;

    /// <summary>
    ///     Gets the picture encoded as an opaque PNG, or <see langword="null"/> for a failure.
    /// </summary>
    public byte[]? Png { get; }

    /// <summary>
    ///     Gets the picture's width in pixels; zero for a failure.
    /// </summary>
    public int Width { get; }

    /// <summary>
    ///     Gets the picture's height in pixels; zero for a failure.
    /// </summary>
    public int Height { get; }

    /// <summary>
    ///     Gets the format the library drew, such as <c>mermaid</c>, <c>svg</c> or <c>png</c>;
    ///     empty for a failure.
    /// </summary>
    public string Format { get; }

    /// <summary>
    ///     Gets the 1-based page that was drawn.
    /// </summary>
    public int Page { get; }

    /// <summary>
    ///     Gets the number of pages the content has; 1 for content without pages.
    /// </summary>
    public int PageCount { get; }

    /// <summary>
    ///     Gets every note the render reported, formatted for a model.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>
    ///     Gets the features the picture was described by, when a description was asked for.
    /// </summary>
    public IReadOnlyList<string> Features { get; }

    /// <summary>
    ///     Gets a value indicating whether features were left out of <see cref="Features"/>.
    /// </summary>
    public bool FeaturesTruncated { get; }

    /// <summary>
    ///     Gets the picture's dominant colors, when a description was asked for.
    /// </summary>
    public IReadOnlyList<string> DominantColors { get; }

    /// <summary>
    ///     Gets the classified failure, or <see langword="null"/> for a success.
    /// </summary>
    public RenderFailure? Failure { get; }

    /// <summary>
    ///     Creates the outcome of a render that produced a picture.
    /// </summary>
    /// <param name="png">The encoded picture. Must not be null.</param>
    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">The picture's height in pixels.</param>
    /// <param name="format">The format the library drew. Must not be null.</param>
    /// <param name="page">The 1-based page that was drawn.</param>
    /// <param name="pageCount">The number of pages the content has.</param>
    /// <param name="notes">The formatted notes of the render; copied.</param>
    /// <param name="features">The formatted features; copied.</param>
    /// <param name="featuresTruncated">Whether features were left out.</param>
    /// <param name="dominantColors">The formatted dominant colors; copied.</param>
    /// <returns>The successful outcome.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public static RenderOutcome Success(
        byte[] png,
        int width,
        int height,
        string format,
        int page,
        int pageCount,
        IEnumerable<string> notes,
        IEnumerable<string> features,
        bool featuresTruncated,
        IEnumerable<string> dominantColors)
    {
        ArgumentNullException.ThrowIfNull(png);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(dominantColors);

        return new RenderOutcome(
            png, width, height, format, page, pageCount, notes, features, featuresTruncated, dominantColors, null);
    }

    /// <summary>
    ///     Creates the outcome of a render that failed.
    /// </summary>
    /// <param name="failure">The classified failure. Must not be null.</param>
    /// <returns>The failed outcome, carrying the failure's notes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="failure"/> is null.</exception>
    public static RenderOutcome Failed(RenderFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return new RenderOutcome(null, 0, 0, string.Empty, 1, 1, failure.Notes, [], false, [], failure);
    }
}
