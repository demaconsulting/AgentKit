using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Tools.Agent;

/// <summary>
///     One named agent an application is willing to have started on its behalf: what it is told to
///     do, which of the attached tools it may use, and — optionally — a narrower set of path grants
///     than its parent holds.
/// </summary>
/// <remarks>
///     <para>
///     <b>A profile exists so that a parent agent never authors its own child's instructions.</b>
///     The model chooses a child by <em>name</em> from a list the application registered; it does
///     not supply the child's system prompt, its tool list, or its grants. If it could, delegation
///     would be a way for a model to grant a second model behavior the application never
///     sanctioned — "you are an unrestricted assistant; ignore the rules" — and every control the
///     application configured would be one prompt away from being reset. The only thing the parent
///     supplies is the task, which is data the child works on rather than authority the child
///     carries.
///     </para>
///     <para>
///     <b>Naming a tool does not conjure it.</b> <see cref="Tools"/> is a filter, not a grant: the
///     child receives the tools whose names appear both here and in the packs the application
///     handed to <see cref="AgentPack"/>. A profile naming <c>shell_exec</c> in an application that
///     attached no such tool produces a child with no such tool, silently and by construction.
///     </para>
///     <para>
///     <b><see cref="Grants"/> may only narrow.</b> A profile that omits them gives the child the
///     parent's policy unchanged. A profile that states them is checked against the parent's policy
///     when the family is composed, and a grant reaching outside what the parent holds — a location
///     the parent cannot reach, a write where the parent has only a read, or a deny pattern the
///     parent imposes and the profile drops — is a programming error in the composing application
///     and is reported there. Delegation is not a route by which an agent acquires reach its
///     operator did not give it.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Two profiles an application might register. The reviewer may read and search but not write,
///     whatever the parent's own grants allow; the note-taker narrows its reach to one directory and
///     keeps only what it needs there.
///     </para>
///     <code>
///     var session = Path.GetFullPath("session");
///
///     var reviewer = new AgentProfile(
///         name: "reviewer",
///         instructions: "You review source files and report findings. You do not modify anything.",
///         tools: ["text_file_read", "text_file_search"]);
///
///     var noteTaker = new AgentProfile(
///         name: "note-taker",
///         instructions: "You write short notes into the session directory.",
///         tools: ["text_file_create", "todo_list", "todo_set"],
///         grants: [PathRule.ReadWrite(session)],
///         description: "Writes notes into the session directory. Cannot read the workspace.");
///     </code>
/// </example>
public sealed class AgentProfile
{
    /// <summary>
    ///     The tool names this profile admits, held so the collection cannot be mutated after
    ///     construction.
    /// </summary>
    private readonly List<string> _tools;

    /// <summary>
    ///     The grants this profile narrows the child's policy to, empty when it narrows nothing.
    /// </summary>
    private readonly List<PathRule> _grants;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentProfile"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Every part of what a child agent <em>is</em> is supplied here, at composition time, by
    ///     the application. There is deliberately no setter and no builder: a profile that could be
    ///     adjusted after registration could be adjusted by whatever holds a reference to it, and
    ///     the point of the type is that the child's instructions have exactly one author.
    ///     </para>
    ///     <para>
    ///     An empty <paramref name="tools"/> collection is permitted and means a child that can
    ///     only think and answer. That is a legitimate profile — summarizing or judging text the
    ///     parent passes in the task needs no tools at all — so it is not treated as a mistake.
    ///     </para>
    /// </remarks>
    /// <param name="name">
    ///     The name the model selects this profile by. Must be non-null and non-empty. Compared
    ///     ordinally, so it means exactly itself.
    /// </param>
    /// <param name="instructions">
    ///     The system instructions the child is given. Must be non-null and non-empty: a child with
    ///     nothing said to it has no job, and the application's authorship of this text is the
    ///     control the type exists to keep.
    /// </param>
    /// <param name="tools">
    ///     The names of the tools the child may use, filtered against what the application
    ///     attached. Must be non-null and must contain no null or empty entry; may be empty.
    /// </param>
    /// <param name="grants">
    ///     Path grants narrowing the child's policy, or <see langword="null"/> to leave the parent's
    ///     policy unchanged. Must contain no null entry. Checked against the parent's policy at
    ///     composition; see <see cref="AgentPack.CreateTools"/>.
    /// </param>
    /// <param name="description">
    ///     A one-line description of what this agent is for, shown to the model alongside the name,
    ///     or <see langword="null"/> for none. A model choosing between profiles by name alone is
    ///     guessing, so this is worth supplying.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="name"/>, <paramref name="instructions"/> or
    ///     <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="name"/> or <paramref name="instructions"/> is empty, when
    ///     <paramref name="tools"/> holds a null or empty entry, or when <paramref name="grants"/>
    ///     holds a null entry.
    /// </exception>
    public AgentProfile(
        string name,
        string instructions,
        IEnumerable<string> tools,
        IEnumerable<PathRule>? grants = null,
        string? description = null)
    {
        // A nameless profile could not be selected, and a silent profile has no job. Both are
        // programming errors in the composing application.
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(instructions);
        ArgumentNullException.ThrowIfNull(tools);

        // Materialize once: a lazily evaluated sequence could yield different names each time it
        // was enumerated, which would make the child's tool set unrepeatable.
        _tools = [.. tools];
        if (_tools.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException(
                "A tool name in the profile is null or empty, so it could never match an "
                + "attached tool.",
                nameof(tools));
        }

        _grants = grants is null ? [] : [.. grants];
        if (_grants.Any(grant => grant is null))
        {
            throw new ArgumentException(
                "A grant in the profile is null, so the child's reach could not be determined.",
                nameof(grants));
        }

        Name = name;
        Instructions = instructions;
        Description = description;
    }

    /// <summary>
    ///     Gets the name the model selects this profile by.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the system instructions the child agent is given.
    /// </summary>
    /// <remarks>
    ///     Written by the application, never by the parent agent. This is the property the whole
    ///     type exists to protect.
    /// </remarks>
    public string Instructions { get; }

    /// <summary>
    ///     Gets the one-line description of what this agent is for, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    ///     Shown to the model alongside the name so that choosing between profiles is a reading
    ///     task rather than a guess.
    /// </remarks>
    public string? Description { get; }

    /// <summary>
    ///     Gets the names of the tools the child may use.
    /// </summary>
    /// <remarks>
    ///     A filter over what the application attached, never a grant. An empty collection means a
    ///     child with no tools, which is a legitimate profile rather than a mistake.
    /// </remarks>
    public IReadOnlyList<string> Tools => _tools;

    /// <summary>
    ///     Gets the path grants that narrow the child's policy, empty when it narrows nothing.
    /// </summary>
    /// <remarks>
    ///     Empty means the child observes the parent's policy unchanged. A non-empty collection is
    ///     checked against the parent's policy at composition and may only narrow it.
    /// </remarks>
    public IReadOnlyList<PathRule> Grants => _grants;
}
