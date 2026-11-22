namespace DemaConsulting.AgentKit.Tools.Visual.Diagram;

/// <summary>
///     How <c>diagram_render</c> shows the model what it drew.
/// </summary>
/// <remarks>
///     <para>
///     The preview mode is a property of the host, not of a diagram: a model that can see receives
///     the picture, and a model that cannot receives a description of it in words. The mode is
///     chosen once, when the pack is constructed — usually through
///     <see cref="DiagramPack.For"/>, which reads it from the host's capabilities — so that the tool a
///     model is offered describes exactly the result it returns.
///     </para>
/// </remarks>
public enum DiagramPreview
{
    /// <summary>
    ///     The rendered picture is returned as image content for a vision-capable model to look at.
    ///     Saving it with <c>save_as</c> is optional.
    /// </summary>
    Image,

    /// <summary>
    ///     The rendered picture is saved with <c>save_as</c>, which is then required, and the model
    ///     receives a description of it — its size, the labels and features it contains, and its
    ///     dominant colors — instead of the picture.
    /// </summary>
    Text
}
