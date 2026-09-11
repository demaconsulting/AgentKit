using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     An <see cref="IToolPack"/> decorator that records whether the composition asked the pack
///     it wraps to create its tools.
/// </summary>
/// <remarks>
///     <para>
///     The capability gate has to prove more than that a non-vision host receives no image
///     tools: an empty tool list is produced both by a pack that was consulted and returned
///     nothing and by a pack that was never consulted at all, and only the second is the
///     behavior the gate promises. This decorator makes the distinction observable by setting
///     <see cref="WasConsulted"/> the moment <see cref="CreateTools"/> is entered.
///     </para>
///     <para>
///     Every member other than <see cref="CreateTools"/> delegates straight to the wrapped pack,
///     so the decorator behaves exactly as the real <see cref="ImagePack"/> the composition sees —
///     it declares the same family prefix and the same required capability, so the builder's gate
///     decision is made about the genuine pack and not about the decorator.
///     </para>
/// </remarks>
/// <param name="inner">The pack to wrap and observe.</param>
internal sealed class RecordingToolPack(IToolPack inner) : IToolPack
{
    /// <summary>
    ///     The pack whose consultation is being observed.
    /// </summary>
    private readonly IToolPack _inner = inner;

    /// <summary>
    ///     Gets a value indicating whether <see cref="CreateTools"/> has been invoked.
    /// </summary>
    /// <remarks>
    ///     Remains <see langword="false"/> until the composition asks the wrapped pack for its
    ///     tools. A gate that skips an unsupported pack leaves this false, which is the property
    ///     an empty tool list alone cannot establish.
    /// </remarks>
    public bool WasConsulted { get; private set; }

    /// <summary>
    ///     Gets the wrapped pack's family prefix, unchanged.
    /// </summary>
    public string FamilyPrefix => _inner.FamilyPrefix;

    /// <summary>
    ///     Gets the wrapped pack's required capabilities, unchanged.
    /// </summary>
    public HostCapabilities RequiredCapabilities => _inner.RequiredCapabilities;

    /// <summary>
    ///     Records the consultation and delegates to the wrapped pack.
    /// </summary>
    /// <param name="policy">The access policy the composition supplied.</param>
    /// <returns>The wrapped pack's tools.</returns>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // The flag is set here, at the single point the contract is honored, so that a gate
        // which never asks for the tools leaves it false.
        WasConsulted = true;
        return _inner.CreateTools(policy);
    }
}
