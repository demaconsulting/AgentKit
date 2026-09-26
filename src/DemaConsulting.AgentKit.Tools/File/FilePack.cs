using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The file tool family: the pack an application attaches to give an agent policy-governed
///     listing, copying, moving and deletion of files of any type.
/// </summary>
/// <remarks>
///     <para>
///     A pack is the unit of attachment, which is why this is the only public way to obtain the
///     family's tools. Each tool's factory is internal, so a tool cannot be constructed outside
///     the family that claims its prefix, and every tool is necessarily built through
///     <see cref="GuardedToolFactory"/> with the policy the composition supplies.
///     </para>
///     <para>
///     <b>The family is type-agnostic.</b> It treats a file as an entity — a thing to list, copy,
///     move or delete — rather than as text, an image, or any other kind of content. That is why it
///     is a family of its own rather than part of the text family: the same four operations apply to
///     a file whatever it holds, and separating them from content reading keeps each family's job
///     clear. The text family reads and edits the <em>contents</em> of a text file; this family
///     manages the file itself.
///     </para>
///     <para>
///     <b>One of the four tools is published under every policy; three need a write grant.</b>
///     <c>file_list</c> consults only the read decision, so it is always published. <c>file_copy</c>,
///     <c>file_move</c> and <c>file_delete</c> each change the file system, so the pack withholds all
///     three when <see cref="PathPolicy.AnyLocationIsWritable"/> is <see langword="false"/> — see
///     <see cref="CreateTools"/>.
///     </para>
///     <para>
///     <see cref="FamilyPrefix"/> is published as a constant as well as through the contract, so
///     that a test or a composing application can name the family without repeating a string
///     literal that could drift from the names the tools actually carry.
///     </para>
///     <para>
///     <b>The family requires no host capability.</b> Managing files needs nothing of the model or
///     the application beyond what every host already provides, so every host receives the family.
///     That is a separate gate from the write-grant filtering described above: the host capability
///     decides whether the pack is asked for tools at all, and the policy decides which of its tools
///     it then returns. A host that grants nothing still receives this family; a policy that permits
///     no writing still receives <c>file_list</c>.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family. The pack requires no host capability, so it is registered by every
///     composition. Under a policy that permits writing somewhere it publishes four tools, in this
///     order: <c>file_list</c>, <c>file_copy</c>,
///     <c>file_move</c>, and <c>file_delete</c>. The policy below grants a read-write session
///     location, so all four appear; had every grant been read-only, <c>file_list</c> would be the
///     whole list. Every one of them is governed by the policy the
///     builder was constructed with — the pack itself grants nothing.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var session = Path.GetFullPath("session");
///
///     var policy = new PathPolicy(
///         workingDirectory: workspace,
///         grants: [PathRule.ReadOnly(workspace), PathRule.ReadWrite(session)]);
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .Add(new FilePack())
///         .Build();
///
///     // file_list, file_copy, file_move, file_delete — every name carries the family prefix.
///     var names = tools.Select(tool =&gt; tool.Name).ToList();
///     var prefix = FilePack.FamilyPrefix;
///     </code>
/// </example>
public sealed class FilePack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "file";

    /// <summary>
    ///     Initializes a new instance of the <see cref="FilePack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and grants nothing on its own: the policy that governs its
    ///     tools comes from the <see cref="ToolPackBuilder"/> the pack is added to, not from
    ///     construction. Declared explicitly rather than left implicit so that the documentation
    ///     the package ships describes every public member.
    /// </remarks>
    public FilePack()
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
    ///     <see cref="HostCapabilities.None"/>: managing files asks nothing of the host, so the
    ///     family is registered by every composition rather than gated behind a declaration an
    ///     application would have to know to make.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The order — list, copy, move, delete — is fixed rather than incidental, because the order
    ///     a model sees the tools in is observable. When tools are withheld the survivors keep their
    ///     relative order, so a model never sees the family rearranged, only shortened. The policy is
    ///     passed to each tool's factory and
    ///     captured there, so no tool in the family can observe a different policy from its neighbor.
    ///     </para>
    ///     <para>
    ///     <b>The pack declares what tools exist; the policy decides which can function.</b> Copy,
    ///     move and delete each change the file system, so under a policy holding no read-write grant
    ///     anywhere they could only ever return a refusal — and are therefore not published at all,
    ///     rather than spending a declaration and a model's attention on a capability that cannot
    ///     work. This is the reasoning <see cref="ToolPackBuilder.Build"/> already applies one level
    ///     up, where a pack whose required capabilities the host did not grant is never asked for its
    ///     tools, applied one level finer. The question asked is
    ///     <see cref="PathPolicy.AnyLocationIsWritable"/> — a fact about the whole policy, not about
    ///     any path — so a policy granting a read-only workspace and a writable session location
    ///     publishes every tool, because writing there is genuinely possible.
    ///     </para>
    ///     <para>
    ///     <c>file_list</c> is never withheld. It consults the read decision to choose what to list,
    ///     and the write decision only to annotate a listed root as writable, so it remains fully
    ///     useful under a policy that permits no writing.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>
    ///     The list, copy, move and delete tools, in that order, when the policy permits writing in
    ///     at least one location; otherwise just the list tool. Never null and never containing a
    ///     null element.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to each tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        // Listing consults only the read decision, so it opens the family under any policy.
        List<AIFunction> tools = [FileListTool.Create(policy)];

        // Asked once, of the policy as a whole: can this composition write anywhere at all? The
        // three tools that change the file system are published only when it can.
        if (policy.AnyLocationIsWritable)
        {
            tools.Add(FileCopyTool.Create(policy));
            tools.Add(FileMoveTool.Create(policy));
            tools.Add(FileDeleteTool.Create(policy));
        }

        return tools;
    }
}
