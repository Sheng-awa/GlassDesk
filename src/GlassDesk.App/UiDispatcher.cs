using GlassDesk.Core;
using Microsoft.UI.Dispatching;

namespace GlassDesk.App;

internal sealed class UiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public bool TryEnqueue(Action action)=>queue.TryEnqueue(()=>action());
    public Task<T> InvokeAsync<T>(Func<Task<T>> action)
    {
        if(queue.HasThreadAccess) return action();
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(!queue.TryEnqueue(async ()=>
        {
            try { completion.TrySetResult(await action()); }
            catch(Exception error) { completion.TrySetException(error); }
        })) completion.TrySetException(new InvalidOperationException("The UI dispatcher is shutting down."));
        return completion.Task;
    }
}
