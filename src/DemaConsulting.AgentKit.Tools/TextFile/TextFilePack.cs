using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The text file tool family: the pack an application attaches to give an agent policy-governed
///     searching, reading and editing of the contents of text files.
/// </summary>
/// <remarks>
///     <para>
///     A pack is the unit of attachment, which is why this is the only public way to obtain the
///     family's tools. Each tool's factory is internal, so a tool cannot be constructed outside the
///     family that claims its prefix, and every tool is necessarily built through
///     <see cref="GuardedToolFactory"/> with the policy the composition supplies.
///     </para>
///     <para>
///     <b>This family reads and edits the <em>contents</em> of a text file.</b> It searches across
///     files, reads a ranged, line-numbered window of one, creates a new file, sets a file's whole
///     content, replaces exact text,
///     and cuts, copies and pastes ranges of lines. Managing the files themselves — listing, copying,
///     moving
///     and deleting them regardless of type — belongs to the sibling <c>file</c> family, and reading
///     the section structure of a Markdown file belongs to the <c>markdown</c> family. Keeping those
///     jobs in separate families is what lets each tool's description say one clear thing.
///     </para>
///     <para>
///     <b>Three of the eight tools are published under every policy; five need a write grant.</b>
///     <c>text_file_search</c>, <c>text_file_read</c> and <c>text_file_copy_lines</c> consult only
///     the read decision, so they are always published. <c>text_file_create</c>,
///     <c>text_file_write</c>, <c>text_file_replace</c>, <c>text_file_cut_lines</c> and
///     <c>text_file_paste_lines</c> can act only by writing, so the pack withholds all five when
///     <see cref="PathPolicy.AnyLocationIsWritable"/> is <see langword="false"/> — see
///     <see cref="CreateTools"/>.
///     </para>
///     <para>
///     <b>Navigation is by line number; editing in place is by content.</b> Search, read, cut and
///     copy address lines by number, while replace addresses text by exact content. The spike observed
///     models mixing the two dialects freely and correctly — searching for a line number, then
///     replacing by matched content — so the family offers both rather than forcing one.
///     </para>
///     <para>
///     <b>The write, cut, copy and paste tools share one buffer per composition.</b> Every call to
///     <see cref="CreateTools"/> allocates a fresh <see cref="TextFileLineBuffers"/> and gives it to
///     whichever of the write, cut, copy and paste tools it publishes, so a range cut or copied in
///     one call can be pasted back in another against the same file set, and content a write
///     displaced can be restored from the slot it was captured into, while two independently
///     composed tool sets never share slots. The buffer lives exactly as long as the tools do. Under
///     a policy that permits no writing, <c>text_file_copy_lines</c> is the only tool holding the
///     buffer, so what it fills has no drain — <c>paste</c> is the buffer's only reader and the
///     buffer deliberately offers no peek or clear tool. Copy stays published nonetheless, because
///     the rule a pack applies is "could the policy permit this tool to succeed", which a pack can
///     evaluate, and not "is this tool useful", which it cannot.
///     </para>
///     <para>
///     <see cref="FamilyPrefix"/> is published as a constant as well as through the contract, so that
///     a test or a composing application can name the family without repeating a string literal that
///     could drift from the names the tools actually carry.
///     </para>
///     <para>
///     <b>The family requires no host capability.</b> Reading and editing text needs nothing of the
///     model or the application beyond what every host already provides, so every host receives the
///     family. That is a separate gate from the write-grant filtering described above: the host
///     capability decides whether the pack is asked for tools at all, and the policy decides which of
///     its tools it then returns. A host that grants nothing still receives this family; a policy
///     that permits no writing still receives its three reading tools.
///     </para>
///     <para>
///     The class is stateless — the per-composition buffer lives on the tools it creates, not on the
///     pack — and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family. The pack requires no host capability, so it is registered by every
///     composition. Under a policy that permits writing somewhere it publishes eight tools, in this
///     order: <c>text_file_search</c>,
///     <c>text_file_read</c>, <c>text_file_create</c>, <c>text_file_write</c>,
///     <c>text_file_replace</c>, <c>text_file_cut_lines</c>, <c>text_file_copy_lines</c>, and
///     <c>text_file_paste_lines</c>. The policy below grants a read-write session location, so all
///     eight appear; had every grant been read-only, the three reading tools —
///     <c>text_file_search</c>, <c>text_file_read</c> and <c>text_file_copy_lines</c> — would be the
///     whole list. Every one of them is governed by
///     the policy the builder was constructed with — the pack itself grants nothing.
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
///         .Add(new TextFilePack())
///         .Build();
///
///     // text_file_search, text_file_read, ... — every name carries the family prefix.
///     var names = tools.Select(tool =&gt; tool.Name).ToList();
///     var prefix = TextFilePack.FamilyPrefix;
///     </code>
/// </example>
public sealed class TextFilePack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "text_file";

    /// <summary>
    ///     Initializes a new instance of the <see cref="TextFilePack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and grants nothing on its own: the policy that governs its
    ///     tools comes from the <see cref="ToolPackBuilder"/> the pack is added to, not from
    ///     construction, and the cut/paste buffer is allocated per <see cref="CreateTools"/> call
    ///     rather than held on the pack. Declared explicitly rather than left implicit so that the
    ///     documentation the package ships describes every public member.
    /// </remarks>
    public TextFilePack()
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
    ///     <see cref="HostCapabilities.None"/>: text file access asks nothing of the host, so the
    ///     family is registered by every composition rather than gated behind a declaration an
    ///     application would have to know to make.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The order — search, read, create, write, replace, cut, copy, paste — is fixed rather than
    ///     incidental, because the order a model sees the tools in is observable. It descends in
    ///     scope through the three editing tools: bring a file into existence, set its whole content,
    ///     change part of its content. When tools are withheld the survivors keep their relative
    ///     order, so a model never sees the family rearranged, only shortened. A fresh
    ///     <see cref="TextFileLineBuffers"/> is allocated here and shared between whichever of the
    ///     write, cut, copy and paste tools are published, giving the buffer exactly the lifetime of
    ///     this composition's tools. The
    ///     policy is passed to each tool's factory and captured there, so no tool in the family can
    ///     observe a different policy from its neighbor.
    ///     </para>
    ///     <para>
    ///     <b>The pack declares what tools exist; the policy decides which can function.</b> A tool
    ///     that can act only by writing, under a policy holding no read-write grant anywhere, could
    ///     only ever return a refusal — so it is not published at all, rather than spending a
    ///     declaration and a model's attention on a capability that cannot work. This is the
    ///     reasoning <see cref="ToolPackBuilder.Build"/> already applies one level up, where a pack
    ///     whose required capabilities the host did not grant is never asked for its tools, applied
    ///     one level finer. The question asked is
    ///     <see cref="PathPolicy.AnyLocationIsWritable"/> — a fact about the whole policy, not about
    ///     any path — so a policy granting a read-only workspace and a writable session location
    ///     publishes every tool, because writing there is genuinely possible.
    ///     </para>
    ///     <para>
    ///     <c>text_file_copy_lines</c> is deliberately not among the write-gated tools: it consults
    ///     only the read decision, by the design decision recorded on
    ///     <see cref="TextFileCopyLinesTool"/>, and so can succeed under any policy that permits
    ///     reading at all.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>
    ///     The search, read, create, write, replace, cut, copy and paste tools, in that order, when
    ///     the policy permits writing in at least one location; otherwise just the search, read and
    ///     copy tools, in that order. Never null and never containing a null element.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to each tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        // One buffer per composition, shared by write, cut, copy and paste: a range cut here can be
        // pasted back here, content a write displaced can be restored here, and two separately
        // composed tool sets never share slots. Allocated even under a read-only policy, because
        // copy is still published and still needs somewhere to put what it copies.
        var buffers = new TextFileLineBuffers();

        // Asked once, of the policy as a whole: can this composition write anywhere at all? The
        // five tools that can act only by writing are published only when it can.
        var writable = policy.AnyLocationIsWritable;

        // Search and read consult only the read decision, so they open the family under any policy.
        List<AIFunction> tools =
        [
            TextFileSearchTool.Create(policy),
            TextFileReadTool.Create(policy)
        ];

        // The four write-performing tools that precede copy in the fixed order.
        if (writable)
        {
            tools.Add(TextFileCreateTool.Create(policy));
            tools.Add(TextFileWriteTool.Create(policy, buffers));
            tools.Add(TextFileReplaceTool.Create(policy));
            tools.Add(TextFileCutLinesTool.Create(policy, buffers));
        }

        // Copy consults the read decision only, so it survives a read-only policy. It is added
        // between the two write-gated groups rather than after them, because the published order is
        // part of the contract and copy sits seventh in it.
        tools.Add(TextFileCopyLinesTool.Create(policy, buffers));

        // Paste is the buffer's only reader and writes what it drains, so it is write-gated too.
        if (writable)
        {
            tools.Add(TextFilePasteLinesTool.Create(policy, buffers));
        }

        return tools;
    }
}
