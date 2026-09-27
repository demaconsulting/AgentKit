using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The image tool family: the pack an application attaches to give a vision-capable agent
///     policy-governed reading of images and PDF documents, and extraction of a rectangular
///     region of an image.
/// </summary>
/// <remarks>
///     <para>
///     A pack is the unit of attachment, which is why this is the only public way to obtain the
///     family's tools. Each tool's factory is internal, so a tool cannot be constructed
///     outside the family that claims its prefix, and every tool is necessarily built through
///     <see cref="GuardedToolFactory"/> with the policy the composition supplies.
///     </para>
///     <para>
///     <b>No tool in the family is ever withheld by the access policy.</b> All three are
///     published under every policy, because each can succeed under one that permits no writing
///     anywhere: reading an image is a read, and taking or trimming a region without a
///     destination returns image content rather than writing a file. The region tools' optional
///     <c>destination</c> does require a write grant, and naming one under a read-only policy
///     earns an ordinary denial that enumerates the writable locations. See
///     <see cref="CreateTools"/> for why that is the right trade.
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
/// <example>
///     <para>
///     Capability gating. The pack is added only when the application can actually show an image to
///     the model, and the <see cref="HostCapabilities.Vision"/> declaration is what permits the pack
///     to contribute its tools. Without the declaration the composition never asks this pack for
///     tools at all, so <c>image_read</c> is not refused at call time — it is never offered.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var policy = new PathPolicy(workspace, [PathRule.ReadOnly(workspace)]);
///
///     var visionEnabled = true;
///
///     var builder = new ToolPackBuilder(policy).Add(new TextFilePack());
///     if (visionEnabled)
///     {
///         builder = builder
///             .WithHostCapabilities(HostCapabilities.Vision)
///             .Add(new ImagePack());
///     }
///
///     // With vision declared the list ends with the image family's tools; without it, the pack
///     // contributes nothing and the model never sees an image tool.
///     IReadOnlyList&lt;AIFunction&gt; tools = builder.Build();
///     </code>
/// </example>
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
    ///     Initializes a new instance of the <see cref="ImagePack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and grants nothing on its own: the policy that governs its
    ///     tools comes from the <see cref="ToolPackBuilder"/> the pack is added to, and the
    ///     vision capability that permits it to contribute tools is declared on that builder.
    ///     Declared explicitly rather than left implicit so that the documentation the package
    ///     ships describes every public member.
    /// </remarks>
    public ImagePack()
    {
    }

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
    ///     <para>
    ///     The family publishes three tools under every policy: the read tool, which returns a
    ///     whole image or PDF; the crop tool, which returns a rectangular region of one the
    ///     caller names; and the auto-crop tool, which returns the region the caller cannot name
    ///     because it is the region the image's own content occupies. They are created
    ///     together because they are one capability rather than three — a region request a model
    ///     cannot aim is a region request it will aim wrongly, the read tool is what reports
    ///     the coordinate space the crop tool consumes, and the auto-crop tool is the answer for
    ///     the common case in which the model can see that a picture is mostly margin but cannot
    ///     measure where the margin stops. The policy is passed to each tool's
    ///     factory and captured there, so no tool can later observe a different policy.
    ///     </para>
    ///     <para>
    ///     <b>This pack does not filter on the policy, deliberately.</b> The rule the file families
    ///     apply gates a tool on the writes it can only perform, not on whether every argument it
    ///     accepts could be used. All three tools here clear that bar under a read-only policy:
    ///     each region
    ///     tool's primary mode returns image content inline and writes nothing, so withholding
    ///     one would remove a fully working capability over an optional argument. Narrowing a
    ///     parameter is a
    ///     different question from publishing a tool, and conflating the two would mean every
    ///     optional argument needs a gate of its own.
    ///     </para>
    ///     <para>
    ///     <b>What that leaves un-gated.</b> A policy holding no grants at all still receives all
    ///     three tools, and <c>image_read</c> then refuses every path it is given, because
    ///     <see cref="PathPolicy.TryResolveRead"/> can admit none. That case is left alone on
    ///     purpose: <see cref="PathRule"/> offers only <see cref="AccessLevel.ReadOnly"/> and
    ///     <see cref="AccessLevel.ReadWrite"/>, so write access always implies read access and a
    ///     read tool is unusable only when nothing whatever is granted — a composition that
    ///     produces an agent unable to touch a file however its tool list is trimmed.
    ///     </para>
    ///     <para>
    ///     Nor does either region tool's description vary with the policy. The <c>destination</c>
    ///     parameter's description is a compile-time attribute and cannot vary, so a
    ///     policy-varying
    ///     tool description would ship inside the same declaration as a fixed parameter description
    ///     still offering the destination — a declaration contradicting itself in one payload. One
    ///     honest description plus an ordinary denial is the truthful arrangement; see the remarks
    ///     on <see cref="ImageCropTool"/> and <see cref="ImageAutoCropTool"/>.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>The read tool, the crop tool and the auto-crop tool, under every policy.</returns>
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
            ImageReadTool.Create(policy),
            ImageCropTool.Create(policy),
            ImageAutoCropTool.Create(policy)
        ];
    }
}
