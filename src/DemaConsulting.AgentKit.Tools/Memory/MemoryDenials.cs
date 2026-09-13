using System.Globalization;
using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The refusals more than one memory tool has to compose, held in one place so that the three
///     tools which address a memory by identifier refuse a wrong one identically.
/// </summary>
/// <remarks>
///     <para>
///     <b>A denial states what happened and stops.</b> It does not name a tool to use instead, does
///     not suggest recalling first, and does not guess at the identifier the model meant. This is a
///     project-wide rule learned expensively: a denial that helpfully said "use
///     <c>text_file_replace</c> instead" was followed by a model destroying a file. Whatever a tool
///     emits is a demonstration the model imitates, and a denial that prescribes teaches the model
///     that prescriptions in tool output are to be acted on.
///     </para>
///     <para>
///     <b>The held identifiers are deliberately not listed.</b> The task-list family — see
///     <c>TodoPack</c> — names the identifiers its list holds, because a task list is a
///     handful of items the model itself named.
///     A memory store may hold thousands of machine-assigned identifiers, so listing them would
///     spend the context budget the recall is for and tell the model nothing it could use.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class MemoryDenials
{
    /// <summary>
    ///     Composes the refusal for an identifier the store does not hold.
    /// </summary>
    /// <remarks>
    ///     The size of the store is stated because it distinguishes the two situations a model
    ///     confuses: an identifier it mistyped, and an identifier it invented for a memory that was
    ///     never stored — the second of which is far likelier against an empty store.
    /// </remarks>
    /// <param name="store">The store that was searched.</param>
    /// <param name="memoryId">The identifier the model stated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The refusal, as a returned value rather than an exception.</returns>
    public static async Task<object> NotFoundAsync(
        IMemoryStore store,
        string memoryId,
        CancellationToken cancellationToken)
    {
        var count = await store.CountAsync(cancellationToken).ConfigureAwait(false);

        return ToolResult.Denied(
            DenialReason.TargetNotFound,
            "No memory carries the id '" + memoryId + "'. The store holds "
            + count.ToString(CultureInfo.InvariantCulture)
            + (count == 1 ? " memory." : " memories."));
    }

    /// <summary>
    ///     Composes the refusal for a tool call that named no memory.
    /// </summary>
    /// <remarks>
    ///     Stated once rather than per tool, so that three tools cannot drift into three different
    ///     wordings for one mistake — a drift a model reads as three different problems.
    /// </remarks>
    /// <returns>The refusal, as a returned value rather than an exception.</returns>
    public static object MissingIdentifier()
    {
        return ToolResult.Denied(
            DenialReason.InvalidRequest,
            "A memory id is required. A recall reports the id of every memory it returns, and "
            + "filing a memory reports the id it was given.");
    }
}
