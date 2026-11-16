namespace StS2AP.Utils;

/// <summary>
/// The deterministic part of a queued AP gold claim. Every peer evaluates it inside the same
/// native action, against the cursor that only that action advances, so every peer accepts or
/// refuses the same claims and grants the same amount without asking the host first.
/// </summary>
internal static class QueuedGoldClaimRule
{
    internal readonly record struct Decision(
        bool Accepted,
        int GrantedAmount,
        int CursorAfter,
        string? RefusalReason
    )
    {
        internal static Decision Refuse(string reason) => new(false, 0, 0, reason);
    }

    /// <param name="actionCursor">
    /// Raw AP gold this player has redeemed through queued claims this run, or null before the
    /// first one. It is a floor rather than an exact match, so a claim made after gold was
    /// redeemed some other way is still accepted, while a repeated or overtaken claim is not.
    /// </param>
    /// <param name="cursorBefore">The owner's raw redeemed gold when the claim was made.</param>
    /// <param name="sourceAmount">Raw AP gold this claim redeems.</param>
    /// <param name="poverty">Whether the run's shared Ascensions include Poverty when it executes.</param>
    internal static Decision Evaluate(int? actionCursor, int cursorBefore, int sourceAmount, bool poverty)
    {
        if (sourceAmount <= 0)
            return Decision.Refuse($"it redeems no gold ({sourceAmount})");
        if (cursorBefore < 0)
            return Decision.Refuse($"its starting cursor {cursorBefore} is negative");
        if (cursorBefore > int.MaxValue - sourceAmount)
            return Decision.Refuse("its cursor would overflow");
        if (actionCursor is int floor && cursorBefore < floor)
        {
            return Decision.Refuse(
                $"gold up to {floor} was already claimed, and this claim starts at {cursorBefore}"
            );
        }

        int cursorAfter = cursorBefore + sourceAmount;
        int granted = poverty
            ? ApplyPoverty(cursorAfter) - ApplyPoverty(cursorBefore)
            : sourceAmount;
        return new Decision(true, granted, cursorAfter, null);
    }

    /// <summary>
    /// Poverty pays three quarters of the cumulative raw gold, so a run of small claims loses no
    /// more to rounding than one large claim would.
    /// </summary>
    internal static int ApplyPoverty(int rawAmount) => (int)((long)rawAmount * 3 / 4);
}
