using System.ComponentModel;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The <c>memory_forget</c> tool: removes one memory from the store.
/// </summary>
/// <remarks>
///     <para>
///     <b>Forgetting is the only way a memory leaves the store, and it takes an explicit
///     identifier.</b> There is no clear-all, no forget-by-query and no expiry. A bulk removal is
///     an operation whose blast radius the model cannot see before it acts — a query that matches
///     more than it meant to erases evidence nothing will report as missing — and an expiry policy
///     would be this library deciding how long an author's facts stay true.
///     </para>
///     <para>
///     <b>Removal is permanent and this tool says so plainly rather than guarding against it.</b>
///     What protects an author here is that the tool has to be attached at all: an application that
///     does not want an agent forgetting things composes the family without this tool's pack
///     member, which is the author's decision to make and not this library's.
///     </para>
///     <para>
///     <b>An identifier the store does not hold is a refusal, not a quiet success.</b> A model told
///     that a removal succeeded when nothing was removed goes on believing a fact is gone from a
///     store that still returns it on the next recall.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the store it was given.
///     </para>
/// </remarks>
public static class MemoryForgetTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "memory_forget";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Removes one memory permanently. Supply the id of the memory to remove, as a recall or a "
        + "file call reported it. Only one memory is removed per call and it cannot be recovered. "
        + "Returns the size of the store after the removal.";

    /// <summary>
    ///     Creates the <c>memory_forget</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment. An application
    ///     obtains this tool by attaching <see cref="MemoryPack"/>.
    /// </remarks>
    /// <param name="store">The store this tool removes from.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(IMemoryStore store)
    {
        // A missing store is a programming error in the composing family.
        ArgumentNullException.ThrowIfNull(store);

        // Declared to return Task<object> on purpose; see the remarks on GuardedToolFactory.
        var forget = (
                [Description("The id of the memory to remove, as a recall or a file call reported it.")]
                string? memoryId = null,
                CancellationToken cancellationToken = default) =>
            ForgetAsync(store, memoryId, cancellationToken);

        return GuardedToolFactory.Create(forget, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Removes one memory, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <param name="store">The store to remove from.</param>
    /// <param name="memoryId">The identifier the model supplied, or null when it supplied none.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A structured statement of what the store now holds, or a refusal.</returns>
    private static async Task<object> ForgetAsync(
        IMemoryStore store,
        string? memoryId,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return MemoryDenials.MissingIdentifier();
        }

        var removed = await store.RemoveAsync(memoryId, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return await MemoryDenials.NotFoundAsync(store, memoryId, cancellationToken).ConfigureAwait(false);
        }

        var count = await store.CountAsync(cancellationToken).ConfigureAwait(false);

        // The size of the store after the removal is stated so the model does not have to recall
        // to find out what its own removal did.
        return ToolResult.Structured(new
        {
            forgotten = true,
            memoryId,
            memoryCount = count,
        });
    }
}
