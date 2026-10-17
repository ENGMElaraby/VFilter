namespace VFilter.Core.Commands;

/// <summary>A command this filter instance claimed (pending → running).</summary>
public sealed record ClaimedCommand(long Id, string Command, string Target, string? PayloadJson, DateTime CreatedAt, DateTime DatabaseNow);

public sealed record CommandOutcome(bool Succeeded, string Result)
{
    public const int MaxResultLength = 500;

    public static CommandOutcome Done(string result) => new(true, Trim(result));

    public static CommandOutcome Failed(string result) => new(false, Trim(result));

    public string Status => Succeeded ? "done" : "failed";

    private static string Trim(string result) => result.Length <= MaxResultLength ? result : result[..(MaxResultLength - 1)] + "…";
}

public interface ICommandRepository
{
    /// <summary>Ids of pending commands, oldest first.</summary>
    Task<IReadOnlyList<long>> GetPendingIdsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Atomically moves a command from pending to running; null if it was cancelled or taken.</summary>
    Task<ClaimedCommand?> TryClaimAsync(long id, CancellationToken cancellationToken);

    /// <summary>Sets done/failed with result and processed_at = database now.</summary>
    Task FinishAsync(long id, CommandOutcome outcome, CancellationToken cancellationToken);
}
