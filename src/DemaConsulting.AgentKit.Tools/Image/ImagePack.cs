using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The image tool family: the pack an application attaches to give a vision-capable agent
///     policy-governed reading of images and PDF documents.
/// </summary>
/// <remarks>
///     <para>
///     A pack is the unit of attachment, which is why this is the only public way to obtain the
///     family's tools. The read tool's factory is internal, so a tool cannot be constructed
///     outside the family that claims its prefix, and every tool is necessarily built through
///     <see cref="GuardedToolFactory"/> with the policy the composition supplies.
///     </para>
///     <para>
///     <see cref="FamilyPrefix"/> is published as a constant as well as through the contract, so
///     that a test or a composing application can name the family without repeating a string
///     literal that could drift from the names the tools actually carry.
///     </para>
///     <para>
///     <b>The family requires the host to be vision-capable.</b> Its tools return image content,
///     which is useful only to a host that can present that content to a model. A host that has
///     not declared <see cref="HostCapabilities.Vision"/> receives none of the family's tools —
///     the composition never even asks this pack to create them — so a model without eyes is
///     never offered a tool that returns an image it cannot see and would then confidently
///     describe from nothing.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public sealed class ImagePack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "image";

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Implemented explicitly so that the constant above can keep the name the contract
    ///     uses. The class is sealed, so the member is not hidden from a derived type.
    /// </remarks>
    string IToolPack.FamilyPrefix => FamilyPrefix;

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.Vision"/>: the family returns image content, which is of
    ///     no use to a host that cannot present it to a model. Declaring the requirement is what
    ///     lets the composition withhold the family from a host that cannot see, rather than
    ///     offering a tool whose result the model can only fabricate around.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.Vision;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     The family publishes a single tool today; it is still returned as a collection because
    ///     the pack contract is a collection and because a family grows without its callers
    ///     changing. The policy is passed to the tool's factory and captured there, so the tool
    ///     cannot later observe a different policy.
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>The read tool.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to the tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        return
        [
            ImageReadTool.Create(policy)
        ];
    }
}
