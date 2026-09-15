namespace StS2AP.Utils;

/// <summary>
/// Serializes one player's AP selections in invocation order. Called on the game main thread;
/// awaits preserve its synchronization context. A failed selection blocks subsequent work.
/// </summary>
internal sealed class ApRewardSelectionQueue
{
    private Task _tail = Task.CompletedTask;
    private Exception? _failure;

    internal async Task WhenIdle()
    {
        await _tail;
        if (_failure != null)
            throw new InvalidOperationException("An earlier AP reward selection failed.", _failure);
    }

    internal Task<bool> Run(Func<Task<bool>> select)
    {
        Task previous = _tail;
        var completion = new TaskCompletionSource();
        _tail = completion.Task;
        return Execute(previous, select, completion);
    }

    private async Task<bool> Execute(Task previous, Func<Task<bool>> select, TaskCompletionSource completion)
    {
        try
        {
            await previous;
            if (_failure != null)
                throw new InvalidOperationException("An earlier AP reward selection failed.", _failure);
            return await select();
        }
        catch (Exception ex)
        {
            _failure ??= ex;
            throw;
        }
        finally
        {
            completion.SetResult();
        }
    }
}
